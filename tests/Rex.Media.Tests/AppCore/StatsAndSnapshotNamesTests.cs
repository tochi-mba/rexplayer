using Rex.Media.AppCore;
using Rex.Media.AppCore.Player;
using Rex.Media.Codecs;
using Rex.Media.Engine;
using Rex.Media.Primitives;
using Rex.Media.TestKit;

namespace Rex.Media.Tests.AppCore;

public sealed class StatsAndSnapshotNamesTests
{
    private static readonly MediaInfo Film = new()
    {
        FormatName = "MP4",
        Tracks =
        [
            new TrackInfo { Id = 1, Codec = CodecId.H264, Video = new VideoTrackInfo { Width = 1920, Height = 1080, FrameRate = new Rational(24000, 1001) } },
            new TrackInfo { Id = 2, Codec = CodecId.Aac, Audio = new AudioTrackInfo { SampleRate = 48000, Channels = 2 } },
        ],
    };

    [Fact]
    [Capability("VID-31")]
    public void TheOverlaySaysWhatDecodesWhereAndHowItIsGoing()
    {
        var earlier = new SessionStats { BytesRead = 1_000_000 };
        var now = new SessionStats
        {
            BytesRead = 1_500_000,
            VideoDecoder = "Microsoft H264 Video Decoder MFT",
            VideoDecoderSource = DecoderSource.OsHardware,
            VideoFramesPresented = 240,
            VideoFramesDropped = 2,
            AudioDecoder = "Microsoft AAC Audio Decoder MFT",
            CorruptPackets = 3,
        };

        var text = StatsText.Describe(now, Film, audioTrack: 2, earlier, TimeSpan.FromSeconds(2)).Split(Environment.NewLine);

        Assert.Equal(
        [
            "Video     H.264 / AVC 1920\u00D71080, 23.976 fps",
            "Decoder   Microsoft H264 Video Decoder MFT (graphics card)",
            "Pictures  240 shown, 2 dropped",
            "Audio     AAC 48000 Hz, 2 ch",
            "Decoder   Microsoft AAC Audio Decoder MFT",
            "Read      1.4 MB, 2000 kb/s",
            "Damaged   3 packet(s) skipped",
        ],
            text);
    }

    [Theory]
    [InlineData(DecoderSource.OsSoftware, "Windows, in software")]
    [InlineData(DecoderSource.Own, "rexplayer")]
    [InlineData(null, "not decoding")]
    public void TheOverlayNamesWhereThePicturesAreDecoded(DecoderSource? source, string where)
    {
        var text = StatsText.Describe(new SessionStats { VideoDecoderSource = source }, Film, audioTrack: null);

        Assert.Contains($"Decoder   none ({where})", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Audio", text, StringComparison.Ordinal);
    }

    [Fact]
    public void SoundOnlyOrSilentMediaGetsTheLinesThatApply()
    {
        var song = new MediaInfo { FormatName = "FLAC", Tracks = [Film.Tracks[1]] };
        var silent = new MediaInfo { FormatName = "Matroska", Tracks = [Film.Tracks[0] with { Video = new VideoTrackInfo { Width = 16, Height = 8 } }] };

        Assert.Equal(["Audio     AAC 48000 Hz, 2 ch", "Decoder   none", "Read      0.0 MB"], StatsText.Describe(new SessionStats(), song, 2).Split(Environment.NewLine));
        Assert.Contains("Video     H.264 / AVC 16\u00D78" + Environment.NewLine, StatsText.Describe(new SessionStats(), silent, -1), StringComparison.Ordinal);
        Assert.Contains("Audio     none (silence)", StatsText.Describe(new SessionStats(), silent, -1), StringComparison.Ordinal);
        Assert.Equal("Read      0.0 MB", StatsText.Describe(new SessionStats(), null, null));
        Assert.Throws<ArgumentNullException>(() => StatsText.Describe(null!, null, null));
    }

    [Fact]
    public void SnapshotsAreNamedForWhatAndWhenAndNeverOverwriteEachOther()
    {
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Path.Combine("shots", "Sungba 0-01-23.png") };

        Assert.Equal(Path.Combine("shots", "Sungba 0-01-23 (2).png"), Snapshot.FileFor("shots", "Sungba", TimeSpan.FromSeconds(83), taken.Contains));
        Assert.Equal(Path.Combine("shots", "Terminator 1-00-00.png"), Snapshot.FileFor("shots", "Terminator", TimeSpan.FromHours(1), _ => false));
        Assert.Equal(Path.Combine("shots", "a_b_c_d 0-00-05.png"), Snapshot.FileFor("shots", "a/b:c?d.", TimeSpan.FromSeconds(5), _ => false));
        Assert.Equal(Path.Combine("shots", "snapshot 0-00-00.png"), Snapshot.FileFor("shots", " ... ", TimeSpan.Zero, _ => false));
        Assert.StartsWith(Path.Combine(Path.GetTempPath(), "rexplayer-no-such-file-"), Snapshot.FileFor(Path.GetTempPath(), "rexplayer-no-such-file-" + Guid.NewGuid().ToString("N"), TimeSpan.Zero), StringComparison.Ordinal);
    }
}
