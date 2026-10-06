using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Media.MediaFoundation;

namespace Rex.Media.Interop.MediaFoundation;

/// <summary>What one call to <see cref="MfTransform.ProcessOutput"/> produced.</summary>
public enum MfOutputStatus
{
    /// <summary>A sample came out.</summary>
    Sample,

    /// <summary>The transform needs more input before it can produce anything.</summary>
    NeedMoreInput,

    /// <summary>The output format changed; the caller picks a new output type and asks again.</summary>
    FormatChanged,
}

/// <summary>A decoded sample's bytes and timing, in 100-nanosecond units as Media Foundation keeps them.</summary>
public readonly record struct MfSample(byte[] Data, long Time, long Duration, bool HasTime);

/// <summary>
/// A synchronous Media Foundation transform, used as a decoder. Call sequencing and COM plumbing only
/// (ADR-008): which transform, which formats and what to do with the output are decided by the
/// decoder adapters in Rex.Media.Codecs.MediaFoundation, the only callers.
/// </summary>
[SupportedOSPlatform("windows6.1")]
public sealed unsafe class MfTransform : IDisposable
{
    private const uint ProvidesSamples = 0x100;
    private const uint CanProvideSamples = 0x200;

    private static readonly Lazy<bool> Started = new(() => PInvoke.MFStartup(PInvoke.MF_VERSION, PInvoke.MFSTARTUP_LITE).Succeeded);

    private readonly IMFActivate _activate;
    private readonly IMFTransform _transform;
    private bool _disposed;

    private MfTransform(IMFActivate activate, IMFTransform transform, string name)
    {
        _activate = activate;
        _transform = transform;
        Name = name;
    }

    /// <summary>The transform's friendly name, as Windows registered it.</summary>
    public string Name { get; }

    /// <summary>Starts Media Foundation for the process; false when it is not installed (Windows N without the Media Feature Pack).</summary>
    public static bool IsAvailable => Started.Value;

    /// <summary>
    /// The friendly names of every transform in a category that takes the input subtype: the
    /// synchronous software ones, or with <paramref name="hardware"/> those on the graphics card.
    /// </summary>
    public static IReadOnlyList<string> Names(Guid category, Guid majorType, Guid inputSubtype, bool hardware = false)
    {
        var names = new List<string>();
        foreach (var activate in Enumerate(category, majorType, inputSubtype, hardware))
        {
            names.Add(FriendlyName(activate));
            Marshal.ReleaseComObject(activate);
        }

        return names;
    }

    /// <summary>The first synchronous software transform in a category that takes the input subtype and activates, or null.</summary>
    public static MfTransform? Create(Guid category, Guid majorType, Guid inputSubtype)
    {
        MfTransform? created = null;
        foreach (var activate in Enumerate(category, majorType, inputSubtype, hardware: false))
        {
            if (created is null)
            {
                try
                {
                    var id = typeof(IMFTransform).GUID;
                    activate.ActivateObject(&id, out var pointer);
                    var transform = (IMFTransform)Marshal.GetObjectForIUnknown((nint)pointer);
                    Marshal.Release((nint)pointer);
                    created = new MfTransform(activate, transform, FriendlyName(activate));
                    continue;
                }
                catch (COMException)
                {
                    // This one would not start; the next candidate may.
                }
            }

            Marshal.ReleaseComObject(activate);
        }

        return created;
    }

    /// <summary>Sets the input format from attribute values: uint, ulong, Guid or byte[].</summary>
    public void SetInputType(IReadOnlyDictionary<Guid, object> attributes)
    {
        ArgumentNullException.ThrowIfNull(attributes);
        PInvoke.MFCreateMediaType(out var type).ThrowOnFailure();
        try
        {
            foreach (var (key, value) in attributes)
            {
                var id = key;
                switch (value)
                {
                    case uint number:
                        type.SetUINT32(&id, number);
                        break;
                    case ulong number:
                        type.SetUINT64(&id, number);
                        break;
                    case Guid guid:
                        type.SetGUID(&id, &guid);
                        break;
                    case byte[] blob:
                        type.SetBlob(&id, blob, (uint)blob.Length);
                        break;
                    default:
                        throw new ArgumentException($"Attribute {key} has a value of type {value.GetType().Name}, which a media type cannot hold.", nameof(attributes));
                }
            }

            _transform.SetInputType(0, type, 0);
        }
        finally
        {
            Marshal.ReleaseComObject(type);
        }
    }

