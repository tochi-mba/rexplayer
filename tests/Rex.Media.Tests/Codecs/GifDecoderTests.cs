using Rex.Media.Codecs.Software.Gif;
using Rex.Media.Containers.Image;
using Rex.Media.IO;
using Rex.Media.Primitives;
using Rex.Media.TestKit;

namespace Rex.Media.Tests.Codecs;

/// <summary>rexplayer's own GIF decoder (FMT-C19), against FFmpeg's pictures and GIFs built to reach every path.</summary>
public sealed class GifDecoderTests
{
    private static readonly byte[] Four = GifWriter.Grays(4);

    /// <summary>Every picture of a GIF, decoded, as BGRA bytes without row padding.</summary>
    private static List<(MediaTime Pts, byte[] Pixels)> Decode(byte[] gif, TimeSpan? showFor = null)
    {
        using var demuxer = new PictureDemuxerFactory(showFor ?? TimeSpan.FromMilliseconds(1)).Open(new MemoryByteSource(gif, "test.gif"), CancellationToken.None);
        var track = demuxer.Info.Tracks[0];
        using var decoder = new GifDecoderFactory().CreateVideo(track);
        var pictures = new List<(MediaTime, byte[])>();
        while (demuxer.ReadPacket(CancellationToken.None) is { } packet)
        {
            using (packet)
            {
                var frames = new List<VideoFrame>();
                decoder.Decode(packet, frames);
                var frame = Assert.Single(frames);
                using (frame)
                {
                    Assert.Equal(PixelFormat.Bgra32, frame.Format);
                    pictures.Add((frame.Pts, Pixels(frame)));
                }
            }
        }

        return pictures;
    }

    private static byte[] Pixels(VideoFrame frame)
    {
        var pixels = new byte[frame.Width * frame.Height * 4];
        for (var y = 0; y < frame.Height; y++)
        {
            frame.Plane(0).Slice(y * frame.Stride(0), frame.Width * 4).CopyTo(pixels.AsSpan(y * frame.Width * 4));
        }

        return pixels;
    }

    /// <summary>The gray a 4-gray index shows as, opaque, as BGRA.</summary>
    private static byte[] Gray(int index) => [(byte)(index * 85), (byte)(index * 85), (byte)(index * 85), 255];

    private static byte[] Screen(int width, int height, Func<int, int, byte[]> pixel) =>
        [.. Enumerable.Range(0, height).SelectMany(y => Enumerable.Range(0, width).SelectMany(x => pixel(x, y)))];

    [Fact]
    [Capability("FMT-C19")]
    public void AnAnimationMatchesFfmpegPictureForPicture()
    {
        var gif = File.ReadAllBytes(RepoPaths.Combine("tests", "fixtures", "picture", "anim.gif"));
        var reference = File.ReadAllBytes(RepoPaths.Combine("tests", "fixtures", "picture", "anim.reference.bgra"));
        var frameBytes = 64 * 48 * 4;

        var pictures = Decode(gif);

        Assert.Equal(reference.Length / frameBytes, pictures.Count);
        for (var i = 0; i < pictures.Count; i++)
        {
            Assert.Equal(MediaTime.FromMilliseconds(250 * i), pictures[i].Pts);
            Assert.True(reference.AsSpan(i * frameBytes, frameBytes).SequenceEqual(pictures[i].Pixels), $"picture {i} differs from FFmpeg's");
        }
    }

    [Fact]
    [Capability("FMT-C19")]
    public void TransparencyLetsWhatIsBeneathShowAndEachDisposalIsHonoured()
    {
        // A 4x2 screen: everything gray 1; then a 2x1 picture at (1,0), cleared after (disposal 2);
        // then one put back after (3); then one kept (1) whose transparent pixel leaves gray 1 beneath.
        var gif = GifWriter.Write(4, 2, Four,
        [
            new GifPicture(0, 0, 4, 2, [1, 1, 1, 1, 1, 1, 1, 1]),
            new GifPicture(1, 0, 2, 1, [2, 2]) { Disposal = 2 },
            new GifPicture(0, 1, 2, 1, [3, 3]) { Disposal = 3 },
            new GifPicture(2, 1, 2, 1, [0, 3]) { Disposal = 1, Transparent = 3 },
            new GifPicture(3, 0, 1, 1, [3]),
        ]);

        var pictures = Decode(gif).Select(p => p.Pixels).ToList();

        byte[] Expect(params int[] grays) => Screen(4, 2, (x, y) => grays[(y * 4) + x] < 0 ? [0, 0, 0, 0] : Gray(grays[(y * 4) + x]));
        Assert.Equal(Expect(1, 1, 1, 1, 1, 1, 1, 1), pictures[0]);
        Assert.Equal(Expect(1, 2, 2, 1, 1, 1, 1, 1), pictures[1]);
        Assert.Equal(Expect(1, -1, -1, 1, 3, 3, 1, 1), pictures[2]);
        Assert.Equal(Expect(1, -1, -1, 1, 1, 1, 0, 1), pictures[3]);
        Assert.Equal(Expect(1, -1, -1, 3, 1, 1, 0, 1), pictures[4]);
    }

