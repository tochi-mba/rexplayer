// Spec: Apple Developer Documentation: Image I/O (CGImageSource.h: CGImageSourceCreateWithData, CGImageSourceGetType, CGImageSourceCreateThumbnailAtIndex and the kCGImageSource thumbnail option keys); Core Graphics (CGBitmapContext.h, CGColorSpace.h, CGImage.h, CGGeometry.h); Core Foundation (CFData.h, CFNumber.h, CFDictionary.h).
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Rex.Media.Interop.Mac.Imaging;

/// <summary>A picture macOS decoded: BGRA rows, four bytes a pixel, no padding.</summary>
public sealed record IoBitmap(int Width, int Height, byte[] Pixels);

/// <summary>
/// Pictures decoded by macOS's Image I/O framework: JPEG, PNG, BMP, TIFF, WebP, HEIC and AVIF,
/// recognised by their bytes. The first picture of the file is turned the way its orientation
/// metadata says, made to fit a size, and given as BGRA, as Apple's headers declare the calls
/// (CGImageSource.h, CGBitmapContext.h, CGColorSpace.h, CGImage.h, CFData.h, CFNumber.h,
/// CFDictionary.h). The calls return the object they make, or null for failure.
/// </summary>
[SupportedOSPlatform("macos")]
public static unsafe partial class ImageIo
{
    /// <summary>What is said when the bytes are no picture macOS recognises.</summary>
    public const string NotAPictureMessage = "macOS does not recognise this kind of picture.";

    private const string CoreFoundationLibrary = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    private const string CoreGraphicsLibrary = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
    private const string ImageIoLibrary = "/System/Library/Frameworks/ImageIO.framework/ImageIO";

    /// <summary>kCFNumberSInt32Type.</summary>
    private const int NumberSInt32 = 3;

    /// <summary>
    /// kCGImageAlphaPremultipliedFirst (2) | kCGBitmapByteOrder32Little (2 &lt;&lt; 12): 32-bit
    /// little-endian pixels with alpha first, so the bytes in memory run blue, green, red, alpha.
    /// Bitmap contexts take no straight-alpha layout, so a translucent pixel comes premultiplied.
    /// </summary>
    private const uint BitmapInfoBgra = 0x2002;

    [StructLayout(LayoutKind.Sequential)]
    private struct CGRect
    {
        public double X;
        public double Y;
        public double Width;
        public double Height;
    }

    /// <summary>
    /// Decodes <paramref name="data"/>, turned upright by the orientation its metadata says,
    /// shrunk to fit within <paramref name="maxSide"/> pixels a side if it is bigger.
    /// </summary>
    public static IoBitmap Decode(ReadOnlySpan<byte> data, int maxSide)
    {
        // The CFData borrows the span, so everything that could read it stays inside this block.
        fixed (byte* bytes = data)
        {
            var held = CFDataCreateWithBytesNoCopy(0, bytes, data.Length, AllocatorNull);
            if (held == 0)
            {
                throw new IOException("macOS could not hold the picture.");
            }

            try
            {
                return Decode(held, maxSide);
            }
            finally
            {
                CFRelease(held);
            }
        }
    }

    private static IoBitmap Decode(nint data, int maxSide)
    {
        var source = CGImageSourceCreateWithData(data, 0);
        if (source == 0)
        {
            throw new IOException(NotAPictureMessage);
        }

        try
        {
            if (CGImageSourceGetType(source) == 0)
            {
                throw new IOException(NotAPictureMessage);
            }

            var options = ThumbnailOptions(maxSide);
            nint picture;
            try
            {
                picture = CGImageSourceCreateThumbnailAtIndex(source, 0, options);
            }
            finally
            {
                CFRelease(options);
            }

            if (picture == 0)
            {
                throw new IOException("macOS could not decode the picture.");
            }

            try
            {
                return Copy(picture);
            }
            finally
            {
                CFRelease(picture);
            }
        }
        finally
        {
            CFRelease(source);
        }
    }

    /// <summary>Draws the picture into BGRA rows of its own size, in sRGB, no padding.</summary>
    private static IoBitmap Copy(nint picture)
    {
        var width = (int)CGImageGetWidth(picture);
        var height = (int)CGImageGetHeight(picture);
        if (width <= 0 || height <= 0)
        {
            throw new IOException("macOS decoded a picture with no size.");
        }

        var pixels = new byte[checked(width * height * 4)];
        fixed (byte* rows = pixels)
        {
            var space = CGColorSpaceCreateWithName(SrgbName);
            if (space == 0)
            {
                throw new IOException("macOS gave no sRGB colour space.");
            }

            nint surface;
            try
            {
                surface = CGBitmapContextCreate(rows, (nuint)width, (nuint)height, 8, (nuint)(width * 4), space, BitmapInfoBgra);
            }
            finally
            {
                CFRelease(space);
            }

            if (surface == 0)
            {
                throw new IOException("macOS gave no surface to draw the picture on.");
            }

            try
            {
                CGContextDrawImage(surface, new CGRect { Width = width, Height = height }, picture);
            }
            finally
            {
                CFRelease(surface);
            }
        }

        return new IoBitmap(width, height, pixels);
    }

