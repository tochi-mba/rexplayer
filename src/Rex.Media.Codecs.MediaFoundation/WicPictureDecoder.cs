using System.Runtime.InteropServices;
using Rex.Media.Codecs.Pictures;
using Rex.Media.Interop.Imaging;
using Rex.Media.Primitives;

namespace Rex.Media.Codecs.MediaFoundation;

/// <summary>Pictures decoded by Windows' imaging components (FMT-C19).</summary>
public sealed class WicDecoderFactory : IDecoderFactory
{
    public string Name => "Windows Imaging";

    public DecoderSource Source => DecoderSource.OsSoftware;

    public int Rank => 50;

    public bool CanDecode(TrackInfo track)
    {
        ArgumentNullException.ThrowIfNull(track);
        return track.Codec == CodecId.Picture;
    }

    public IAudioDecoder CreateAudio(TrackInfo track) => throw new NotSupportedException("Pictures have no sound.");

    public IVideoDecoder CreateVideo(TrackInfo track) => new WicPictureDecoder();
}

/// <summary>
/// One still picture a packet, decoded by Windows as a BGRA frame: turned upright by its Exif
/// orientation, and no bigger than <see cref="MaxSide"/> pixels a side.
/// </summary>
public sealed class WicPictureDecoder : IVideoDecoder
{
    /// <summary>The longest side a picture is shown at; bigger ones are shrunk to it.</summary>
    public const int MaxSide = 8192;

    public string Name => "Windows Imaging";

    public DecoderSource Source => DecoderSource.OsSoftware;

    public void Decode(Packet packet, ICollection<VideoFrame> output)
    {
        ArgumentNullException.ThrowIfNull(packet);
        ArgumentNullException.ThrowIfNull(output);
        var data = packet.Data.Span;
        WicBitmap bitmap;
        try
        {
            bitmap = WicPicture.Decode(data, ExifOrientation.Of(data), MaxSide);
        }
        catch (COMException ex) when (ex.HResult == WicPicture.NoDecoder)
        {
            throw new MediaFormatException("Windows has no decoder for this kind of picture. HEIC and AVIF pictures need the HEIF Image Extensions or AV1 Video Extension from the Microsoft Store.");
        }
        catch (COMException ex)
        {
            throw new MediaFormatException($"Windows could not decode the picture (0x{ex.HResult:X8}).");
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
