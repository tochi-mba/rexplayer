using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Rex.Media.Interop.Mac.Media;

/// <summary>
/// The parts of CoreFoundation, CoreMedia, CoreVideo and VideoToolbox rexplayer uses to decode
/// video on a Mac, as Apple's headers declare them (CFBase.h, CFDictionary.h, CFNumber.h,
/// CMTime.h, CMFormatDescription.h, CMBlockBuffer.h, CMSampleBuffer.h, CVPixelBuffer.h,
/// VTDecompressionSession.h, VTDecompressionProperties.h). Functions return an OSStatus, zero for
/// success, unless they return the object they make.
/// </summary>
[SupportedOSPlatform("macos")]
internal static unsafe partial class AppleMedia
{
    private const string CoreFoundationLibrary = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    private const string CoreMediaLibrary = "/System/Library/Frameworks/CoreMedia.framework/CoreMedia";
    private const string CoreVideoLibrary = "/System/Library/Frameworks/CoreVideo.framework/CoreVideo";
    private const string VideoToolboxLibrary = "/System/Library/Frameworks/VideoToolbox.framework/VideoToolbox";

    public const int NumberSInt32 = 3;
    public const uint TimeFlagsValid = 1;
    public const uint BlockBufferAssureMemoryNow = 1;
    public const uint DecodeEnableTemporalProcessing = 1 << 3;
    public const uint LockReadOnly = 1;

    /// <summary>'420v': 8-bit 4:2:0, luma then interleaved chroma, video range: NV12.</summary>
    public const uint PixelFormatNv12 = 0x34323076;

    /// <summary>'x420': 10-bit 4:2:0 in the high bits of 16-bit samples, video range: P010.</summary>
    public const uint PixelFormatP010 = 0x78343230;

    /// <summary>'420f': NV12's layout for full-range samples.</summary>
    public const uint PixelFormatNv12Full = 0x34323066;

    /// <summary>'xf20': P010's layout for full-range samples.</summary>
    public const uint PixelFormatP010Full = 0x78663230;

