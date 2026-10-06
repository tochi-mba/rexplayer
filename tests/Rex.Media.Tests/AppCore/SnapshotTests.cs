using System.Text.Json;
using Rex.Media.AppCore;
using Rex.Media.AppCore.Cli;
using Rex.Media.Codecs;
using Rex.Media.Containers;
using Rex.Media.Primitives;
using Rex.Media.TestKit;
using Rex.Media.Tests.Containers;
using static Rex.Media.TestKit.EbmlWriter;
using Id = Rex.Media.Containers.Matroska.MatroskaId;

namespace Rex.Media.Tests.AppCore;

/// <summary>Which picture a snapshot takes, with a decoder that turns each block into a grey picture of its first byte.</summary>
public sealed class SnapshotTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("rexplayer-snapshot-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    /// <summary>A Matroska H.264 track of 16x8 pictures, one block per time, each block's payload the index of its picture.</summary>
    private static byte[] Video(params int[] milliseconds)
    {
        var track = Element(Id.TrackEntry, UInt(Id.TrackNumber, 1), UInt(Id.TrackType, 1), Text(Id.CodecId, "V_MPEG4/ISO/AVC"), Element(Id.Video, UInt(Id.PixelWidth, 16), UInt(Id.PixelHeight, 8)));
        return MatroskaCraftedTests.Mkv(
            MatroskaCraftedTests.Tracks(track),
            MatroskaCraftedTests.Cluster(0, [.. milliseconds.Select((ms, i) => MatroskaCraftedTests.Simple(1, (short)ms, i == 0, (byte)i))]));
    }

    private static IDemuxer Open(byte[] file) => MatroskaDemuxerTests.Open(file);

    private static DecoderRegistry Decoders(GreyDecoderFactory factory) => new DecoderRegistry().Add(factory);

    [Theory]
    [InlineData(0.05, 1, 3)]
    [InlineData(0.0, 0, 2)]
    [InlineData(0.04, 1, 3)]
    [InlineData(9.0, 3, 4)]
    public void TheSnapshotIsTheLastPictureShownAtOrBeforeTheMoment(double at, int picture, int decoded)
    {
        var factory = new GreyDecoderFactory();
        using var demuxer = Open(Video(0, 40, 80, 120));

        var (frame, decoder) = Snapshot.Take(demuxer, Decoders(factory), MediaTime.FromSeconds(at), CancellationToken.None);
        using (frame)
        {
            Assert.Equal(picture, frame.Row(0, 0)[0]);
            Assert.Equal(MediaTime.FromSeconds(picture * 0.04), frame.Pts);
            Assert.Equal("grey", decoder);
            Assert.Equal(decoded, factory.Decoded);
        }
    }

    [Fact]
    public void AMomentBeforeTheFirstPictureTakesTheFirst()
    {
        using var demuxer = Open(Video(100, 140));

        var (frame, _) = Snapshot.Take(demuxer, Decoders(new GreyDecoderFactory()), MediaTime.Zero, CancellationToken.None);

        using (frame)
        {
            Assert.Equal(0, frame.Row(0, 0)[0]);
        }
    }

    [Fact]
    public void PicturesWithoutTimesAndHeldPicturesAreConsidered()
    {
        var factory = new GreyDecoderFactory { Untimed = true, Hold = true };
        using var demuxer = Open(Video(0, 40));

        var (frame, _) = Snapshot.Take(demuxer, Decoders(factory), MediaTime.FromSeconds(1), CancellationToken.None);

        using (frame)
        {
            Assert.Equal(1, frame.Row(0, 0)[0]);
            Assert.False(frame.Pts.IsKnown);
        }
    }

    [Fact]
    public void FilesWithoutAPictureToTakeAreRefused()
    {
        var audioOnly = MatroskaCraftedTests.Mkv(MatroskaCraftedTests.Tracks(MatroskaCraftedTests.PcmTrack()));
        using var silent = Open(audioOnly);
        using var empty = Open(Video(0));
        using var broken = Open(Video(0, 40));

        Assert.Throws<NotSupportedException>(() => Snapshot.Take(silent, Decoders(new GreyDecoderFactory()), MediaTime.Zero, CancellationToken.None));
        var none = Assert.Throws<NotSupportedException>(() => Snapshot.Take(empty, new DecoderRegistry(), MediaTime.Zero, CancellationToken.None));
        Assert.Contains("No decoder", none.Message, StringComparison.Ordinal);
        Assert.Throws<MediaFormatException>(() => Snapshot.Take(empty, Decoders(new GreyDecoderFactory { Silent = true }), MediaTime.Zero, CancellationToken.None));
        Assert.Throws<MediaFormatException>(() => Snapshot.Take(broken, Decoders(new GreyDecoderFactory { FailAfter = 1, Hold = true }), MediaTime.FromSeconds(1), CancellationToken.None));
        using var partWay = Open(Video(0, 40));
        Assert.Throws<MediaFormatException>(() => Snapshot.Take(partWay, Decoders(new GreyDecoderFactory { FailAfter = 1, FailAfterGiving = true }), MediaTime.FromSeconds(1), CancellationToken.None));
        Assert.Throws<ArgumentNullException>(() => Snapshot.Take(null!, new DecoderRegistry(), MediaTime.Zero, CancellationToken.None));
        Assert.Throws<ArgumentNullException>(() => Snapshot.Take(empty, null!, MediaTime.Zero, CancellationToken.None));
    }

    private static (int Exit, string Out) Run(params string[] args)
    {
        using var output = new StringWriter();
        var host = new CliHost { Out = output, Error = output, Version = "9.9.9", ExtraDecoders = [new GreyDecoderFactory()] };
        return (CliApplication.Run(args, host), output.ToString());
    }

    [Fact]
    [Capability("PB-16")]
    public void TheCommandSavesThePictureAsAPng()
    {
        var video = Path.Combine(_directory, "clip.mkv");
        System.IO.File.WriteAllBytes(video, Video(0, 40, 80));
        var chosen = Path.Combine(_directory, "chosen.png");

        var (exit, output) = Run("snapshot", video, "--at", "0.05", "--out", chosen, "--json");
        var (humanExit, human) = Run("snapshot", video);

        Assert.Equal((0, 0), (exit, humanExit));
        using var document = JsonDocument.Parse(output);
        var data = document.RootElement.GetProperty("data");
        Assert.Equal(("clip.mkv", chosen, 0.04, 16, 8, "grey"), (data.GetProperty("file").GetString(), data.GetProperty("out").GetString(), data.GetProperty("at").GetDouble(), data.GetProperty("width").GetInt32(), data.GetProperty("height").GetInt32(), data.GetProperty("decoder").GetString()));
        var image = PngReader.Read(System.IO.File.ReadAllBytes(chosen));
        Assert.Equal((16, 8), (image.Width, image.Height));
        Assert.Equal($"Saved the 16x8 picture at 0:00 to {Path.Combine(_directory, "clip.png")}.", human.Trim());
        Assert.True(System.IO.File.Exists(Path.Combine(_directory, "clip.png")));
    }

    [Fact]
    public void ThePrintedTimeSaysWhenThePictureHasNone()
    {
        var video = Path.Combine(_directory, "untimed.mkv");
        System.IO.File.WriteAllBytes(video, Video(0));
        using var output = new StringWriter();
        var host = new CliHost { Out = output, Error = output, Version = "9.9.9", ExtraDecoders = [new GreyDecoderFactory { Untimed = true }] };

        Assert.Equal(0, CliApplication.Run(["snapshot", video], host));
        Assert.Contains("at an unknown time", output.ToString(), StringComparison.Ordinal);
        output.GetStringBuilder().Clear();
        Assert.Equal(0, CliApplication.Run(["snapshot", video, "--json"], host));
        using var document = JsonDocument.Parse(output.ToString());
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("data").GetProperty("at").ValueKind);
    }

    /// <summary>Decodes H.264 tracks into grey pictures whose first byte is the packet's first byte.</summary>
    private sealed class GreyDecoderFactory : IDecoderFactory
    {
        public int Decoded { get; private set; }

        /// <summary>Pictures carry no time.</summary>
        public bool Untimed { get; init; }

        /// <summary>Pictures are only given at the drain, as a reordering decoder may.</summary>
        public bool Hold { get; init; }

        /// <summary>No pictures at all.</summary>
        public bool Silent { get; init; }

        /// <summary>Throws on the packet after this many.</summary>
        public int FailAfter { get; init; } = int.MaxValue;

        /// <summary>Gives the failing packet's picture before throwing, as a decoder failing part-way may.</summary>
        public bool FailAfterGiving { get; init; }

        public string Name => "grey";

        public DecoderSource Source => DecoderSource.Own;

        public int Rank => 100;

        public bool CanDecode(TrackInfo track) => track.Codec == CodecId.H264;

        public IAudioDecoder CreateAudio(TrackInfo track) => throw new NotSupportedException();

        public IVideoDecoder CreateVideo(TrackInfo track) => new Decoder(this);

        private sealed class Decoder(GreyDecoderFactory factory) : IVideoDecoder
        {
            private readonly List<VideoFrame> _held = [];

            public string Name => "grey";

            public DecoderSource Source => DecoderSource.Own;

            public void Decode(Packet packet, ICollection<VideoFrame> output)
            {
                if (factory.Decoded >= factory.FailAfter && !factory.FailAfterGiving)
                {
                    throw new MediaFormatException("broken");
                }

                factory.Decoded++;
                if (factory.Silent)
                {
                    return;
                }

                var frame = VideoFrame.Rent(PixelFormat.Gray8, 16, 8);
                frame.Plane(0).Fill(packet.Data.Span[0]);
                frame.Pts = factory.Untimed ? MediaTime.Unknown : packet.Pts;
                (factory.Hold ? _held : output).Add(frame);
                if (factory.Decoded > factory.FailAfter)
                {
                    throw new MediaFormatException("broken part-way");
                }
            }

            public void Drain(ICollection<VideoFrame> output)
            {
                _held.ForEach(output.Add);
                _held.Clear();
            }

            public void Flush()
            {
            }

            public void Dispose() => _held.ForEach(frame => frame.Dispose());
        }
    }
}
