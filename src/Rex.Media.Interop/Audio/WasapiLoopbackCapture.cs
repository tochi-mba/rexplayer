using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Windows.Win32;
using Windows.Win32.Media.Audio;
using Windows.Win32.System.Com;

namespace Rex.Media.Interop.Audio;

/// <summary>
/// Listens to what this computer plays, for the visualisations: everything the default output plays
/// (shared-mode loopback), or only one app's sound with the processes it started (process loopback,
/// Windows 10 version 2004 and later). Packets are handed over as they arrive, on a thread of the
/// capture's own at below-normal priority, so the window and the player are never waited on. Plumbing
/// only (ADR-008): what is listened to and how the sound is read are decided elsewhere.
/// </summary>
[SupportedOSPlatform("windows6.0.6000")]
public sealed unsafe class WasapiLoopbackCapture : IDisposable
{
    /// <summary>A packet: interleaved samples of <see cref="Format"/>, how many frames, and whether Windows marked it silent.</summary>
    public delegate void PacketHandler(ReadOnlySpan<byte> interleaved, int frames, bool silent);

    private const int LoopbackFlag = 0x00020000;
    private const int AutoConvertFlags = unchecked((int)0x80000000) | 0x08000000;
    private const uint SilentFlag = 0x2;
    private static readonly TimeSpan BufferDuration = TimeSpan.FromMilliseconds(200);

    private readonly IAudioClient _client;
    private readonly IAudioCaptureClient _capture;
    private readonly PacketHandler _packet;
    private readonly Thread _thread;
    private volatile bool _stopping;
    private bool _disposed;

    private WasapiLoopbackCapture(IAudioClient client, WasapiMixFormat format, PacketHandler packet)
    {
        (_client, Format, _packet) = (client, format, packet);
        var id = typeof(IAudioCaptureClient).GUID;
        _client.GetService(&id, out var pointer);
        try
        {
            _capture = (IAudioCaptureClient)Marshal.GetObjectForIUnknown((nint)pointer);
        }
        finally
        {
            Marshal.Release((nint)pointer);
        }

        _thread = new Thread(Run) { IsBackground = true, Name = "Sound capture", Priority = ThreadPriority.BelowNormal };
    }

    /// <summary>The captured sound's layout.</summary>
    public WasapiMixFormat Format { get; }

    /// <summary>Why the last packet could not be read, for the log; capture stops then.</summary>
    public Exception? Failure { get; private set; }