    /// <summary>
    /// Offers each output type the transform proposes, in its order, to <paramref name="accept"/>,
    /// which reads the type's attributes and says yes or no. The first accepted type is set.
    /// </summary>
    public bool SetOutputType(Func<MfMediaType, bool> accept)
    {
        ArgumentNullException.ThrowIfNull(accept);
        for (uint index = 0; ; index++)
        {
            IMFMediaType type;
            try
            {
                _transform.GetOutputAvailableType(0, index, out type);
            }
            catch (COMException)
            {
                // MF_E_NO_MORE_TYPES, or a transform that cannot offer types yet.
                return false;
            }

            try
            {
                if (accept(new MfMediaType(type)))
                {
                    _transform.SetOutputType(0, type, 0);
                    return true;
                }
            }
            finally
            {
                Marshal.ReleaseComObject(type);
            }
        }
    }

    /// <summary>Reads the current output type's attributes.</summary>
    public T ReadOutputType<T>(Func<MfMediaType, T> read)
    {
        ArgumentNullException.ThrowIfNull(read);
        _transform.GetOutputCurrentType(0, out var type);
        try
        {
            return read(new MfMediaType(type));
        }
        finally
        {
            Marshal.ReleaseComObject(type);
        }
    }

    /// <summary>Tells the transform a stream is starting, as a pipeline does before the first sample.</summary>
    public void Begin()
    {
        _transform.ProcessMessage(MFT_MESSAGE_TYPE.MFT_MESSAGE_NOTIFY_BEGIN_STREAMING, 0);
        _transform.ProcessMessage(MFT_MESSAGE_TYPE.MFT_MESSAGE_NOTIFY_START_OF_STREAM, 0);
    }

    /// <summary>Feeds one compressed sample; false when the transform must give output first.</summary>
    public bool ProcessInput(ReadOnlySpan<byte> data, long time, long duration, bool keyframe, bool discontinuity)
    {
        PInvoke.MFCreateMemoryBuffer((uint)Math.Max(1, data.Length), out var buffer).ThrowOnFailure();
        PInvoke.MFCreateSample(out var sample).ThrowOnFailure();
        try
        {
            byte* bytes;
            buffer.Lock(&bytes, null, null);
            data.CopyTo(new Span<byte>(bytes, data.Length));
            buffer.Unlock();
            buffer.SetCurrentLength((uint)data.Length);
            sample.AddBuffer(buffer);
            if (time != long.MinValue)
            {
                sample.SetSampleTime(time);
            }

            if (duration > 0)
            {
                sample.SetSampleDuration(duration);
            }

            SetFlag(sample, PInvoke.MFSampleExtension_CleanPoint, keyframe);
            SetFlag(sample, PInvoke.MFSampleExtension_Discontinuity, discontinuity);
            try
            {
                _transform.ProcessInput(0, sample, 0);
                return true;
            }
            catch (COMException ex) when (ex.HResult == HRESULT.MF_E_NOTACCEPTING.Value)
            {
                return false;
            }
        }
        finally
        {
            Marshal.ReleaseComObject(sample);
            Marshal.ReleaseComObject(buffer);
        }
    }

    /// <summary>Asks for one output sample.</summary>
    public MfOutputStatus ProcessOutput(out MfSample output)
    {
        output = default;
        MFT_OUTPUT_STREAM_INFO info;
        _transform.GetOutputStreamInfo(0, &info);
        IMFSample? provided = null;
        if ((info.dwFlags & (ProvidesSamples | CanProvideSamples)) == 0)
        {
            PInvoke.MFCreateMemoryBuffer(Math.Max(info.cbSize, 1u), out var buffer).ThrowOnFailure();
            PInvoke.MFCreateSample(out provided).ThrowOnFailure();
            provided.AddBuffer(buffer);
            Marshal.ReleaseComObject(buffer);
        }

        var buffers = new MFT_OUTPUT_DATA_BUFFER[] { new() { dwStreamID = 0, pSample = provided! } };
        try
        {
            _transform.ProcessOutput(0, 1, buffers, out _);
        }
        catch (COMException ex) when (ex.HResult == HRESULT.MF_E_TRANSFORM_NEED_MORE_INPUT.Value)
        {
            Release(provided, buffers[0]);
            return MfOutputStatus.NeedMoreInput;
        }
        catch (COMException ex) when (ex.HResult == HRESULT.MF_E_TRANSFORM_STREAM_CHANGE.Value)
        {
            Release(provided, buffers[0]);
            return MfOutputStatus.FormatChanged;
        }

        var result = buffers[0].pSample;
        try
        {
            result.ConvertToContiguousBuffer(out var contiguous);
            try
            {
                byte* bytes;
                uint length;
                contiguous.Lock(&bytes, null, &length);
                var data = new ReadOnlySpan<byte>(bytes, (int)length).ToArray();
                contiguous.Unlock();
                long time = 0, duration = 0;
                var hasTime = true;
                try
                {
                    result.GetSampleTime(out time);
                }
                catch (COMException)
                {
                    // The transform left the time unset; the caller continues from the last one.
                    hasTime = false;
                }

                try
                {
                    result.GetSampleDuration(out duration);
                }
                catch (COMException)
                {
                    // No duration: the caller derives it from the data.
                }

                output = new MfSample(data, time, duration, hasTime);
                return MfOutputStatus.Sample;
            }
            finally
            {
                Marshal.ReleaseComObject(contiguous);
            }
        }
        finally
        {
            Release(provided, buffers[0]);
        }
    }

