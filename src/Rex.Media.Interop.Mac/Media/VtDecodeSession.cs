using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using static Rex.Media.Interop.Mac.Media.AppleMedia;

namespace Rex.Media.Interop.Mac.Media;

/// <summary>Which of VideoToolbox's decoders a session is for.</summary>
public enum VtCodec
{
    H264,
    Hevc,
}

/// <summary>
/// A decoded picture while VideoToolbox still holds it: its planes' rows can be read until the
/// handler it was given to returns. Only valid inside that handler.
/// </summary>
[SupportedOSPlatform("macos")]
public readonly unsafe ref struct VtPicture
{
    private readonly nint _buffer;

    internal VtPicture(nint buffer, long presentationTicks, long durationTicks, bool hasTime)
    {
        _buffer = buffer;
        PresentationTicks = presentationTicks;
        DurationTicks = durationTicks;
        HasTime = hasTime;
    }

    public long PresentationTicks { get; }

    public long DurationTicks { get; }

    public bool HasTime { get; }

    public bool IsP010 => CVPixelBufferGetPixelFormatType(_buffer) is PixelFormatP010 or PixelFormatP010Full;

    public int PlaneCount => (int)CVPixelBufferGetPlaneCount(_buffer);

    public int PlaneWidth(int plane) => (int)CVPixelBufferGetWidthOfPlane(_buffer, (nuint)plane);

    public int PlaneHeight(int plane) => (int)CVPixelBufferGetHeightOfPlane(_buffer, (nuint)plane);

    /// <summary>One row of a plane, <paramref name="bytes"/> long (the stride may be wider).</summary>
    public ReadOnlySpan<byte> Row(int plane, int row, int bytes)
    {
        var height = PlaneHeight(plane);
        var stride = (int)CVPixelBufferGetBytesPerRowOfPlane(_buffer, (nuint)plane);
        if ((uint)row >= (uint)height || bytes < 0 || bytes > stride)
        {
            throw new ArgumentOutOfRangeException(nameof(row), "The row is outside the picture.");
        }

        return new ReadOnlySpan<byte>((byte*)CVPixelBufferGetBaseAddressOfPlane(_buffer, (nuint)plane) + ((nint)row * stride), bytes);
    }
}

/// <summary>Takes a decoded picture; called while VideoToolbox holds it.</summary>
public delegate void VtPictureHandler(VtPicture picture);

/// <summary>
/// One VideoToolbox decompression session for H.264 or HEVC samples stored with length prefixes
/// (as MP4 and Matroska keep them). Humble (ADR-0008): it turns packets into CoreMedia samples,
/// decodes synchronously, and hands each picture to the handler while it is locked for reading;
/// ordering, cropping and copying are the caller's. Times are in 100-nanosecond ticks.
/// </summary>
[SupportedOSPlatform("macos")]
public sealed unsafe class VtDecodeSession : IDisposable
{
    private const int Timescale = 10_000_000;

    private readonly VtPictureHandler _handler;
    private GCHandle _self;
    private nint _format;
    private nint _session;
    private Exception? _failure;

    private VtDecodeSession(VtPictureHandler handler) => _handler = handler;

    /// <summary>Whether the session decodes on the graphics hardware rather than in software.</summary>
    public bool OnHardware { get; private set; }

    /// <summary>
    /// Opens a session for the parameter sets (SPS then PPS for H.264; VPS, SPS, PPS for HEVC)
    /// whose samples prefix each NAL unit with <paramref name="lengthSize"/> bytes, giving
    /// pictures in P010 when <paramref name="tenBit"/>, else NV12. The pictures keep the stream's own
    /// sample range (<paramref name="fullRange"/>): asked for the other, VideoToolbox would convert every
    /// sample, and the picture would no longer be the one the stream holds.
    /// </summary>
    public static VtDecodeSession Open(VtCodec codec, IReadOnlyList<byte[]> parameterSets, int lengthSize, bool tenBit, bool fullRange, VtPictureHandler handler, bool allowHardware = true)
    {
        ArgumentNullException.ThrowIfNull(parameterSets);
        ArgumentNullException.ThrowIfNull(handler);
        var session = new VtDecodeSession(handler);
        try
        {
            session._format = Describe(codec, parameterSets, lengthSize);
            var attributes = PixelAttributes((tenBit, fullRange) switch
            {
                (true, true) => PixelFormatP010Full,
                (true, false) => PixelFormatP010,
                (false, true) => PixelFormatNv12Full,
                (false, false) => PixelFormatNv12,
            });
            var specification = HardwareAttributes(allowHardware);
            try
            {
                session._self = GCHandle.Alloc(session);
                var record = new OutputCallbackRecord { Callback = &Decoded, Reference = GCHandle.ToIntPtr(session._self) };
                Check(VTDecompressionSessionCreate(0, session._format, specification, attributes,
                    &record, out session._session), "Opening the VideoToolbox decoder");
            }
            finally
            {
                CFRelease(specification);
                CFRelease(attributes);
            }

            if (VTSessionCopyProperty(session._session, UsingHardwareKey, 0, out var hardware) == 0 && hardware != 0)
            {
                session.OnHardware = hardware == True;
                CFRelease(hardware);
            }

            return session;
        }
        catch
        {
            session.Dispose();
            throw;
        }
    }