    [StructLayout(LayoutKind.Sequential)]
    public struct CMTime
    {
        public long Value;
        public int Timescale;
        public uint Flags;
        public long Epoch;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct CMSampleTimingInfo
    {
        public CMTime Duration;
        public CMTime PresentationTimeStamp;
        public CMTime DecodeTimeStamp;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct OutputCallbackRecord
    {
        public delegate* unmanaged<nint, nint, int, uint, nint, CMTime, CMTime, void> Callback;
        public nint Reference;
    }

    [LibraryImport(CoreFoundationLibrary)]
    public static partial void CFRelease(nint value);

    [LibraryImport(CoreFoundationLibrary)]
    public static partial nint CFNumberCreate(nint allocator, int type, void* value);

    [LibraryImport(CoreFoundationLibrary)]
    public static partial nint CFDictionaryCreate(nint allocator, nint* keys, nint* values, nint count, nint keyCallBacks, nint valueCallBacks);

    [LibraryImport(CoreMediaLibrary)]
    public static partial int CMVideoFormatDescriptionCreateFromH264ParameterSets(nint allocator, nuint count, byte** pointers, nuint* sizes, int nalUnitHeaderLength, out nint description);

    [LibraryImport(CoreMediaLibrary)]
    public static partial int CMVideoFormatDescriptionCreateFromHEVCParameterSets(nint allocator, nuint count, byte** pointers, nuint* sizes, int nalUnitHeaderLength, nint extensions, out nint description);

    [LibraryImport(CoreMediaLibrary)]
    public static partial int CMBlockBufferCreateWithMemoryBlock(nint allocator, void* memoryBlock, nuint blockLength, nint blockAllocator, void* customBlockSource, nuint offsetToData, nuint dataLength, uint flags, out nint blockBuffer);

    [LibraryImport(CoreMediaLibrary)]
    public static partial int CMBlockBufferReplaceDataBytes(void* source, nint destination, nuint offsetIntoDestination, nuint dataLength);

    [LibraryImport(CoreMediaLibrary)]
    public static partial int CMSampleBufferCreateReady(nint allocator, nint dataBuffer, nint formatDescription, nint sampleCount, nint timingEntryCount, CMSampleTimingInfo* timing, nint sizeEntryCount, nuint* sizes, out nint sampleBuffer);

    [LibraryImport(CoreVideoLibrary)]
    public static partial int CVPixelBufferLockBaseAddress(nint buffer, ulong flags);

    [LibraryImport(CoreVideoLibrary)]
    public static partial int CVPixelBufferUnlockBaseAddress(nint buffer, ulong flags);

    [LibraryImport(CoreVideoLibrary)]
    public static partial uint CVPixelBufferGetPixelFormatType(nint buffer);

    [LibraryImport(CoreVideoLibrary)]
    public static partial nuint CVPixelBufferGetPlaneCount(nint buffer);

    [LibraryImport(CoreVideoLibrary)]
    public static partial nuint CVPixelBufferGetWidthOfPlane(nint buffer, nuint plane);

    [LibraryImport(CoreVideoLibrary)]
    public static partial nuint CVPixelBufferGetHeightOfPlane(nint buffer, nuint plane);

    [LibraryImport(CoreVideoLibrary)]
    public static partial nuint CVPixelBufferGetBytesPerRowOfPlane(nint buffer, nuint plane);

    [LibraryImport(CoreVideoLibrary)]
    public static partial void* CVPixelBufferGetBaseAddressOfPlane(nint buffer, nuint plane);

    [LibraryImport(VideoToolboxLibrary)]
    public static partial int VTDecompressionSessionCreate(nint allocator, nint formatDescription, nint decoderSpecification, nint imageBufferAttributes, OutputCallbackRecord* callback, out nint session);

    [LibraryImport(VideoToolboxLibrary)]
    public static partial int VTDecompressionSessionDecodeFrame(nint session, nint sampleBuffer, uint flags, nint frameReference, out uint infoFlags);

    [LibraryImport(VideoToolboxLibrary)]
    public static partial int VTDecompressionSessionFinishDelayedFrames(nint session);

    [LibraryImport(VideoToolboxLibrary)]
    public static partial int VTDecompressionSessionWaitForAsynchronousFrames(nint session);

    [LibraryImport(VideoToolboxLibrary)]
    public static partial void VTDecompressionSessionInvalidate(nint session);

    [LibraryImport(VideoToolboxLibrary)]
    public static partial int VTSessionCopyProperty(nint session, nint key, nint allocator, out nint value);

    /// <summary>The value of a global a framework exports by name: a CFStringRef key or a CFBooleanRef.</summary>
    public static nint Global(string library, string name) => *(nint*)NativeLibrary.GetExport(NativeLibrary.Load(library), name);

    /// <summary>The address of a global struct a framework exports, such as the dictionary call-back tables.</summary>
    public static nint Address(string library, string name) => NativeLibrary.GetExport(NativeLibrary.Load(library), name);

    public static nint PixelFormatKey => Global(CoreVideoLibrary, "kCVPixelBufferPixelFormatTypeKey");

    public static nint UsingHardwareKey => Global(VideoToolboxLibrary, "kVTDecompressionPropertyKey_UsingHardwareAcceleratedVideoDecoder");

    public static nint AllowHardwareDecodeKey => Global(VideoToolboxLibrary, "kVTVideoDecoderSpecification_EnableHardwareAcceleratedVideoDecoder");

    public static nint True => Global(CoreFoundationLibrary, "kCFBooleanTrue");

    public static nint False => Global(CoreFoundationLibrary, "kCFBooleanFalse");

    public static nint KeyCallBacks => Address(CoreFoundationLibrary, "kCFTypeDictionaryKeyCallBacks");

    public static nint ValueCallBacks => Address(CoreFoundationLibrary, "kCFTypeDictionaryValueCallBacks");

    public static void Check(int status, string what)
    {
        if (status != 0)
        {
            throw new IOException($"{what} failed with OSStatus {status}.");
        }
    }
}
