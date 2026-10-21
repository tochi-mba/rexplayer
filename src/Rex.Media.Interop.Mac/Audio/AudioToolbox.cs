using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Rex.Media.Interop.Mac.Audio;

/// <summary>
/// The parts of macOS's AudioToolbox framework rexplayer uses, as Apple's headers declare them
/// (AudioToolbox/AudioComponent.h, AudioUnit/AUComponent.h, AudioUnitProperties.h,
/// CoreAudioTypes.h). Every function returns an OSStatus, zero for success.
/// </summary>
[SupportedOSPlatform("macos")]
internal static partial class AudioToolbox
{
    private const string Library = "/System/Library/Frameworks/AudioToolbox.framework/AudioToolbox";

    public const uint TypeOutput = 0x61756F75; // 'auou'
    public const uint SubTypeDefaultOutput = 0x64656620; // 'def '
    public const uint ManufacturerApple = 0x6170706C; // 'appl'
    public const uint FormatLinearPcm = 0x6C70636D; // 'lpcm'
    public const uint FormatFlagIsFloat = 1;
    public const uint FormatFlagIsPacked = 8;
    public const uint PropertyStreamFormat = 8;
    public const uint PropertySetRenderCallback = 23;
    public const uint ScopeGlobal = 0;
    public const uint ScopeInput = 1;

    [StructLayout(LayoutKind.Sequential)]
    public struct AudioComponentDescription
    {
        public uint ComponentType;
        public uint ComponentSubType;
        public uint ComponentManufacturer;
        public uint ComponentFlags;
        public uint ComponentFlagsMask;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct AudioStreamBasicDescription
    {
        public double SampleRate;
        public uint FormatId;
        public uint FormatFlags;
        public uint BytesPerPacket;
        public uint FramesPerPacket;
        public uint BytesPerFrame;
        public uint ChannelsPerFrame;
        public uint BitsPerChannel;
        public uint Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct RenderCallback
    {
        public delegate* unmanaged<nint, uint*, nint, uint, uint, AudioBufferList*, int> Procedure;
        public nint ProcedureReference;
    }

    /// <summary>One buffer of an <see cref="AudioBufferList"/>; interleaved output uses exactly one.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct AudioBuffer
    {
        public uint NumberChannels;
        public uint DataByteSize;
        public void* Data;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct AudioBufferList
    {
        public uint NumberBuffers;
        public AudioBuffer First;
    }

    [LibraryImport(Library)]
    public static partial nint AudioComponentFindNext(nint component, ref AudioComponentDescription description);

    [LibraryImport(Library)]
    public static partial int AudioComponentInstanceNew(nint component, out nint instance);

    [LibraryImport(Library)]
    public static partial int AudioComponentInstanceDispose(nint instance);

    [LibraryImport(Library)]
    public static unsafe partial int AudioUnitSetProperty(nint unit, uint property, uint scope, uint element, void* data, uint size);

    [LibraryImport(Library)]
    public static partial int AudioUnitInitialize(nint unit);

    [LibraryImport(Library)]
    public static partial int AudioUnitUninitialize(nint unit);

    [LibraryImport(Library)]
    public static partial int AudioOutputUnitStart(nint unit);

    [LibraryImport(Library)]
    public static partial int AudioOutputUnitStop(nint unit);

    /// <summary>Throws the OSStatus as an <see cref="IOException"/> naming what failed.</summary>
    public static void Check(int status, string what)
    {
        if (status != 0)
        {
            throw new IOException($"{what} failed with OSStatus {status}.");
        }
    }
}
