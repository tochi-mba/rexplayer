using System.Runtime.InteropServices;
using Rex.Media.Codecs.H264;
using Rex.Media.Codecs.Hevc;
using Rex.Media.Codecs.Video;
using Rex.Media.Interop.Graphics;
using Rex.Media.Interop.MediaFoundation;
using Rex.Media.Primitives;
using Windows.Win32;

namespace Rex.Media.Codecs.MediaFoundation;

/// <summary>
/// One Media Foundation video decoder transform (H.264, HEVC or VP9), producing NV12 pictures, or
/// P010 for streams deeper than 8 bits. Given the presenter's Direct3D 11 device, the transform
/// decodes on the graphics card and its pictures stay there (VID-01); otherwise it decodes in
/// software into memory. Windows' H.264 and HEVC decoders take Annex B, so their samples stored
/// with length prefixes are rewritten, with the parameter sets put in front of each keyframe.
/// </summary>
public sealed class MfVideoDecoder : IVideoDecoder
{
    /// <summary>How long to wait for a busy decoder before trying input again, and how long before giving up.</summary>
    private static readonly TimeSpan StallWait = TimeSpan.FromMilliseconds(1);
    private static readonly TimeSpan StallLimit = TimeSpan.FromSeconds(5);

    private readonly MfTransform _transform;
    private readonly Queue<Input> _waiting = new();
    private bool _draining;
    private readonly int _lengthSize;
    private readonly byte[] _parameterSets;
    private readonly int _displayWidth;
    private readonly int _displayHeight;
    private readonly PixelFormat _format;
    private readonly Guid _outputSubtype;
    private readonly ColorInfo _color;
    private readonly Rational _aspect;
    private int _codedWidth;
    private int _codedHeight;
    private int _stride;

    public MfVideoDecoder(TrackInfo track)
        : this(track, null)
    {
    }

    /// <summary>A decoder that decodes on the graphics card when <paramref name="gpu"/> is a Direct3D 11 device it can use.</summary>
    public MfVideoDecoder(TrackInfo track, object? gpu)
    {
        ArgumentNullException.ThrowIfNull(track);
        var video = track.Video ?? throw new MediaFormatException("The track is not video.");
        var subtype = MfDecoderFactory.VideoSubtype(track.Codec) ?? throw new MediaFormatException($"Windows is not asked to decode {track.Codec}.");
        SequenceInfo? sequence = null;
        if (track.CodecPrivate.Length > 0)
        {
            if (track.Codec == CodecId.H264)
            {
                var config = AvcConfig.Parse(track.CodecPrivate);
                (_lengthSize, _parameterSets, sequence) = (config.LengthSize, config.ParameterSetsAnnexB(), config.Sequence());
            }
            else if (track.Codec == CodecId.Hevc)
            {
                var config = HevcConfig.Parse(track.CodecPrivate);
                (_lengthSize, _parameterSets, sequence) = (config.LengthSize, config.ParameterSetsAnnexB(), config.Sequence());
            }
            else
            {
                _parameterSets = [];
            }
        }
        else
        {
            _parameterSets = [];
        }

        _displayWidth = video.Width;
        _displayHeight = video.Height;
        _color = video.Color != ColorInfo.Unspecified || sequence is null ? video.Color : sequence.Color;
        _aspect = video.PixelAspect;
        (_format, _outputSubtype) = (sequence?.BitDepth ?? 8) > 8 ? (PixelFormat.P010, PInvoke.MFVideoFormat_P010) : (PixelFormat.Nv12, PInvoke.MFVideoFormat_NV12);
        _transform = MfTransform.Create(PInvoke.MFT_CATEGORY_VIDEO_DECODER, PInvoke.MFMediaType_Video, subtype)
            ?? throw new MediaFormatException($"Windows has no decoder for {track.Codec}.");
        try
        {
            var inputType = new Dictionary<Guid, object>
            {
                [PInvoke.MF_MT_MAJOR_TYPE] = PInvoke.MFMediaType_Video,
                [PInvoke.MF_MT_SUBTYPE] = subtype,
                [PInvoke.MF_MT_FRAME_SIZE] = ((ulong)(uint)video.Width << 32) | (uint)video.Height,
            };
            if (track.Codec == CodecId.Hevc && sequence is { Profile: > 0 })
            {
                // The Windows HEVC decoder requires Main10 to be declared before it will offer
                // P010. The profile_idc values carried by HEVC are the values this attribute uses.
                inputType[PInvoke.MF_MT_VIDEO_PROFILE] = (uint)sequence.Profile;
            }

            _transform.SetInputType(inputType);
            if (OperatingSystem.IsWindowsVersionAtLeast(8) && gpu is D3D11Gpu { IsSoftware: false } device)
            {
                _transform.AttachGpu(device);
            }

            ChooseOutput();
            _transform.Begin();
        }
        catch (COMException ex)
        {
            _transform.Dispose();
            throw new MediaFormatException($"The Windows {track.Codec} decoder refused the track's format (0x{ex.HResult:X8}).", ex);
        }
    }

    public string Name => _transform.Name;

    public DecoderSource Source => _transform.OnGraphicsCard ? DecoderSource.OsHardware : DecoderSource.OsSoftware;

