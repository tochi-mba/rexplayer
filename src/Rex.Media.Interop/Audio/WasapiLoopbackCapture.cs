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
    /// <summary>A packet: interleaved samples, how many frames, whether Windows marked it silent, and their layout.</summary>
    public delegate void PacketHandler(ReadOnlySpan<byte> interleaved, int frames, bool silent, WasapiMixFormat format);

    private const int LoopbackFlag = 0x00020000;
    private const int AutoConvertFlags = unchecked((int)0x80000000) | 0x08000000;
    private const uint SilentFlag = 0x2;
    private static readonly TimeSpan BufferDuration = TimeSpan.FromMilliseconds(200);

    private readonly Func<(IAudioClient Client, WasapiMixFormat Format)?> _open;
    private readonly PacketHandler _packet;
    private readonly Thread _thread;
    private readonly ManualResetEventSlim _ready = new(false);
    private volatile bool _stopping;
    private bool _opened;
    private bool _disposed;

    /// <summary>
    /// Opens and runs the capture on a thread of its own: the audio objects are made, read and let go
    /// there, never handed between threads (they cannot be, from a window's single-threaded apartment).
    /// </summary>
    private WasapiLoopbackCapture(Func<(IAudioClient Client, WasapiMixFormat Format)?> open, PacketHandler packet)
    {
        (_open, _packet) = (open, packet);
        _thread = new Thread(Run) { IsBackground = true, Name = "Sound capture", Priority = ThreadPriority.BelowNormal };
        _thread.SetApartmentState(ApartmentState.MTA);
        _thread.Start();
        _ready.Wait(TimeSpan.FromSeconds(6));
    }

    /// <summary>The captured sound's layout.</summary>
    public WasapiMixFormat Format { get; private set; } = new(48_000, 2, 16, 16, 3, false, 4);

    /// <summary>Why capture stopped or could not start, for the log.</summary>
    public Exception? Failure { get; private set; }

    /// <summary>Whether listening to one app is possible on this Windows.</summary>
    public static bool CanListenToOneApp => OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041);

    private static WasapiLoopbackCapture? Started(WasapiLoopbackCapture capture)
    {
        if (capture._opened)
        {
            return capture;
        }

        capture.Dispose();
        return null;
    }

    /// <summary>Listens to everything the media default output plays; null when there is no output.</summary>
    public static WasapiLoopbackCapture? OpenEverything(PacketHandler packet)
    {
        ArgumentNullException.ThrowIfNull(packet);
        return Started(new WasapiLoopbackCapture(MakeEverything, packet));
    }

    private static (IAudioClient Client, WasapiMixFormat Format)? MakeEverything()
    {
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
                return (client, format);
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
        return Started(new WasapiLoopbackCapture(() => MakeApp(processId), packet));
    }

    [SupportedOSPlatform("windows10.0.19041")]
    private static (IAudioClient Client, WasapiMixFormat Format)? MakeApp(uint processId)
    {
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
                return (client, format);
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

        _ready.Dispose();
    }

    /// <summary>
    /// Opens, then reads what has arrived every ten milliseconds (event callbacks are unreliable with
    /// loopback on older Windows), then lets everything go: all on this thread.
    /// </summary>
    private void Run()
    {
        IAudioClient? client = null;
        IAudioCaptureClient? capture = null;
        try
        {
            if (_open() is not { } opened)
            {
                return;
            }

            (client, Format) = opened;
            var id = typeof(IAudioCaptureClient).GUID;
            client.GetService(&id, out var pointer);
            try
            {
                capture = (IAudioCaptureClient)Marshal.GetObjectForIUnknown((nint)pointer);
            }
            finally
            {
                Marshal.Release((nint)pointer);
            }

            client.Start();
            _opened = true;
            _ready.Set();
            while (!_stopping)
            {
                Thread.Sleep(10);
                capture.GetNextPacketSize(out var size);
                while (size > 0 && !_stopping)
                {
                    byte* data;
                    uint frames;
                    uint flags;
                    capture.GetBuffer(&data, out frames, out flags, null, null);
                    try
                    {
                        _packet(new ReadOnlySpan<byte>(data, (int)frames * Format.BlockAlign), (int)frames, (flags & SilentFlag) != 0, Format);
                    }
                    finally
                    {
                        capture.ReleaseBuffer(frames);
                    }

                    capture.GetNextPacketSize(out size);
                }
            }
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or InvalidOperationException or NotSupportedException or ArgumentException)
        {
            // The device went away, the app closed, or Windows refused: listening stops, the app goes on.
            Failure = ex;
        }
        finally
        {
            try
            {
                client?.Stop();
            }
            catch (COMException)
            {
                // The device may be gone already; letting go is all that is left.
            }

            if (capture is not null)
            {
                Marshal.ReleaseComObject(capture);
            }

            if (client is not null)
            {
                Marshal.ReleaseComObject(client);
            }

            _ready.Set();
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
