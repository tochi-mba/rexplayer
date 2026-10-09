using Rex.Media.AppCore;
using Rex.Media.Codecs;
using Rex.Media.Codecs.MediaFoundation;
using Rex.Media.Interop.Imaging;
using Rex.Media.IO;
using Rex.Media.Primitives;
using Rex.Media.TestKit;

namespace Rex.Media.Windows.Tests;

/// <summary>Pictures decoded by Windows' imaging components (FMT-C19), against FFmpeg's pixels.</summary>
public sealed class WicPictureDecoderTests
{
    private const int Width = 64;
    private const int Height = 48;

    private static byte[] Fixture(string name) => File.ReadAllBytes(RepoPaths.Combine("tests", "fixtures", "picture", name));

    /// <summary>The picture as the app plays it: through the demuxers, then this decoder.</summary>
    private static (int Width, int Height, byte[] Pixels) Decode(byte[] data)
    {
        var source = new MemoryByteSource(data, "picture");
        using var demuxer = MediaRegistries.Demuxers().Open(source, CancellationToken.None);
        var track = demuxer.Info.Tracks[0];
        var factory = new WicDecoderFactory();
        Assert.True(factory.CanDecode(track));
        using var decoder = factory.CreateVideo(track);
        using var packet = demuxer.ReadPacket(CancellationToken.None)!;
        var frames = new List<VideoFrame>();
        decoder.Decode(packet, frames);
        using var frame = Assert.Single(frames);
        Assert.Equal((PixelFormat.Bgra32, packet.Pts, packet.Duration), (frame.Format, frame.Pts, frame.Duration));
        var pixels = new byte[frame.Width * frame.Height * 4];
        for (var y = 0; y < frame.Height; y++)
        {
            frame.Plane(0).Slice(y * frame.Stride(0), frame.Width * 4).CopyTo(pixels.AsSpan(y * frame.Width * 4));
        }

        return (frame.Width, frame.Height, pixels);
    }

    [Theory]
    [Capability("FMT-C19")]
    [InlineData("still.png")]
    [InlineData("still.bmp")]
    [InlineData("still.tiff")]
    [InlineData("still.webp")]
    public void LosslessPicturesMatchFfmpegExactly(string name)
    {
        int width, height;
        byte[] pixels;
        try
        {
            (width, height, pixels) = Decode(Fixture(name));
        }
        catch (MediaFormatException ex) when (ex.Message == WicPictureDecoder.NoDecoderMessage)
        {
            Assert.Skip($"This Windows cannot decode {name} (Windows Server has no WebP Image Extension).");
            return;
        }


        Assert.Equal((Width, Height), (width, height));
        Assert.True(Fixture("still.reference.bgra").AsSpan().SequenceEqual(pixels), $"{name} differs from FFmpeg's pixels");
    }

    [Fact]
    [Capability("FMT-C19")]
    public void AJpegIsFaithfulToTheOriginal()
    {
        var (_, _, pixels) = Decode(Fixture("still.jpg"));
        var original = Fixture("still.reference.bgra");

        // The JPEG keeps full colour (4:4:4), so decoders differ only in rounding: a correct decode is
        // well over 30 dB from the original, and a wrong one (colours swapped, range or rows off) far below.
        var squares = pixels.Zip(original).Where((_, i) => i % 4 != 3).Average(pair => Math.Pow(pair.First - pair.Second, 2));
        var psnr = 10 * Math.Log10(255 * 255 / squares);
        Assert.True(psnr > 30, $"The JPEG is {psnr:F1} dB from the original.");
    }

