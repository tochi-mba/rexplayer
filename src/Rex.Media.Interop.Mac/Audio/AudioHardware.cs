// Spec: CoreAudio/AudioHardware.h and AudioHardwareBase.h (macOS SDK): the HAL's property interface.
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Rex.Media.Interop.Mac.Audio;

/// <summary>
/// The parts of macOS's CoreAudio hardware interface rexplayer uses, as Apple's headers declare
/// them (CoreAudio/AudioHardware.h, AudioHardwareBase.h): reading a property of an audio object
/// and listening for it to change. Every function returns an OSStatus, zero for success.
/// </summary>
[SupportedOSPlatform("macos")]
internal static partial class AudioHardware
{
    private const string Library = "/System/Library/Frameworks/CoreAudio.framework/CoreAudio";

    /// <summary>kAudioObjectSystemObject: the one object that owns the machine-wide properties.</summary>
    public const uint SystemObject = 1;

    public const uint PropertyDefaultOutputDevice = 0x644F7574; // kAudioHardwarePropertyDefaultOutputDevice 'dOut'
    public const uint PropertyStreamConfiguration = 0x736C6179; // kAudioDevicePropertyStreamConfiguration 'slay'
    public const uint ScopeGlobal = 0x676C6F62; // kAudioObjectPropertyScopeGlobal 'glob'
    public const uint ScopeOutput = 0x6F757470; // kAudioObjectPropertyScopeOutput 'outp'
    public const uint ElementMain = 0; // kAudioObjectPropertyElementMain

    /// <summary>Which property of an audio object a call is about, and on which side of it.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct AudioObjectPropertyAddress
    {
        public uint Selector;
        public uint Scope;
        public uint Element;
    }

    [LibraryImport(Library)]
    public static unsafe partial int AudioObjectGetPropertyDataSize(uint objectId, in AudioObjectPropertyAddress address, uint qualifierSize, void* qualifier, out uint size);

    [LibraryImport(Library)]
    public static unsafe partial int AudioObjectGetPropertyData(uint objectId, in AudioObjectPropertyAddress address, uint qualifierSize, void* qualifier, ref uint size, void* data);

    [LibraryImport(Library)]
    public static unsafe partial int AudioObjectAddPropertyListener(uint objectId, in AudioObjectPropertyAddress address, delegate* unmanaged<uint, uint, AudioObjectPropertyAddress*, nint, int> listener, nint reference);

    [LibraryImport(Library)]
    public static unsafe partial int AudioObjectRemovePropertyListener(uint objectId, in AudioObjectPropertyAddress address, delegate* unmanaged<uint, uint, AudioObjectPropertyAddress*, nint, int> listener, nint reference);
}