    /// <summary>Asks the transform to give up everything it holds; follow with <see cref="ProcessOutput"/> until it needs input.</summary>
    public void Drain() => _transform.ProcessMessage(MFT_MESSAGE_TYPE.MFT_MESSAGE_COMMAND_DRAIN, 0);

    /// <summary>Discards everything the transform holds, as after a seek.</summary>
    public void Flush() => _transform.ProcessMessage(MFT_MESSAGE_TYPE.MFT_MESSAGE_COMMAND_FLUSH, 0);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            _transform.ProcessMessage(MFT_MESSAGE_TYPE.MFT_MESSAGE_NOTIFY_END_OF_STREAM, 0);
            _transform.ProcessMessage(MFT_MESSAGE_TYPE.MFT_MESSAGE_NOTIFY_END_STREAMING, 0);
            _activate.ShutdownObject();
        }
        catch (COMException)
        {
            // A transform that fails to say goodbye is released all the same.
        }

        Marshal.ReleaseComObject(_transform);
        Marshal.ReleaseComObject(_activate);
    }

    private static IEnumerable<IMFActivate> Enumerate(Guid category, Guid majorType, Guid inputSubtype, bool hardware)
    {
        if (!IsAvailable)
        {
            return [];
        }

        var input = new MFT_REGISTER_TYPE_INFO { guidMajorType = majorType, guidSubtype = inputSubtype };
        var flags = hardware
            ? MFT_ENUM_FLAG.MFT_ENUM_FLAG_HARDWARE | MFT_ENUM_FLAG.MFT_ENUM_FLAG_SORTANDFILTER
            : MFT_ENUM_FLAG.MFT_ENUM_FLAG_SYNCMFT | MFT_ENUM_FLAG.MFT_ENUM_FLAG_LOCALMFT | MFT_ENUM_FLAG.MFT_ENUM_FLAG_SORTANDFILTER;
        PInvoke.MFTEnumEx(category, flags, input, null, out var array, out var count).ThrowOnFailure();
        var activates = new List<IMFActivate>((int)count);
        try
        {
            for (var i = 0; i < count; i++)
            {
                activates.Add((IMFActivate)Marshal.GetObjectForIUnknown((nint)array[i]));
                Marshal.Release((nint)array[i]);
            }
        }
        finally
        {
            PInvoke.CoTaskMemFree(array);
        }

        return activates;
    }

    private static string FriendlyName(IMFActivate activate)
    {
        var key = PInvoke.MFT_FRIENDLY_NAME_Attribute;
        try
        {
            PWSTR text;
            activate.GetAllocatedString(&key, &text, out _);
            try
            {
                return text.ToString();
            }
            finally
            {
                PInvoke.CoTaskMemFree(text.Value);
            }
        }
        catch (COMException)
        {
            return "Windows decoder";
        }
    }

    private static void SetFlag(IMFSample sample, Guid key, bool value)
    {
        if (value)
        {
            sample.SetUINT32(&key, 1);
        }
    }

    private static void Release(IMFSample? provided, MFT_OUTPUT_DATA_BUFFER buffer)
    {
        if (buffer.pEvents is not null)
        {
            Marshal.ReleaseComObject(buffer.pEvents);
        }

        if (buffer.pSample is not null)
        {
            Marshal.ReleaseComObject(buffer.pSample);
        }

        if (provided is not null && !ReferenceEquals(provided, buffer.pSample))
        {
            Marshal.ReleaseComObject(provided);
        }
    }
}

/// <summary>A media type a transform offered, read through its attributes.</summary>
[SupportedOSPlatform("windows6.1")]
public sealed unsafe class MfMediaType
{
    private readonly IMFMediaType _type;

    internal MfMediaType(IMFMediaType type) => _type = type;

    public Guid Subtype => Guid(PInvoke.MF_MT_SUBTYPE) ?? System.Guid.Empty;

    public uint? UInt32(Guid key)
    {
        try
        {
            _type.GetUINT32(&key, out var value);
            return value;
        }
        catch (COMException)
        {
            return null;
        }
    }

    public ulong? UInt64(Guid key)
    {
        try
        {
            _type.GetUINT64(&key, out var value);
            return value;
        }
        catch (COMException)
        {
            return null;
        }
    }

    public Guid? Guid(Guid key)
    {
        try
        {
            Guid value;
            _type.GetGUID(&key, &value);
            return value;
        }
        catch (COMException)
        {
            return null;
        }
    }
}
