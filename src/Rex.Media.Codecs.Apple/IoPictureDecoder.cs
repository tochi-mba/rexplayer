// Spec: Apple Developer Documentation: Image I/O (CGImageSource) and Core Graphics (CGBitmapContext), reached through Rex.Media.Interop.Mac.Imaging.ImageIo.
using System.Runtime.Versioning;
using Rex.Media.Interop.Mac.Imaging;
using Rex.Media.Primitives;

namespace Rex.Media.Codecs.Apple;

/// <summary>
/// One still picture a packet, decoded by macOS as a BGRA frame: turned upright by its Exif
/// orientation, and no bigger than <see cref="MaxSide"/> pixels a side. The very contract Windows'
/// picture decoder keeps (FMT-C19), so artwork, thumbnails, seek previews and the pictures feature
/// read the two platforms' frames alike.
/// </summary>
[SupportedOSPlatform("macos")]
public sealed class IoPictureDecoder : IVideoDecoder
{
    /// <summary>
    /// The longest side a picture is shown at; enough for a 4K display without allocating the
    /// quarter-gigabyte frame an 8192-pixel photograph required before it could be presented.
    /// </summary>
    public const int MaxSide = 4096;

    public string Name => "macOS ImageIO";

    public DecoderSource Source => DecoderSource.OsSoftware;

    public void Decode(Packet packet, ICollection<VideoFrame> output)
    {
        ArgumentNullException.ThrowIfNull(packet);
        ArgumentNullException.ThrowIfNull(output);
        IoBitmap bitmap;
        try
        {
            bitmap = ImageIo.Decode(packet.Data.Span, MaxSide);
        }
        catch (IOException ex) when (ex is not MediaFormatException)
        {
            throw new MediaFormatException(ex.Message);
        }

        var frame = VideoFrame.Rent(PixelFormat.Bgra32, bitmap.Width, bitmap.Height);
        frame.Pts = packet.Pts;
        frame.Duration = packet.Duration;
        var plane = frame.Plane(0);
        var stride = frame.Stride(0);
        var row = bitmap.Width * 4;
        for (var y = 0; y < bitmap.Height; y++)
        {
            bitmap.Pixels.AsSpan(y * row, row).CopyTo(plane[(y * stride)..]);
        }

        output.Add(frame);
    }

    public bool Drain(ICollection<VideoFrame> output) => true;

    public void Flush()
    {
    }

    public void Dispose()
    {
    }
}
