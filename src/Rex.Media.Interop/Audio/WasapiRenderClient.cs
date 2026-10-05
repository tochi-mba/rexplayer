using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Media.Audio;
using Windows.Win32.System.Com;

namespace Rex.Media.Interop.Audio;

/// <summary>The endpoint's shared-mode mix format, copied out of the native structure.</summary>
public sealed record WasapiMixFormat(int SampleRate, int Channels, int BitsPerSample, int ValidBitsPerSample, uint ChannelMask, bool IsFloat, int BlockAlign);

/// <summary>
/// A shared-mode, event-driven WASAPI render stream on an endpoint. Call sequencing and pointer
/// handling only (ADR-008): every decision about formats, buffering and recovery is made by the
/// sink in Rex.Media.Audio.Wasapi, which is the only caller.
/// </summary>
[SupportedOSPlatform("windows6.0.6000")]
public sealed unsafe class WasapiRenderClient : IDisposable
{
    private static readonly Guid FloatSubtype = new("00000003-0000-0010-8000-00aa00389b71");

    private readonly IMMDevice _device;
    private readonly IAudioClient _client;
    private readonly IAudioRenderClient _render;
    private readonly IAudioClock _clock;
    private readonly AutoResetEvent _bufferEvent = new(false);
    private readonly ulong _clockFrequency;
    private bool _disposed;

    private WasapiRenderClient(IMMDevice device, TimeSpan bufferDuration)
    {
        _device = device;
        var clientId = typeof(IAudioClient).GUID;
        _device.Activate(&clientId, CLSCTX.CLSCTX_ALL, null, out var activated);
        _client = (IAudioClient)activated;

        WAVEFORMATEX* mix;
        _client.GetMixFormat(&mix);
        try
        {
            Format = ReadFormat(mix);
            _client.Initialize(AUDCLNT_SHAREMODE.AUDCLNT_SHAREMODE_SHARED, PInvoke.AUDCLNT_STREAMFLAGS_EVENTCALLBACK, bufferDuration.Ticks, 0, mix, null);
        }
        finally
        {
            PInvoke.CoTaskMemFree(mix);
        }

        _client.SetEventHandle(new HANDLE(_bufferEvent.SafeWaitHandle.DangerousGetHandle()));
        _client.GetBufferSize(out var frames);
        BufferFrames = frames;
        _render = GetService<IAudioRenderClient>();
        _clock = GetService<IAudioClock>();
        _clock.GetFrequency(out _clockFrequency);
        DeviceId = ReadId(_device);
    }

    public WasapiMixFormat Format { get; }

    public uint BufferFrames { get; }

    public string DeviceId { get; }

    /// <summary>Signalled by the endpoint each time it has consumed a period of audio.</summary>
    public WaitHandle BufferEvent => _bufferEvent;

    /// <summary>Frames queued in the endpoint buffer and not yet played.</summary>
    public uint Padding
    {
        get
        {
            _client.GetCurrentPadding(out var padding);
            return padding;
        }
    }

    /// <summary>Frames played since the stream was started or last reset.</summary>
    public long PlayedFrames
    {
        get
        {
            _clock.GetPosition(out var position, null);
            return (long)(position * (ulong)Format.SampleRate / Math.Max(1, _clockFrequency));
        }
    }

    /// <summary>Opens the console default render endpoint, or returns null when there is none.</summary>
    public static WasapiRenderClient? OpenDefault(TimeSpan bufferDuration)
    {
        var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
        try
        {
            enumerator.GetDefaultAudioEndpoint(EDataFlow.eRender, ERole.eConsole, out var device);
            return new WasapiRenderClient(device, bufferDuration);
        }
        catch (COMException)
        {
            // No endpoint is plugged in or enabled; the caller plays to the null sink instead.
            return null;
        }
        finally
        {
            Marshal.ReleaseComObject(enumerator);
        }
    }

    /// <summary>Copies <paramref name="frames"/> interleaved frames into the endpoint buffer. The caller ensures they fit.</summary>
    public void Write(ReadOnlySpan<byte> interleaved, int frames)
    {
        var bytes = frames * Format.BlockAlign;
        ArgumentOutOfRangeException.ThrowIfLessThan(interleaved.Length, bytes, nameof(interleaved));
        byte* target;
        _render.GetBuffer((uint)frames, &target);
        interleaved[..bytes].CopyTo(new Span<byte>(target, bytes));
        _render.ReleaseBuffer((uint)frames, 0);
    }

    public void Start() => _client.Start();

    public void Stop() => _client.Stop();

    /// <summary>Discards queued audio and restarts the position at zero. The stream must be stopped.</summary>
    public void Reset() => _client.Reset();

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            _client.Stop();
        }
        catch (COMException)
        {
            // The device may already be gone; releasing it is all that is left to do.
        }

        Marshal.ReleaseComObject(_clock);
        Marshal.ReleaseComObject(_render);
        Marshal.ReleaseComObject(_client);
        Marshal.ReleaseComObject(_device);
        _bufferEvent.Dispose();
    }

    private T GetService<T>()
        where T : class
    {
        var id = typeof(T).GUID;
        void* pointer;
        _client.GetService(&id, out pointer);
        try
        {
            return (T)Marshal.GetObjectForIUnknown((nint)pointer);
        }
        finally
        {
            Marshal.Release((nint)pointer);
        }
    }

    private static WasapiMixFormat ReadFormat(WAVEFORMATEX* format)
    {
        var isExtensible = format->wFormatTag == 0xFFFE && format->cbSize >= 22;
        var extensible = (WAVEFORMATEXTENSIBLE*)format;
        var isFloat = format->wFormatTag == 3 || (isExtensible && extensible->SubFormat == FloatSubtype);
        return new WasapiMixFormat(
            (int)format->nSamplesPerSec,
            format->nChannels,
            format->wBitsPerSample,
            isExtensible ? extensible->Samples.wValidBitsPerSample : format->wBitsPerSample,
            isExtensible ? extensible->dwChannelMask : 0,
            isFloat,
            format->nBlockAlign);
    }

    private static string ReadId(IMMDevice device)
    {
        PWSTR id;
        device.GetId(&id);
        try
        {
            return id.ToString();
        }
        finally
        {
            PInvoke.CoTaskMemFree(id.Value);
        }
    }
}