    [Fact]
    [Capability("FMT-C19")]
    public void InterlacedRowsLocalColorsAndPicturesPastTheScreenEdge()
    {
        // Eleven rows, so every pass of the interlacing has rows; each row its own index.
        var rows = Enumerable.Range(0, 11).SelectMany(row => new[] { (byte)(row % 4), (byte)(row % 4) }).ToArray();
        var reds = new byte[] { 0, 0, 0, 255, 0, 0, 0, 255, 0, 0, 0, 255 };
        var gif = GifWriter.Write(2, 11, null,
        [
            new GifPicture(0, 0, 2, 11, rows) { Interlaced = true, Colors = Four },
            new GifPicture(1, 10, 3, 3, [.. Enumerable.Repeat((byte)1, 9)]) { Colors = reds },
        ]);

        var pictures = Decode(gif).Select(p => p.Pixels).ToList();

        Assert.Equal(Screen(2, 11, (_, y) => Gray(y % 4)), pictures[0]);
        Assert.Equal([0, 0, 255, 255], pictures[1].AsSpan((((10 * 2) + 1) * 4), 4).ToArray());
    }

    [Fact]
    [Capability("FMT-C19")]
    public void LongDataFillsTheCodeTableAndStartsItAgain()
    {
        // Noise defeats the dictionary, so codes grow to 12 bits and the table fills and clears.
        var random = new Random(7);
        var indexes = new byte[200 * 100];
        random.NextBytes(indexes);
        for (var i = 0; i < indexes.Length; i++)
        {
            indexes[i] = (byte)(indexes[i] % 4 == 0 && i % 7 == 0 ? indexes[i] : indexes[i] & 0xF);
        }

        var colors = GifWriter.Grays(256);
        var gif = GifWriter.Write(200, 100, colors, [new GifPicture(0, 0, 200, 100, indexes) { CodeSize = 8 }]);

        var picture = Decode(gif).Single().Pixels;

        Assert.Equal(Screen(200, 100, (x, y) => [indexes[(y * 200) + x], indexes[(y * 200) + x], indexes[(y * 200) + x], 255]), picture);
    }

    [Fact]
    [Capability("FMT-C19")]
    public void DamagedDataEndsThePictureWhereItGoesWrong()
    {
        // A code past the table after two good pixels; then data that stops short.
        var bad = GifWriter.Write(4, 1, Four, [new GifPicture(0, 0, 4, 1, [1, 2, 3, 1])]);
        var data = Array.LastIndexOf(bad, (byte)0x2C) + 11;
        bad[data + 1] = 0xFF;
        bad[data + 2] = 0xFF;
        var shortData = GifWriter.Write(4, 1, Four, [new GifPicture(0, 0, 4, 1, [3, 3, 3, 3]) with { }]);
        var image = Array.LastIndexOf(shortData, (byte)0x2C) + 11;
        shortData[image] = 1;
        shortData[image + 2] = 0;

        Assert.Equal(16, Decode(bad).Single().Pixels.Length);
        var cut = Decode(shortData).Single().Pixels;
        Assert.Equal(Gray(3), cut[..4]);
        Assert.Equal(new byte[4], cut[12..]);
    }