    [Theory]
    [Capability("FMT-C19")]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    public void APhotoIsTurnedTheWayItsExifSays(int orientation)
    {
        var jpeg = Fixture("still.jpg");
        var plain = Decode(jpeg);

        // The same picture, told it was taken another way up.
        byte[] tiff = [.. "II*\0"u8, 8, 0, 0, 0, 1, 0, 0x12, 0x01, 3, 0, 1, 0, 0, 0, (byte)orientation, 0, 0, 0];
        byte[] exif = [0xFF, 0xE1, 0, (byte)(2 + 6 + tiff.Length), .. "Exif\0\0"u8, .. tiff];
        var turned = Decode([.. jpeg[..2], .. exif, .. jpeg[2..]]);

        // Where each shown pixel comes from in the stored picture, by the Exif definitions.
        var sideways = orientation >= 5;
        Assert.Equal(sideways ? (Height, Width) : (Width, Height), (turned.Width, turned.Height));
        for (var y = 0; y < turned.Height; y++)
        {
            for (var x = 0; x < turned.Width; x++)
            {
                var (sx, sy) = orientation switch
                {
                    1 => (x, y),
                    2 => (Width - 1 - x, y),
                    3 => (Width - 1 - x, Height - 1 - y),
                    4 => (x, Height - 1 - y),
                    5 => (y, x),
                    6 => (y, Height - 1 - x),
                    7 => (Width - 1 - y, Height - 1 - x),
                    _ => (Width - 1 - y, x),
                };
                var shown = turned.Pixels.AsSpan(((y * turned.Width) + x) * 4, 4);
                var stored = plain.Pixels.AsSpan(((sy * Width) + sx) * 4, 4);
                Assert.True(shown.SequenceEqual(stored), $"orientation {orientation}: the pixel at ({x}, {y}) is not the stored one at ({sx}, {sy})");
            }
        }
    }

    [Fact]
    [Capability("FMT-C19")]
    public void ABigPictureIsShrunkToFit()
    {
        var bitmap = WicPicture.Decode(Fixture("still.png"), 1, 32);

        Assert.Equal((32, 24, 32 * 24 * 4), (bitmap.Width, bitmap.Height, bitmap.Pixels.Length));
        Assert.Null(WicPicture.Transform(1));
        Assert.Equal(4096, WicPictureDecoder.MaxSide);
    }

    [Fact]
    public void ADamagedPictureIsRefusedCleanly()
    {
        var decoder = new WicDecoderFactory().CreateVideo(new TrackInfo { Id = 1, Codec = CodecId.Picture, Video = new VideoTrackInfo { Width = 0, Height = 0 } });
        var frames = new List<VideoFrame>();
        var png = Fixture("still.png");

        Assert.Throws<MediaFormatException>(() => decoder.Decode(Packet.Create(1, MediaBuffer.CopyOf(png.AsSpan(0, 60)), MediaTime.Zero, MediaTime.Zero, MediaTime.FromSeconds(1), true), frames));
        Assert.Throws<MediaFormatException>(() => decoder.Decode(Packet.Create(1, MediaBuffer.CopyOf([1, 2, 3, 4, 5, 6, 7, 8]), MediaTime.Zero, MediaTime.Zero, MediaTime.FromSeconds(1), true), frames));
        Assert.Empty(frames);
        Assert.True(decoder.Drain(frames));
        decoder.Flush();
        decoder.Dispose();
        Assert.Equal(("Windows Imaging", DecoderSource.OsSoftware), (decoder.Name, decoder.Source));
        Assert.Throws<ArgumentNullException>(() => decoder.Decode(null!, frames));
    }

    [Fact]
    public void TheFactoryTakesStillPicturesOnly()
    {
        var factory = new WicDecoderFactory();
        var picture = new TrackInfo { Id = 1, Codec = CodecId.Picture };

        Assert.True(factory.CanDecode(picture));
        Assert.False(factory.CanDecode(picture with { Codec = CodecId.Gif }));
        Assert.Equal(("Windows Imaging", DecoderSource.OsSoftware, 50), (factory.Name, factory.Source, factory.Rank));
        Assert.Throws<NotSupportedException>(() => factory.CreateAudio(picture));
        Assert.Throws<ArgumentNullException>(() => factory.CanDecode(null!));
    }
}
