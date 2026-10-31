using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Windows.Win32.Foundation;
using Windows.Win32.Media.Audio;

namespace Rex.Media.Interop.Audio;

/// <summary>
/// Tells its owner when Windows' default audio devices change: the endpoint enumerator calls back on
/// a thread of its own, and this passes on the flow (true for playback), the role (0 console, 1
/// multimedia, 2 communications) and the new default's id. Plumbing only (ADR-008): whether the
/// sound moves is decided by <c>DeviceFollowPolicy</c>.
/// </summary>
[SupportedOSPlatform("windows6.0.6000")]
public sealed class AudioEndpointWatch : IDisposable
{
    private readonly IMMDeviceEnumerator _enumerator;
    private readonly Client _client;
    private bool _disposed;

    /// <summary>Starts watching; <paramref name="defaultChanged"/> is called on the enumerator's own thread and must not block.</summary>
    public AudioEndpointWatch(Action<bool, int, string?> defaultChanged)
    {
        ArgumentNullException.ThrowIfNull(defaultChanged);
        _enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
        _client = new Client(defaultChanged);
        _enumerator.RegisterEndpointNotificationCallback(_client);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            _enumerator.UnregisterEndpointNotificationCallback(_client);
        }
        catch (COMException)
        {
            // The audio service may already be gone; there is nothing left to unregister from.
        }

        Marshal.ReleaseComObject(_enumerator);
    }

    /// <summary>The callback Windows calls; it only forwards default changes.</summary>
    private sealed class Client(Action<bool, int, string?> defaultChanged) : IMMNotificationClient
    {
        public void OnDeviceStateChanged(PCWSTR pwstrDeviceId, DEVICE_STATE dwNewState)
        {
        }

        public void OnDeviceAdded(PCWSTR pwstrDeviceId)
        {
        }

        public void OnDeviceRemoved(PCWSTR pwstrDeviceId)
        {
        }

        public void OnDefaultDeviceChanged(EDataFlow flow, ERole role, PCWSTR pwstrDefaultDeviceId) =>
            defaultChanged(flow == EDataFlow.eRender, (int)role, pwstrDefaultDeviceId.ToString());

        public void OnPropertyValueChanged(PCWSTR pwstrDeviceId, PROPERTYKEY key)
        {
        }
    }
}
