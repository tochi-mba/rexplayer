using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Graphics.Imaging;
using Windows.Media.FaceAnalysis;

namespace Rex.Media.Faces;

/// <summary>A face found in a picture, as shares of the picture's width and height.</summary>
public readonly record struct FaceBox(double X, double Y, double Width, double Height);

/// <summary>
/// Finds faces in pictures with Windows' own face detector (Windows.Media.FaceAnalysis), which
/// ships with Windows, runs on this computer and needs nothing downloaded (OSD-11). The detector
/// takes an 8-bit grey picture; the colour picture's luminance is handed to it.
/// </summary>
public sealed class FaceFinder : IDisposable
{
    private readonly FaceDetector _detector;

    private FaceFinder(FaceDetector detector) => _detector = detector;

    /// <summary>Whether this Windows can find faces.</summary>
    public static bool IsSupported => FaceDetector.IsSupported;

    /// <summary>A finder, or null where Windows cannot find faces.</summary>
    public static FaceFinder? Create()
    {
        if (!IsSupported)
        {
            return null;
        }

        return new FaceFinder(FaceDetector.CreateAsync().AsTask().GetAwaiter().GetResult());
    }

    /// <summary>
    /// The faces in a BGRA picture <paramref name="width"/> by <paramref name="height"/>, its rows
    /// <paramref name="stride"/> bytes apart; the detector wants even sizes, so an odd edge is cut.
    /// </summary>
    public IReadOnlyList<FaceBox> Find(ReadOnlySpan<byte> bgra, int width, int height, int stride)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 2);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 2);
        ArgumentOutOfRangeException.ThrowIfLessThan(stride, width * 4);
        var (w, h) = (width & ~1, height & ~1);
        var grey = new byte[w * h];
        for (var y = 0; y < h; y++)
        {
            var row = bgra.Slice(y * stride, w * 4);
            for (var x = 0; x < w; x++)
            {
                var at = x * 4;
                grey[(y * w) + x] = (byte)((row[at + 2] * 54 + row[at + 1] * 183 + row[at] * 19) >> 8);
            }
        }

        using var bitmap = SoftwareBitmap.CreateCopyFromBuffer(grey.AsBuffer(), BitmapPixelFormat.Gray8, w, h);
        var faces = _detector.DetectFacesAsync(bitmap).AsTask().GetAwaiter().GetResult();
        return [.. faces.Select(face => new FaceBox((double)face.FaceBox.X / w, (double)face.FaceBox.Y / h, (double)face.FaceBox.Width / w, (double)face.FaceBox.Height / h))];
    }

    public void Dispose()
    {
        // The detector holds no handle of its own to let go; the type is disposable for callers that scope it.
    }
}