    /// <summary>Whether listening to one app is possible on this Windows.</summary>
    public static bool CanListenToOneApp => OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041);

    /// <summary>Listens to everything the media default output plays; null when there is no output.</summary>
    public static WasapiLoopbackCapture? OpenEverything(PacketHandler packet)
    {
        ArgumentNullException.ThrowIfNull(packet);
        var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
        IMMDevice? device = null;
        try
        {
            enumerator.GetDefaultAudioEndpoint(EDataFlow.eRender, ERole.eMultimedia, out device);
            var id = typeof(IAudioClient).GUID;
            device.Activate(&id, CLSCTX.CLSCTX_ALL, null, out var activated);
            var client = (IAudioClient)activated;
            WAVEFORMATEX* mix;
            client.GetMixFormat(&mix);
            try
            {
                var format = Read(mix);
                client.Initialize(AUDCLNT_SHAREMODE.AUDCLNT_SHAREMODE_SHARED, (uint)LoopbackFlag, BufferDuration.Ticks, 0, mix, null);
                return new WasapiLoopbackCapture(client, format, packet);
            }
            finally
            {
                PInvoke.CoTaskMemFree(mix);
            }
        }
        catch (COMException)
        {
            return null;
        }
        finally
        {
            if (device is not null)
            {
                Marshal.ReleaseComObject(device);
            }

            Marshal.ReleaseComObject(enumerator);
        }
    }

    /// <summary>
    /// Listens to one app: process <paramref name="processId"/> and the processes it started, as
    /// 48 kHz stereo 16-bit. Null when this Windows cannot, or the app could not be listened to.
    /// </summary>
    [SupportedOSPlatform("windows10.0.19041")]
    public static WasapiLoopbackCapture? OpenApp(uint processId, PacketHandler packet)
    {
        ArgumentNullException.ThrowIfNull(packet);
        var parameters = new ActivationParameters { Type = 1, ProcessId = processId, Mode = 0 };
        var variant = new BlobVariant { Type = 65, Size = (uint)sizeof(ActivationParameters), Data = (nint)(&parameters) };
        var handler = new Completion();
        var id = typeof(IAudioClient).GUID;
        if (ActivateAudioInterfaceAsync(@"VAD\Process_Loopback", in id, &variant, handler, out var operation) < 0)
        {
            return null;
        }

        try
        {
            if (!handler.Done.Wait(TimeSpan.FromSeconds(5)) || operation.GetActivateResult(out var result, out var activated) < 0 || result < 0)
            {
                return null;
            }

            var client = (IAudioClient)activated;
            var format = new WasapiMixFormat(48_000, 2, 16, 16, 3, false, 4);
            var wave = new WAVEFORMATEX { wFormatTag = 1, nChannels = 2, nSamplesPerSec = 48_000, wBitsPerSample = 16, nBlockAlign = 4, nAvgBytesPerSec = 192_000 };
            try
            {
                client.Initialize(AUDCLNT_SHAREMODE.AUDCLNT_SHAREMODE_SHARED, unchecked((uint)(LoopbackFlag | AutoConvertFlags)), BufferDuration.Ticks, 0, &wave, null);
                return new WasapiLoopbackCapture(client, format, packet);
            }
            catch (COMException)
            {
                Marshal.ReleaseComObject(client);
                return null;
            }
        }
        finally
        {
            Marshal.ReleaseComObject(operation);
            handler.Done.Dispose();
        }
    }

    public void Start()
    {
        _client.Start();
        _thread.Start();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _stopping = true;
        if (_thread.IsAlive)
        {
            _thread.Join();
        }

        try
        {
            _client.Stop();
        }
        catch (COMException)
        {
            // The device may be gone already; letting go is all that is left.
        }

        Marshal.ReleaseComObject(_capture);
        Marshal.ReleaseComObject(_client);
    }

    /// <summary>Reads what has arrived every ten milliseconds: event callbacks are unreliable with loopback on older Windows.</summary>
    private void Run()
    {
        try
        {
            while (!_stopping)
            {
                Thread.Sleep(10);
                _capture.GetNextPacketSize(out var size);
                while (size > 0 && !_stopping)
                {
                    byte* data;
                    uint frames;
                    uint flags;
                    _capture.GetBuffer(&data, out frames, out flags, null, null);
                    try
                    {
                        _packet(new ReadOnlySpan<byte>(data, (int)frames * Format.BlockAlign), (int)frames, (flags & SilentFlag) != 0);
                    }
                    finally
                    {
                        _capture.ReleaseBuffer(frames);
                    }

                    _capture.GetNextPacketSize(out size);
                }
            }
        }
        catch (COMException ex)
        {
            // The device went away or the app closed; the owner sees Failure and decides what next.
            Failure = ex;
        }
    }

    private static WasapiMixFormat Read(WAVEFORMATEX* format)
    {
        var floatSubtype = new Guid("00000003-0000-0010-8000-00aa00389b71");
        var extensible = format->wFormatTag == 0xFFFE && format->cbSize >= 22;
        var isFloat = format->wFormatTag == 3 || (extensible && ((WAVEFORMATEXTENSIBLE*)format)->SubFormat == floatSubtype);
        return new WasapiMixFormat((int)format->nSamplesPerSec, format->nChannels, format->wBitsPerSample,
            extensible ? ((WAVEFORMATEXTENSIBLE*)format)->Samples.wValidBitsPerSample : format->wBitsPerSample,
            extensible ? ((WAVEFORMATEXTENSIBLE*)format)->dwChannelMask : 0, isFloat, format->nBlockAlign);
    }

    /// <summary>AUDIOCLIENT_ACTIVATION_PARAMS for process loopback: type 1, the process, mode 0 (that process and those it started).</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct ActivationParameters
    {
        public int Type;
        public uint ProcessId;
        public int Mode;
    }

    /// <summary>A PROPVARIANT holding a blob (VT_BLOB, 65), the only kind the activation takes.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct BlobVariant
    {
        public ushort Type;
        public ushort Reserved1;
        public ushort Reserved2;
        public ushort Reserved3;
        public uint Size;
        public nint Data;
    }

    [ComImport]
    [Guid("72A22D78-CDE4-431D-B8CC-843A71199B6D")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IActivateAudioInterfaceAsyncOperation
    {
        [PreserveSig]
        int GetActivateResult(out int result, [MarshalAs(UnmanagedType.IUnknown)] out object activated);
    }

    [ComImport]
    [Guid("41D949AB-9862-444A-80F6-C261334DA5EB")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IActivateAudioInterfaceCompletionHandler
    {
        [PreserveSig]
        int ActivateCompleted(IActivateAudioInterfaceAsyncOperation operation);
    }

    /// <summary>Windows calls this from its own thread when the activation is done.</summary>
    private sealed class Completion : IActivateAudioInterfaceCompletionHandler
    {
        public ManualResetEventSlim Done { get; } = new(false);

        public int ActivateCompleted(IActivateAudioInterfaceAsyncOperation operation)
        {
            Done.Set();
            return 0;
        }
    }

    [DllImport("Mmdevapi.dll", ExactSpelling = true, PreserveSig = true)]
    private static extern int ActivateAudioInterfaceAsync([MarshalAs(UnmanagedType.LPWStr)] string deviceInterfacePath, in Guid riid,
        BlobVariant* activationParams, IActivateAudioInterfaceCompletionHandler completionHandler, out IActivateAudioInterfaceAsyncOperation activationOperation);
}