    /// <summary>Decodes one sample; pictures it completes go to the handler before this returns.</summary>
    public void Decode(ReadOnlySpan<byte> sample, long presentationTicks, long durationTicks, bool hasTime)
    {
        Check(CMBlockBufferCreateWithMemoryBlock(0, null, (nuint)sample.Length, 0, null, 0, (nuint)sample.Length, BlockBufferAssureMemoryNow, out var block), "Holding a sample");
        try
        {
            fixed (byte* data = sample)
            {
                Check(CMBlockBufferReplaceDataBytes(data, block, 0, (nuint)sample.Length), "Copying a sample");
            }

            var timing = new CMSampleTimingInfo
            {
                Duration = Time(durationTicks, durationTicks > 0),
                PresentationTimeStamp = Time(presentationTicks, hasTime),
                DecodeTimeStamp = Time(0, valid: false),
            };
            var size = (nuint)sample.Length;
            Check(CMSampleBufferCreateReady(0, block, _format, 1, 1, &timing, 1, &size, out var buffer), "Describing a sample");
            try
            {
                Check(VTDecompressionSessionDecodeFrame(_session, buffer, DecodeEnableTemporalProcessing, 0, out _), "Decoding a sample");
            }
            finally
            {
                CFRelease(buffer);
            }
        }
        finally
        {
            CFRelease(block);
        }

        Rethrow();
    }

    /// <summary>Emits every picture the decoder still holds back, as at the end of a stream.</summary>
    public void Finish()
    {
        Check(VTDecompressionSessionFinishDelayedFrames(_session), "Finishing the stream");
        Check(VTDecompressionSessionWaitForAsynchronousFrames(_session), "Waiting for the last pictures");
        Rethrow();
    }

    public void Dispose()
    {
        if (_session != 0)
        {
            VTDecompressionSessionInvalidate(_session);
            CFRelease(_session);
            _session = 0;
        }

        if (_format != 0)
        {
            CFRelease(_format);
            _format = 0;
        }

        if (_self.IsAllocated)
        {
            _self.Free();
        }
    }

    private static CMTime Time(long ticks, bool valid) => new() { Value = ticks, Timescale = Timescale, Flags = valid ? TimeFlagsValid : 0 };

    private static nint Describe(VtCodec codec, IReadOnlyList<byte[]> sets, int lengthSize)
    {
        var handles = sets.Select(set => GCHandle.Alloc(set, GCHandleType.Pinned)).ToArray();
        try
        {
            var pointers = stackalloc byte*[sets.Count];
            var sizes = stackalloc nuint[sets.Count];
            for (var i = 0; i < sets.Count; i++)
            {
                pointers[i] = (byte*)handles[i].AddrOfPinnedObject();
                sizes[i] = (nuint)sets[i].Length;
            }

            nint description;
            var status = codec == VtCodec.H264
                ? CMVideoFormatDescriptionCreateFromH264ParameterSets(0, (nuint)sets.Count, pointers, sizes, lengthSize, out description)
                : CMVideoFormatDescriptionCreateFromHEVCParameterSets(0, (nuint)sets.Count, pointers, sizes, lengthSize, 0, out description);
            Check(status, "Reading the parameter sets");
            return description;
        }
        finally
        {
            foreach (var handle in handles)
            {
                handle.Free();
            }
        }
    }

    private static nint PixelAttributes(uint pixelFormat)
    {
        var value = (int)pixelFormat;
        var number = CFNumberCreate(0, NumberSInt32, &value);
        try
        {
            var key = PixelFormatKey;
            return CFDictionaryCreate(0, &key, &number, 1, KeyCallBacks, ValueCallBacks);
        }
        finally
        {
            CFRelease(number);
        }
    }

    /// <summary>Apple's VideoToolbox policy: prefer hardware when allowed, force software when off.</summary>
    private static nint HardwareAttributes(bool allowHardware)
    {
        var key = AllowHardwareDecodeKey;
        var value = allowHardware ? True : False;
        return CFDictionaryCreate(0, &key, &value, 1, KeyCallBacks, ValueCallBacks);
    }

    private void Rethrow()
    {
        if (_failure is { } failure)
        {
            _failure = null;
            throw new IOException("Decoding a picture failed: " + failure.Message, failure);
        }
    }

    [UnmanagedCallersOnly]
    private static void Decoded(nint reference, nint frameReference, int status, uint flags, nint image, CMTime presentation, CMTime duration)
    {
        var session = (VtDecodeSession)GCHandle.FromIntPtr(reference).Target!;
        if (status != 0 || image == 0)
        {
            // A dropped frame (status non-zero) or one the decoder chose not to output.
            session._failure ??= status != 0 ? new IOException($"VideoToolbox reported OSStatus {status}.") : null;
            return;
        }

        if (CVPixelBufferLockBaseAddress(image, LockReadOnly) != 0)
        {
            session._failure ??= new IOException("A decoded picture could not be read.");
            return;
        }

        try
        {
            var hasTime = (presentation.Flags & TimeFlagsValid) != 0;
            session._handler(new VtPicture(image, hasTime ? Ticks(presentation) : 0, (duration.Flags & TimeFlagsValid) != 0 ? Ticks(duration) : 0, hasTime));
        }
        catch (Exception ex)
        {
            // An exception must never cross into VideoToolbox's frames: it is rethrown on the caller's side.
            session._failure ??= ex;
        }
        finally
        {
            _ = CVPixelBufferUnlockBaseAddress(image, LockReadOnly);
        }
    }

    private static long Ticks(CMTime time) => time.Timescale == Timescale ? time.Value : (long)((Int128)time.Value * Timescale / time.Timescale);
}
