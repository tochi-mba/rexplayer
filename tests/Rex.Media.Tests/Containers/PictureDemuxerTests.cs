using System.Buffers.Binary;
using System.Text;
using Rex.Media.Containers;
using Rex.Media.Containers.Image;
using Rex.Media.Containers.Mp4;
using Rex.Media.IO;
using Rex.Media.Primitives;
using Rex.Media.TestKit;

namespace Rex.Media.Tests.Containers;

/// <summary>Pictures as media (FMT-C19): recognised by their bytes, sized from their headers, shown for a while.</summary>
public sealed class PictureDemuxerTests
{
    private static byte[] Fixture(string name) => File.ReadAllBytes(RepoPaths.Combine("tests", "fixtures", "picture", name));

    private static IDemuxer Open(byte[] data, TimeSpan? showFor = null, string name = "picture") =>
        new PictureDemuxerFactory(showFor).Open(new MemoryByteSource(data, name), CancellationToken.None);

    [Theory]
    [Capability("FMT-C19")]
    [InlineData("still.png", PictureFormat.Png, "PNG")]
    [InlineData("still.jpg", PictureFormat.Jpeg, "JPEG")]
    [InlineData("still.bmp", PictureFormat.Bmp, "BMP")]
    [InlineData("still.tiff", PictureFormat.Tiff, "TIFF")]
    [InlineData("still.webp", PictureFormat.WebP, "WebP")]
    [InlineData("still.gif", PictureFormat.Gif, "GIF")]
    public void EachFormatIsKnownByItsBytesAndSized(string name, PictureFormat format, string formatName)
    {
        var data = Fixture(name);

        Assert.Equal(format, PictureFormats.Detect(data));
        Assert.Equal((64, 48), PictureFormats.SizeOf(format, data));
        Assert.Equal(100, new PictureDemuxerFactory().Probe(data, ".bin"));
        using var demuxer = Open(data);
        Assert.Equal(formatName, demuxer.Info.FormatName);
        Assert.Equal((64, 48), (demuxer.Info.Tracks[0].Video!.Width, demuxer.Info.Tracks[0].Video!.Height));
        Assert.Equal(MediaKind.Video, demuxer.Info.Tracks[0].Kind);
    }

    [Fact]
    [Capability("FMT-C19")]
    public void AStillIsOneFrameShownForAWhileAndAgainAfterASeek()
    {
        var data = Fixture("still.png");
        using var demuxer = Open(data, TimeSpan.FromSeconds(3));

        Assert.Equal(MediaTime.FromSeconds(3), demuxer.Info.Duration);
        Assert.Equal(CodecId.Picture, demuxer.Info.Tracks[0].Codec);
        using (var packet = demuxer.ReadPacket(CancellationToken.None)!)
        {
            Assert.Equal((MediaTime.Zero, MediaTime.FromSeconds(3), true), (packet.Pts, packet.Duration, packet.IsKeyframe));
            Assert.True(packet.Data.Span.SequenceEqual(data));
        }

        Assert.Null(demuxer.ReadPacket(CancellationToken.None));

        // Sent again from where the seek lands, inside the time it shows.
        foreach (var (target, from) in new[] { (2.0, 2.0), (-1.0, 0.0), (9.0, 3.0 - 1e-7) })
        {
            demuxer.Seek(MediaTime.FromSeconds(target), CancellationToken.None);
            using var again = demuxer.ReadPacket(CancellationToken.None)!;
            Assert.Equal(MediaTime.FromSeconds(from), again.Pts);
            Assert.Equal(MediaTime.FromSeconds(3) - MediaTime.FromSeconds(from), again.Duration);
        }

        Assert.Equal(PictureDemuxerFactory.DefaultShowFor, new PictureDemuxerFactory().ShowFor);
        Assert.Equal("picture", new PictureDemuxerFactory().Name);
        Assert.Throws<ArgumentOutOfRangeException>(() => new PictureDemuxerFactory(TimeSpan.Zero));
        Assert.Throws<ArgumentNullException>(() => new PictureDemuxer(PictureFormat.Png, null!, TimeSpan.FromSeconds(1)));
    }