    public void Decode(Packet packet, ICollection<VideoFrame> output)
    {
        ArgumentNullException.ThrowIfNull(packet);
        ArgumentNullException.ThrowIfNull(output);
        var data = _lengthSize > 0
            ? NalUnits.ToAnnexB(packet.Data.Span, _lengthSize, packet.IsKeyframe ? _parameterSets : default)
            : packet.Data.Span.ToArray();
        _waiting.Enqueue(new Input(data, packet.Pts.IsKnown ? packet.Pts.Ticks : long.MinValue, packet.Duration.Ticks, packet.IsKeyframe));
        Pump(output);
    }

    public bool Drain(ICollection<VideoFrame> output)
    {
        ArgumentNullException.ThrowIfNull(output);
        Pump(output);
        if (_waiting.Count > 0)
        {
            // Input still waits for surfaces the caller holds: it shows these pictures and calls again.
            return false;
        }

        try
        {
            if (!_draining)
            {
                _transform.Drain();
                _draining = true;
            }

            if (!Collect(output))
            {
                return false;
            }

            _transform.Begin();
            _draining = false;
            return true;
        }
        catch (COMException ex)
        {
            throw new MediaFormatException($"The Windows decoder could not finish the stream (0x{ex.HResult:X8}).", ex);
        }
    }

    public void Flush()
    {
        _waiting.Clear();
        _draining = false;
        _transform.Flush();
    }

    public void Dispose() => _transform.Dispose();

    /// <summary>
    /// Feeds waiting input. A decoder on the graphics card refuses input while every surface in its pool
    /// holds a picture, and a surface comes back only once its picture has been shown and released; so
    /// when input is refused after pictures came out, the input waits here and the pictures go to the
    /// caller. When nothing came out either, the card is still decoding: wait a moment and try again.
    /// </summary>
    private void Pump(ICollection<VideoFrame> output)
    {
        try
        {
            var stalled = TimeSpan.Zero;
            while (_waiting.TryPeek(out var input))
            {
                if (_transform.ProcessInput(input.Data, input.Time, input.Duration, input.Keyframe, discontinuity: false))
                {
                    _waiting.Dequeue();
                    stalled = TimeSpan.Zero;
                    continue;
                }

                var before = output.Count;
                Collect(output);
                if (output.Count > before)
                {
                    return;
                }

                if (output.Count == before)
                {
                    if (stalled >= StallLimit)
                    {
                        throw new MediaFormatException("The Windows decoder stopped taking input.");
                    }

                    Thread.Sleep(StallWait);
                    stalled += StallWait;
                }
            }

            Collect(output);
        }
        catch (COMException ex)
        {
            throw new MediaFormatException($"The Windows decoder could not decode a packet (0x{ex.HResult:X8}).", ex);
        }
    }

    private void ChooseOutput()
    {
        if (!_transform.SetOutputType(type => type.Subtype == _outputSubtype))
        {
            throw new MediaFormatException($"The Windows decoder offers no {_format} output.");
        }

        (_codedWidth, _codedHeight, _stride) = _transform.ReadOutputType(type =>
        {
            var size = type.UInt64(PInvoke.MF_MT_FRAME_SIZE) ?? 0;
            var width = (int)(size >> 32);
            var stride = (int)(type.UInt32(PInvoke.MF_MT_DEFAULT_STRIDE) ?? 0);
            return (width, (int)(uint)size, stride > 0 ? stride : width * (_format.StorageBits() / 8));
        });
    }

    /// <summary>
    /// Takes the pictures the transform has ready, cropped to the display size; true once it has none
    /// left. On the graphics card it takes one at a time: there, asking for a picture while the caller
    /// still holds too many surfaces does not fail but waits for a surface that can never come back.
    /// </summary>
    private bool Collect(ICollection<VideoFrame> output)
    {
        while (true)
        {
            switch (_transform.ProcessOutput(out var sample))
            {
                case MfOutputStatus.NeedMoreInput:
                    return true;
                case MfOutputStatus.FormatChanged:
                    ChooseOutput();
                    continue;
            }

            var width = Math.Min(_displayWidth > 0 ? _displayWidth : _codedWidth, _codedWidth);
            var height = Math.Min(_displayHeight > 0 ? _displayHeight : _codedHeight, _codedHeight);
            if (sample.Surface is { } surface)
            {
                var onCard = VideoFrame.OnGraphicsCard(_format, width, height, surface);
                Stamp(onCard, sample);
                output.Add(onCard);
                return false;
            }

            if (sample.Data.Length < _stride * _codedHeight * 3 / 2)
            {
                throw new MediaFormatException("The Windows decoder returned a picture smaller than its format.");
            }

            var frame = VideoFrame.Rent(_format, width, height);
            for (var plane = 0; plane < 2; plane++)
            {
                var (rowBytes, rows) = _format.PlaneSize(plane, width, height);
                var start = plane == 0 ? 0 : _stride * _codedHeight;
                for (var row = 0; row < rows; row++)
                {
                    sample.Data.AsSpan(start + (row * _stride), rowBytes).CopyTo(frame.Row(plane, row));
                }
            }

            Stamp(frame, sample);
            output.Add(frame);
        }
    }

    private void Stamp(VideoFrame frame, MfSample sample)
    {
        frame.Pts = sample.HasTime ? new MediaTime(sample.Time) : MediaTime.Unknown;
        frame.Duration = new MediaTime(sample.Duration);
        frame.Color = _color;
        frame.PixelAspect = _aspect;
    }

    /// <summary>A packet in the form the decoder takes, waiting for it to accept input.</summary>
    private readonly record struct Input(byte[] Data, long Time, long Duration, bool Keyframe);
}