    [Fact]
    public void NoColorsShowsBlackAndBadInputIsRefused()
    {
        var plain = GifWriter.Write(2, 1, null, [new GifPicture(0, 0, 2, 1, [0, 1]) { NoControl = true }]);
        Assert.Equal([0, 0, 0, 255, 0, 0, 0, 255], Decode(plain).Single().Pixels);

        var track = new TrackInfo { Id = 1, Codec = CodecId.Gif, CodecPrivate = [.. "GIF89a"u8, 2, 0, 1, 0, 0, 0, 0], Video = new VideoTrackInfo { Width = 2, Height = 1 } };
        using var decoder = new GifDecoder(track);
        var frames = new List<VideoFrame>();
        Assert.Throws<MediaFormatException>(() => decoder.Decode(Packet.Create(1, MediaBuffer.CopyOf([1, 2, 3]), MediaTime.Zero, MediaTime.Zero, MediaTime.Zero, true), frames));
        byte[] badCodeSize = [0x21, 0xF9, 4, 0, 0, 0, 0, 0, 0x2C, 0, 0, 0, 0, 2, 0, 1, 0, 0, 12, 0];
        Assert.Throws<MediaFormatException>(() => decoder.Decode(Packet.Create(1, MediaBuffer.CopyOf(badCodeSize), MediaTime.Zero, MediaTime.Zero, MediaTime.Zero, true), frames));
        byte[] shortTable = [0x21, 0xF9, 4, 0, 0, 0, 0, 0, 0x2C, 0, 0, 0, 0, 2, 0, 1, 0, 0x81, 0, 0, 0, 2];
        Assert.Throws<MediaFormatException>(() => decoder.Decode(Packet.Create(1, MediaBuffer.CopyOf(shortTable), MediaTime.Zero, MediaTime.Zero, MediaTime.Zero, true), frames));
        // A sub-block that claims more bytes than there are ends the picture, keeping what was decoded.
        byte[] claimsMore = [0x21, 0xF9, 4, 0, 0, 0, 0, 0, 0x2C, 0, 0, 0, 0, 2, 0, 1, 0, 0, 2, 9, 0x44];
        decoder.Decode(Packet.Create(1, MediaBuffer.CopyOf(claimsMore), MediaTime.Zero, MediaTime.Zero, MediaTime.Zero, true), frames);
        Assert.Single(frames).Dispose();
        frames.Clear();
        byte[] noRoom = [0x21, 0xF9, 4, 0, 0, 0, 0, 0, 0x2C, 0, 0, 0, 0, 2, 0, 1, 0, 0x87, 2];
        Assert.Throws<MediaFormatException>(() => decoder.Decode(Packet.Create(1, MediaBuffer.CopyOf(noRoom), MediaTime.Zero, MediaTime.Zero, MediaTime.Zero, true), frames));
        Assert.Empty(frames);
        Assert.True(decoder.Drain(frames));
        Assert.Equal(("rexplayer GIF", Rex.Media.Codecs.DecoderSource.Own), (decoder.Name, decoder.Source));

        Assert.Throws<MediaFormatException>(() => new GifDecoder(track with { CodecPrivate = [1, 2] }));
        Assert.Throws<MediaFormatException>(() => new GifDecoder(track with { CodecPrivate = [.. "GIF89a"u8, 0, 0, 0, 0, 0, 0, 0], Video = new VideoTrackInfo { Width = 0, Height = 0 } }));
        Assert.Throws<MediaFormatException>(() => new GifDecoder(track with { CodecPrivate = [.. "GIF89a"u8, 2, 0, 1, 0, 0x81, 0, 0] }));
        using var sized = new GifDecoder(track with { CodecPrivate = [.. "GIF89a"u8, 0, 0, 0, 0, 0, 0, 0] });
        Assert.Throws<ArgumentNullException>(() => new GifDecoder(null!));
        Assert.Throws<ArgumentNullException>(() => decoder.Decode(null!, frames));
        Assert.Throws<ArgumentNullException>(() => decoder.Decode(Packet.Create(1, MediaBuffer.CopyOf([1]), MediaTime.Zero, MediaTime.Zero, MediaTime.Zero, true), null!));
    }

    [Fact]
    public void TheFactoryTakesGifPicturesOnly()
    {
        var factory = new GifDecoderFactory();
        var gif = new TrackInfo { Id = 1, Codec = CodecId.Gif, CodecPrivate = [.. "GIF89a"u8, 1, 0, 1, 0, 0, 0, 0], Video = new VideoTrackInfo { Width = 1, Height = 1 } };

        Assert.True(factory.CanDecode(gif));
        Assert.False(factory.CanDecode(gif with { Video = null }));
        Assert.False(factory.CanDecode(gif with { Codec = CodecId.Picture }));
        Assert.Equal(("rexplayer GIF", Rex.Media.Codecs.DecoderSource.Own, 100), (factory.Name, factory.Source, factory.Rank));
        Assert.Throws<NotSupportedException>(() => factory.CreateAudio(gif));
        Assert.Throws<ArgumentNullException>(() => factory.CanDecode(null!));
    }
}