    /// <summary>
    /// The options that make a thumbnail the shown picture: always from the full image, never an
    /// embedded preview; turned upright by the orientation metadata; at most <paramref name="maxSide"/>
    /// pixels a side, which never grows a smaller picture.
    /// </summary>
    private static nint ThumbnailOptions(int maxSide)
    {
        var size = CFNumberCreate(0, NumberSInt32, &maxSide);
        try
        {
            var keys = stackalloc nint[] { FromImageAlwaysKey, WithTransformKey, MaxPixelSizeKey };
            var values = stackalloc nint[] { True, True, size };
            return CFDictionaryCreate(0, keys, values, 3, KeyCallBacks, ValueCallBacks);
        }
        finally
        {
            CFRelease(size);
        }
    }

    [LibraryImport(CoreFoundationLibrary)]
    private static partial void CFRelease(nint value);

    [LibraryImport(CoreFoundationLibrary)]
    private static partial nint CFDataCreateWithBytesNoCopy(nint allocator, byte* bytes, nint length, nint bytesDeallocator);

    [LibraryImport(CoreFoundationLibrary)]
    private static partial nint CFNumberCreate(nint allocator, int type, void* value);

    [LibraryImport(CoreFoundationLibrary)]
    private static partial nint CFDictionaryCreate(nint allocator, nint* keys, nint* values, nint count, nint keyCallBacks, nint valueCallBacks);

    [LibraryImport(ImageIoLibrary)]
    private static partial nint CGImageSourceCreateWithData(nint data, nint options);

    [LibraryImport(ImageIoLibrary)]
    private static partial nint CGImageSourceGetType(nint source);

    [LibraryImport(ImageIoLibrary)]
    private static partial nint CGImageSourceCreateThumbnailAtIndex(nint source, nuint index, nint options);

    [LibraryImport(CoreGraphicsLibrary)]
    private static partial nuint CGImageGetWidth(nint image);

    [LibraryImport(CoreGraphicsLibrary)]
    private static partial nuint CGImageGetHeight(nint image);

    [LibraryImport(CoreGraphicsLibrary)]
    private static partial nint CGColorSpaceCreateWithName(nint name);

    [LibraryImport(CoreGraphicsLibrary)]
    private static partial nint CGBitmapContextCreate(void* data, nuint width, nuint height, nuint bitsPerComponent, nuint bytesPerRow, nint space, uint bitmapInfo);

    [LibraryImport(CoreGraphicsLibrary)]
    private static partial void CGContextDrawImage(nint context, CGRect rect, nint image);

    /// <summary>The value of a global a framework exports by name: a CFStringRef key or a CFBooleanRef.</summary>
    private static nint Global(string library, string name) => *(nint*)NativeLibrary.GetExport(NativeLibrary.Load(library), name);

    /// <summary>The address of a global struct a framework exports, such as the dictionary call-back tables.</summary>
    private static nint Address(string library, string name) => NativeLibrary.GetExport(NativeLibrary.Load(library), name);

    /// <summary>kCFAllocatorNull, the allocator that frees nothing: the CFData borrows the caller's bytes.</summary>
    private static nint AllocatorNull => Global(CoreFoundationLibrary, "kCFAllocatorNull");

    private static nint True => Global(CoreFoundationLibrary, "kCFBooleanTrue");

    private static nint KeyCallBacks => Address(CoreFoundationLibrary, "kCFTypeDictionaryKeyCallBacks");

    private static nint ValueCallBacks => Address(CoreFoundationLibrary, "kCFTypeDictionaryValueCallBacks");

    private static nint SrgbName => Global(CoreGraphicsLibrary, "kCGColorSpaceSRGB");

    private static nint FromImageAlwaysKey => Global(ImageIoLibrary, "kCGImageSourceCreateThumbnailFromImageAlways");

    private static nint WithTransformKey => Global(ImageIoLibrary, "kCGImageSourceCreateThumbnailWithTransform");

    private static nint MaxPixelSizeKey => Global(ImageIoLibrary, "kCGImageSourceThumbnailMaxPixelSize");
}