    [Fact]
    [Capability("FMT-C19")]
    public void AnAnimationLoopsUntilItsTimeIsUpAndSeeksToTheStartOfALoop()
    {
        using var demuxer = Open(Fixture("anim.gif"), TimeSpan.FromSeconds(2.5));
        var gif = Assert.IsType<GifDemuxer>(demuxer);

        // Four pictures a second, so three times through covers two and a half seconds.
        Assert.Equal(4, gif.FrameCount);
        Assert.Equal("GIF animation", demuxer.Info.FormatName);
        Assert.Equal(MediaTime.FromSeconds(3), demuxer.Info.Duration);
        var packets = new List<(MediaTime, bool)>();
        while (demuxer.ReadPacket(CancellationToken.None) is { } packet)
        {
            using (packet)
            {
                packets.Add((packet.Pts, packet.IsKeyframe));
            }
        }

        Assert.Equal(12, packets.Count);
        Assert.Equal((MediaTime.FromSeconds(1.25), false), packets[5]);
        Assert.Equal((MediaTime.FromSeconds(2), true), packets[8]);

        demuxer.Seek(MediaTime.FromSeconds(1.6), CancellationToken.None);
        Assert.Equal(MediaTime.FromSeconds(1), demuxer.ReadPacket(CancellationToken.None)!.Pts);
        demuxer.Seek(MediaTime.FromSeconds(99), CancellationToken.None);
        Assert.Equal(MediaTime.FromSeconds(2), demuxer.ReadPacket(CancellationToken.None)!.Pts);
        demuxer.Seek(MediaTime.FromSeconds(-1), CancellationToken.None);
        Assert.Equal(MediaTime.Zero, demuxer.ReadPacket(CancellationToken.None)!.Pts);
    }

