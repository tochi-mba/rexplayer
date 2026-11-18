// Spec: CoreAudio/AudioHardware.h (macOS SDK) — kAudioHardwarePropertyDefaultOutputDevice and its property listener.
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using static Rex.Media.Interop.Mac.Audio.AudioHardware;
using static Rex.Media.Interop.Mac.Audio.AudioToolbox;

namespace Rex.Media.Interop.Mac.Audio;

/// <summary>
/// Tells its owner when the Mac's default audio output changes: the hardware calls back on a
/// thread of its own, and this passes on the new default's id, 0 when none is left. Plumbing only
/// (ADR-0008): whether the sound moves is decided by <c>DeviceFollowPolicy</c>, and the static
/// probes only report what the hardware says, for the format policy to decide with.
/// </summary>
[SupportedOSPlatform("macos")]
public sealed unsafe class DefaultOutputWatch : IDisposable
{
    private readonly Action<uint> _defaultChanged;
    private GCHandle _self;
    private bool _disposed;

    /// <summary>Starts watching; <paramref name="defaultChanged"/> is called on the hardware's own thread and must not block.</summary>
    public DefaultOutputWatch(Action<uint> defaultChanged)
    {
        ArgumentNullException.ThrowIfNull(defaultChanged);
        _defaultChanged = defaultChanged;
        _self = GCHandle.Alloc(this);
        var address = DefaultOutputAddress();
        var status = AudioObjectAddPropertyListener(SystemObject, in address, &Changed, GCHandle.ToIntPtr(_self));
        if (status != 0)
        {
            _self.Free();
            throw new IOException($"Watching the default output failed with OSStatus {status}.");
        }
    }

    /// <summary>The device the Mac plays to by default, 0 when it has none.</summary>
    public static uint Device()
    {
        var address = DefaultOutputAddress();
        uint device = 0;
        var size = (uint)sizeof(uint);
        return AudioObjectGetPropertyData(SystemObject, in address, 0, null, ref size, &device) == 0 ? device : 0;
    }

    /// <summary>How many channels <paramref name="device"/> plays at once, across all its output streams; 0 when it cannot say.</summary>
    public static int OutputChannels(uint device)
    {
        var address = new AudioObjectPropertyAddress { Selector = PropertyStreamConfiguration, Scope = ScopeOutput, Element = ElementMain };
        if (device == 0 || AudioObjectGetPropertyDataSize(device, in address, 0, null, out var size) != 0 || size < sizeof(uint))
        {
            return 0;
        }

        var buffer = new byte[size];
        fixed (byte* data = buffer)
        {
            if (AudioObjectGetPropertyData(device, in address, 0, null, ref size, data) != 0)
            {
                return 0;
            }

            // A variable-length AudioBufferList: the count, then that many AudioBuffers in place.
            var list = (AudioBufferList*)data;
            var most = (size - (sizeof(AudioBufferList) - sizeof(AudioBuffer))) / sizeof(AudioBuffer);
            var channels = 0u;
            var buffers = &list->First;
            for (var i = 0; i < Math.Min(list->NumberBuffers, most); i++)
            {
                channels += buffers[i].NumberChannels;
            }

            return (int)channels;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        var address = DefaultOutputAddress();
        _ = AudioObjectRemovePropertyListener(SystemObject, in address, &Changed, GCHandle.ToIntPtr(_self));
        _self.Free();
    }

    private static AudioObjectPropertyAddress DefaultOutputAddress() =>
        new() { Selector = PropertyDefaultOutputDevice, Scope = AudioHardware.ScopeGlobal, Element = ElementMain };

    /// <summary>The callback the hardware calls; it only forwards the new default's id.</summary>
    [UnmanagedCallersOnly]
    private static int Changed(uint objectId, uint count, AudioObjectPropertyAddress* addresses, nint reference)
    {
        var watch = (DefaultOutputWatch)GCHandle.FromIntPtr(reference).Target!;
        watch._defaultChanged(Device());
        return 0;
    }
}
