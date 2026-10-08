using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Graphics.Imaging;

namespace Rex.Media.Interop.Imaging;

/// <summary>A picture Windows decoded: BGRA rows, four bytes a pixel, no padding.</summary>
public sealed record WicBitmap(int Width, int Height, byte[] Pixels);

/// <summary>
/// Pictures decoded by Windows' imaging components (WIC): JPEG, PNG, BMP, TIFF, WebP, and HEIF or
/// AVIF where their extensions are installed, recognised by their bytes. The first picture of the
/// file is turned the way its orientation says, made to fit a size, and given as BGRA.
/// </summary>
public static unsafe class WicPicture
{
    /// <summary>Windows has no decoder for the picture's format (a HEIF extension not installed, say).</summary>
    public const int NoDecoder = unchecked((int)0x88982F50);

    /// <summary>The decoder is listed but cannot start: a Store extension (WebP's, say) that is not installed.</summary>
    public const int DecoderUnavailable = unchecked((int)0x88982F8B);

    /// <summary>
    /// Decodes <paramref name="data"/>, turned by the Exif <paramref name="orientation"/> (1 to 8),
    /// shrunk to fit within <paramref name="maxSide"/> pixels a side if it is bigger.
    /// </summary>
    public static WicBitmap Decode(ReadOnlySpan<byte> data, int orientation, int maxSide)
    {
        var factory = (IWICImagingFactory)Activator.CreateInstance(Type.GetTypeFromCLSID(PInvoke.CLSID_WICImagingFactory, throwOnError: true)!)!;
        var held = new List<object> { factory };
        try
        {
            var stream = PInvoke.SHCreateMemStream(data) ?? throw new IOException("Windows could not hold a copy of the picture.");
            held.Add(stream);
            var decoder = factory.CreateDecoderFromStream(stream, null, WICDecodeOptions.WICDecodeMetadataCacheOnDemand);
            held.Add(decoder);
            decoder.GetFrame(0, out var frame);
            held.Add(frame);
            IWICBitmapSource source = frame;

            frame.GetSize(out var width, out var height);
            if (width > maxSide || height > maxSide)
            {
                var scale = Math.Min((double)maxSide / width, (double)maxSide / height);
                factory.CreateBitmapScaler(out var scaler);
                held.Add(scaler);
                scaler.Initialize(source, (uint)Math.Max(1, Math.Round(width * scale)), (uint)Math.Max(1, Math.Round(height * scale)), WICBitmapInterpolationMode.WICBitmapInterpolationModeFant);
                source = scaler;
            }

            if (Transform(orientation) is { } transform)
            {
                factory.CreateBitmapFlipRotator(out var rotator);
                held.Add(rotator);
                rotator.Initialize(source, transform);
                source = rotator;
            }

            factory.CreateFormatConverter(out var converter);
            held.Add(converter);
            converter.Initialize(source, PInvoke.GUID_WICPixelFormat32bppBGRA, WICBitmapDitherType.WICBitmapDitherTypeNone, null, 0, WICBitmapPaletteType.WICBitmapPaletteTypeCustom);
            converter.GetSize(out width, out height);
            var pixels = new byte[checked((int)(width * height * 4))];
            converter.CopyPixels(null, width * 4, pixels);

            return new WicBitmap((int)width, (int)height, pixels);
        }
        finally
        {
            held.Reverse();
            foreach (var item in held)
            {
                Marshal.ReleaseComObject(item);
            }
        }
    }

    /// <summary>The turn and mirror that shows a picture of Exif <paramref name="orientation"/> upright; null for none.</summary>
    public static WICBitmapTransformOptions? Transform(int orientation) => orientation switch
    {
        2 => WICBitmapTransformOptions.WICBitmapTransformFlipHorizontal,
        3 => WICBitmapTransformOptions.WICBitmapTransformRotate180,
        4 => WICBitmapTransformOptions.WICBitmapTransformFlipVertical,
        // Windows mirrors before it turns, so a transpose is a mirror then a quarter turn back.
        5 => WICBitmapTransformOptions.WICBitmapTransformRotate270 | WICBitmapTransformOptions.WICBitmapTransformFlipHorizontal,
        6 => WICBitmapTransformOptions.WICBitmapTransformRotate90,
        7 => WICBitmapTransformOptions.WICBitmapTransformRotate90 | WICBitmapTransformOptions.WICBitmapTransformFlipHorizontal,
        8 => WICBitmapTransformOptions.WICBitmapTransformRotate270,
        _ => null,
    };
}