    [Fact]
    [Capability("FMT-C19")]
    public void AGifIsReadAsFarAsItsWholePicturesGo()
    {
        var colors = GifWriter.Grays(4);
        var pictures = new[]
        {
            new GifPicture(0, 0, 2, 2, [0, 1, 2, 3]) { Delay = 0 },
            new GifPicture(0, 0, 2, 2, [3, 2, 1, 0]) { Delay = 50, Colors = colors },
            new GifPicture(0, 0, 1, 1, [1]) { NoControl = true },
        };
        var whole = GifWriter.Write(0, 0, colors, pictures);

        // Delays under 20 ms play at 100 ms; no control extension means no delay either.
        using (var demuxer = (GifDemuxer)Open(whole, TimeSpan.FromMilliseconds(1)))
        {
            Assert.Equal(3, demuxer.FrameCount);
            Assert.Equal((2, 2), (demuxer.Info.Tracks[0].Video!.Width, demuxer.Info.Tracks[0].Video!.Height));
            Assert.Equal(MediaTime.FromMilliseconds(700), demuxer.Info.Duration);
        }

        // Cut short anywhere in the last picture, or its extension, the whole ones before are the file.
        var lastPicture = Array.LastIndexOf(whole, (byte)0x2C);
        foreach (var cut in new[] { whole.Length - 2, lastPicture + 5, lastPicture + 10, lastPicture + 11 })
        {
            using var demuxer = (GifDemuxer)Open(whole[..cut]);
            Assert.Equal(2, demuxer.FrameCount);
        }

        // A comment extension is passed over; bytes that are no block end the file.
        var commented = GifWriter.Write(2, 2, null, [pictures[0]], trailer: false);
        using (var demuxer = (GifDemuxer)Open([.. commented[..^0], 0x21, 0xFE, 2, (byte)'h', (byte)'i', 0, 0x99, 0x2C]))
        {
            Assert.Equal(1, demuxer.FrameCount);
            Assert.Equal("GIF", demuxer.Info.FormatName);
        }

        using (var untrailed = (GifDemuxer)Open(commented))
        {
            Assert.Equal(1, untrailed.FrameCount);
        }

        Assert.Equal("GIF", PictureFormats.Name(PictureFormat.Gif));
        using (var extensionCut = (GifDemuxer)Open([.. commented, 0x21]))
        {
            Assert.Equal(1, extensionCut.FrameCount);
        }

        using (var controlCut = (GifDemuxer)Open([.. commented, 0x21, 0xF9, 4, 0]))
        {
            Assert.Equal(1, controlCut.FrameCount);
        }

        Assert.Throws<MediaFormatException>(() => Open(whole[..12]));
        Assert.Throws<MediaFormatException>(() => Open([.. "GIF89a"u8, 2, 0, 2, 0, 0x81, 0, 0, 1, 2]));
        Assert.Throws<MediaFormatException>(() => Open([.. "GIF89a"u8, 2, 0, 2, 0, 0, 0, 0, 0x3B]));
        Assert.Throws<ArgumentNullException>(() => new GifDemuxer(null!, TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void ReadingTheWholeFileNeedsItsSizeAndASizeAPictureCouldBe()
    {
        Assert.Throws<MediaFormatException>(() => PictureDemuxer.ReadAll(new UnsizedSource(Fixture("still.png")), CancellationToken.None));
        Assert.Throws<MediaFormatException>(() => PictureDemuxer.ReadAll(new UnsizedSource(Fixture("still.png"), PictureDemuxer.MaxBytes + 1L), CancellationToken.None));
        Assert.Equal(3, PictureDemuxer.ReadAll(new UnsizedSource([1, 2, 3], 10), CancellationToken.None).Length);
        Assert.Throws<MediaFormatException>(() => new PictureDemuxerFactory().Open(new MemoryByteSource(new byte[] { 1, 2, 3 }, "x"), CancellationToken.None));
        Assert.Throws<ArgumentNullException>(() => PictureDemuxer.ReadAll(null!, CancellationToken.None));
    }

    [Theory]
    [InlineData(new byte[] { 0xFF, 0xD8 }, null)]
    [InlineData(new byte[] { (byte)'B', (byte)'M', 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 99, 0, 0, 0 }, null)]
    [InlineData(new byte[] { (byte)'I', (byte)'I', 42, 0 }, PictureFormat.Tiff)]
    [InlineData(new byte[] { (byte)'M', (byte)'M', 0, 42 }, PictureFormat.Tiff)]
    [InlineData(new byte[] { 1, 2, 3 }, null)]
    public void OnlyTrueSignaturesCount(byte[] head, PictureFormat? format) => Assert.Equal(format, PictureFormats.Detect(head));

    [Fact]
    [Capability("FMT-C19")]
    public void HeifAndAvifAreKnownByTheirBrandsAndTheMp4ReaderLeavesThem()
    {
        byte[] Ftyp(string major, params string[] compatible) => [.. U32(16 + (4 * compatible.Length)), .. "ftyp"u8, .. Encoding.ASCII.GetBytes(major), 0, 0, 0, 0, .. compatible.SelectMany(Encoding.ASCII.GetBytes)];

        var heic = Ftyp("heic", "mif1");
        var avif = Ftyp("avis", "msf1");
        var tagged = Ftyp("isom", "mp41", "mif1");
        var movie = Ftyp("isom", "mp41", "avc1");

        Assert.All(new[] { heic, avif, tagged }, head => Assert.Equal(PictureFormat.Heif, PictureFormats.Detect(head)));
        Assert.Null(PictureFormats.Detect(movie));
        Assert.Null(PictureFormats.Detect(heic.AsSpan(0, 12)));
        Assert.Equal(0, new Mp4DemuxerFactory().Probe(heic, ".heic"));
        Assert.Equal(100, new Mp4DemuxerFactory().Probe(movie, ".mp4"));
        Assert.Equal("HEIF", PictureFormats.Name(PictureFormat.Heif));

        // The size is the largest image spatial extents property: the picture, not its thumbnail.
        byte[] Box(string type, params byte[][] parts) => [.. U32(8 + parts.Sum(p => p.Length)), .. Encoding.ASCII.GetBytes(type), .. parts.SelectMany(p => p)];
        byte[] Ispe(int width, int height) => Box("ispe", [0, 0, 0, 0], U32(width), U32(height));
        var meta = Box("meta", [0, 0, 0, 0], Box("hdlr", new byte[20]), Box("iprp", Box("ipco", Box("colr", [1]), Ispe(320, 240), Ispe(4032, 3024))));
        Assert.Equal((4032, 3024), PictureFormats.SizeOf(PictureFormat.Heif, [.. heic, .. meta]));

        // A large size field, a box running to the end, and boxes that do not fit.
        byte[] large = [0, 0, 0, 1, .. "meta"u8, 0, 0, 0, 0, 0, 0, 0, 24, 0, 0, 0, 0, .. Box("free")];
        Assert.Equal((0, 0), PictureFormats.SizeOf(PictureFormat.Heif, [.. heic, .. large]));
        Assert.Equal((0, 0), PictureFormats.SizeOf(PictureFormat.Heif, [.. heic, 0, 0, 0, 0, .. "free"u8]));
        Assert.Equal((0, 0), PictureFormats.SizeOf(PictureFormat.Heif, [.. heic, 0, 0, 0, 99, .. "meta"u8]));
        Assert.Equal((0, 0), PictureFormats.SizeOf(PictureFormat.Heif, [.. heic, 0, 0, 0, 4, .. "meta"u8]));
        Assert.Equal((0, 0), PictureFormats.SizeOf(PictureFormat.Heif, [.. heic, .. Box("meta", [0, 0])]));
        Assert.Equal((0, 0), PictureFormats.SizeOf(PictureFormat.Heif, [.. heic, .. Box("meta", [0, 0, 0, 0], Box("iprp", Box("free")))]));
    }

    [Fact]
    [Capability("FMT-C19")]
    public void SizesComeFromEveryKindOfHeader()
    {
        // JPEG: fill bytes, a standalone marker and segments before the frame header; a damaged length.
        byte[] jpeg = [0xFF, 0xD8, 0xFF, 0xFF, 0xD0, 0xFF, 0xE0, 0, 4, 0, 0, 0xFF, 0xC4, 0, 2, 0xFF, 0xC2, 0, 11, 8, 0, 30, 0, 40, 3];
        Assert.Equal((40, 30), PictureFormats.SizeOf(PictureFormat.Jpeg, jpeg));
        Assert.Equal((0, 0), PictureFormats.SizeOf(PictureFormat.Jpeg, [0xFF, 0xD8, 0xFF, 0xE0, 0, 1, 0, 0]));
        Assert.Equal((0, 0), PictureFormats.SizeOf(PictureFormat.Jpeg, [0xFF, 0xD8, 0xFF, 0xC0, 0, 11, 8]));
        Assert.Equal((0, 0), PictureFormats.SizeOf(PictureFormat.Jpeg, [0xFF, 0xD8, 0x00, 0x00, 0, 0]));

        // WebP: lossy, lossless and extended headers; one too short.
        byte[] Webp(string chunk, params byte[] body) => [.. "RIFF"u8, 0, 0, 0, 0, .. "WEBP"u8, .. Encoding.ASCII.GetBytes(chunk), 0, 0, 0, 0, .. body, .. new byte[16]];
        Assert.Equal((300, 200), PictureFormats.SizeOf(PictureFormat.WebP, Webp("VP8 ", 0, 0, 0, 0x9D, 0x01, 0x2A, 44, 1, 200, 0)));
        var lossless = (uint)(299 | (199 << 14));
        Assert.Equal((300, 200), PictureFormats.SizeOf(PictureFormat.WebP, Webp("VP8L", [0x2F, .. BitConverter.GetBytes(lossless)])));
        Assert.Equal((0, 0), PictureFormats.SizeOf(PictureFormat.WebP, Webp("VP8L", 0x00)));
        Assert.Equal((70000, 2), PictureFormats.SizeOf(PictureFormat.WebP, Webp("VP8X", 0, 0, 0, 0, 0x6F, 0x11, 0x01, 1, 0, 0)));
        Assert.Equal((0, 0), PictureFormats.SizeOf(PictureFormat.WebP, Webp("ALPH")));
        Assert.Equal((0, 0), PictureFormats.SizeOf(PictureFormat.WebP, [.. "RIFF"u8, 0, 0, 0, 0, .. "WEBP"u8]));

        // BMP: an old OS/2 header, and a bottom-up and a top-down one; too short.
        byte[] Bmp(uint header, params byte[] rest) => [(byte)'B', (byte)'M', .. new byte[12], .. U32Le(header), .. rest, .. new byte[8]];
        Assert.Equal((5, 6), PictureFormats.SizeOf(PictureFormat.Bmp, Bmp(12, 5, 0, 6, 0)));
        Assert.Equal((5, 6), PictureFormats.SizeOf(PictureFormat.Bmp, Bmp(40, [.. U32Le(5), .. U32Le(6)])));
        Assert.Equal((5, 6), PictureFormats.SizeOf(PictureFormat.Bmp, Bmp(40, [.. U32Le(5), .. BitConverter.GetBytes(-6)])));
        Assert.Equal((0, 0), PictureFormats.SizeOf(PictureFormat.Bmp, [(byte)'B', (byte)'M', 0]));

        // TIFF: big-endian, LONG and SHORT values, a tag of another type, a directory past the end or cut short.
        byte[] tiff = [(byte)'M', (byte)'M', 0, 42, 0, 0, 0, 8, 0, 4,
            1, 0, 0, 4, 0, 0, 0, 1, 0, 0, 1, 0,
            1, 1, 0, 3, 0, 0, 0, 1, 0, 200, 0, 0,
            1, 2, 0, 5, 0, 0, 0, 1, 0, 0, 0, 0,
            0, 1, 0, 3, 0, 0, 0, 1, 0, 1, 0, 0];
        Assert.Equal((256, 200), PictureFormats.SizeOf(PictureFormat.Tiff, tiff));
        Assert.Equal((0, 0), PictureFormats.SizeOf(PictureFormat.Tiff, tiff.AsSpan(0, 30)));
        Assert.Equal((0, 0), PictureFormats.SizeOf(PictureFormat.Tiff, [(byte)'I', (byte)'I', 42, 0, 99, 0, 0, 0]));
        Assert.Equal((0, 0), PictureFormats.SizeOf(PictureFormat.Tiff, [(byte)'I', (byte)'I', 42, 0]));

        // PNG and GIF cut short.
        Assert.Equal((0, 0), PictureFormats.SizeOf(PictureFormat.Png, Fixture("still.png").AsSpan(0, 20)));
        Assert.Equal((0, 0), PictureFormats.SizeOf(PictureFormat.Gif, Fixture("still.gif").AsSpan(0, 9)));
    }

    private static byte[] U32(int value)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(bytes, value);
        return bytes;
    }

    private static byte[] U32Le(uint value) => BitConverter.GetBytes(value);

    /// <summary>A source that says a size of its own choosing, or none.</summary>
    private sealed class UnsizedSource(byte[] data, long? length = null) : IByteSource
    {
        public string Name => "unsized";

        public long? Length => length;

        public bool CanSeek => true;

        public int Read(long position, Span<byte> destination, CancellationToken cancellationToken)
        {
            if (position >= data.Length)
            {
                return 0;
            }

            var count = (int)Math.Min(destination.Length, data.Length - position);
            data.AsSpan((int)position, count).CopyTo(destination);
            return count;
        }

        public void Dispose()
        {
        }
    }
}
