using Rex.Media.AppCore.Player;
using Rex.Media.Library;
using Rex.Media.TestKit;
using Rex.Media.Engine;
using static Rex.Media.Tests.AppCore.ControllerHarness;

namespace Rex.Media.Tests.AppCore;

/// <summary>Confirmed, file-specific skip bounds never infer a shared series timestamp.</summary>
public sealed class EpisodeSectionsTests
{
    private static readonly TimeSpan Duration = TimeSpan.FromMinutes(43);

    [Fact]
    [Capability("PB-10")]
    public void ExactIntroAndCreditsMarkersAreNeverOfferedOutsideTheirOwnValidatedSpans()
    {
        var empty = new EpisodeSections();
        Assert.Null(empty.Offer(TimeSpan.FromSeconds(10), Duration));
        Assert.Null(empty.Offer(TimeSpan.Zero, TimeSpan.Zero));
        Assert.Null(empty.Offer(TimeSpan.FromSeconds(-1), Duration));
        Assert.Null(empty.Offer(Duration, Duration));

        var start = empty.Mark(EpisodeSectionKind.Intro, true, TimeSpan.FromSeconds(22));
        Assert.Null(start.Offer(TimeSpan.FromSeconds(24), Duration));
        var intro = start.Mark(EpisodeSectionKind.Intro, false, TimeSpan.FromSeconds(95));
        var skip = intro.Offer(TimeSpan.FromSeconds(30), Duration);
        Assert.Equal(new EpisodeSkip(EpisodeSectionKind.Intro, TimeSpan.FromSeconds(95)), skip);
        Assert.Null(intro.Offer(TimeSpan.FromSeconds(21), Duration));
        Assert.Null(intro.Offer(TimeSpan.FromSeconds(95), Duration));
        Assert.Null(intro.Offer(TimeSpan.FromSeconds(94.9), Duration));
        Assert.Null(intro.Offer(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(60)));

        var credits = intro.Mark(EpisodeSectionKind.Credits, true, TimeSpan.FromMinutes(39))
            .Mark(EpisodeSectionKind.Credits, false, TimeSpan.FromMinutes(41));
        Assert.Equal(new EpisodeSkip(EpisodeSectionKind.Credits, TimeSpan.FromMinutes(41)),
            credits.Offer(TimeSpan.FromMinutes(40), Duration));
        Assert.Null(credits.Offer(TimeSpan.FromMinutes(42), Duration));
        Assert.Null(credits.Clear(EpisodeSectionKind.Intro).Offer(TimeSpan.FromSeconds(30), Duration));
        Assert.Equal(new EpisodeSkip(EpisodeSectionKind.Credits, TimeSpan.FromMinutes(41)),
            credits.Clear(EpisodeSectionKind.Intro).Offer(TimeSpan.FromMinutes(40), Duration));
        Assert.Null(credits.Clear(EpisodeSectionKind.Credits).Offer(TimeSpan.FromMinutes(40), Duration));

        Assert.Null(new EpisodeSections(IntroStart: TimeSpan.FromSeconds(20), IntroEnd: TimeSpan.FromSeconds(21))
            .Offer(TimeSpan.FromSeconds(20), Duration));
        Assert.Null(new EpisodeSections(IntroStart: TimeSpan.FromSeconds(40), IntroEnd: TimeSpan.FromSeconds(39))
            .Offer(TimeSpan.FromSeconds(40), Duration));
        Assert.Null(new EpisodeSections(IntroStart: TimeSpan.FromSeconds(-2), IntroEnd: TimeSpan.FromSeconds(60))
            .Offer(TimeSpan.FromSeconds(10), Duration));
        Assert.Null(new EpisodeSections(IntroStart: TimeSpan.FromSeconds(20), IntroEnd: TimeSpan.FromSeconds(60))
            .Offer(TimeSpan.FromSeconds(100), Duration));

        Assert.Throws<ArgumentOutOfRangeException>(() => empty.Mark(EpisodeSectionKind.Intro, true, TimeSpan.FromSeconds(-1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => empty.Mark((EpisodeSectionKind)999, true, TimeSpan.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() => empty.Clear((EpisodeSectionKind)999));
    }

    [Fact]
    [Capability("PB-10")]
    public void TheMarkersPersistPerFileEvenIfHistoryIsDisabled()
    {
        var store = RexStore.InMemory();
        var memory = new PlayerMemory(store) { KeepsHistory = false };
        var intro = new EpisodeSections(TimeSpan.FromSeconds(14), TimeSpan.FromSeconds(71));
        memory.SetSections("D:/Shows/one.mkv", intro);
        Assert.Equal(intro, memory.Sections("d:/shows/ONE.mkv"));
        Assert.Equal(new EpisodeSections(), memory.Sections("D:/Shows/two.mkv"));
        memory.ClearHistory();
        Assert.Equal(intro, memory.Sections("D:/Shows/one.mkv"));
        memory.SetSections("D:/Shows/one.mkv", new EpisodeSections());
        Assert.Equal(new EpisodeSections(), memory.Sections("D:/Shows/one.mkv"));
        Assert.Throws<ArgumentNullException>(() => memory.SetSections("D:/Shows/one.mkv", null!));
    }

    [Fact]
    [Capability("PB-10")]
    public void SeekableVideosCanMarkSkipAndClearDifferentEpisodeSections()
    {
        // A video with an explicit duration keeps the timeline deterministic in the portable
        // controller tests. A fake video decoder never opens a camera or produces sound.
        var video = Rex.Media.TestKit.EbmlWriter.Element(
            Rex.Media.Containers.Matroska.MatroskaId.TrackEntry,
            Rex.Media.TestKit.EbmlWriter.UInt(Rex.Media.Containers.Matroska.MatroskaId.TrackNumber, 2),
            Rex.Media.TestKit.EbmlWriter.UInt(Rex.Media.Containers.Matroska.MatroskaId.TrackType, 1),
            Rex.Media.TestKit.EbmlWriter.Text(Rex.Media.Containers.Matroska.MatroskaId.CodecId, "V_MPEG4/ISO/AVC"),
            Rex.Media.TestKit.EbmlWriter.Element(Rex.Media.Containers.Matroska.MatroskaId.Video,
                Rex.Media.TestKit.EbmlWriter.UInt(Rex.Media.Containers.Matroska.MatroskaId.PixelWidth, 16),
                Rex.Media.TestKit.EbmlWriter.UInt(Rex.Media.Containers.Matroska.MatroskaId.PixelHeight, 8)));
        var info = Rex.Media.TestKit.EbmlWriter.Element(Rex.Media.Containers.Matroska.MatroskaId.Info,
            Rex.Media.TestKit.EbmlWriter.UInt(Rex.Media.Containers.Matroska.MatroskaId.TimestampScale, 1_000_000),
            Rex.Media.TestKit.EbmlWriter.Float(Rex.Media.Containers.Matroska.MatroskaId.Duration, 10_000));
        var frames = Enumerable.Range(0, 200)
            .Select(i => Rex.Media.Tests.Containers.MatroskaCraftedTests.Simple(2, (short)(i * 40), true, (byte)i))
            .ToArray();
        var bytes = Rex.Media.Tests.Containers.MatroskaCraftedTests.Mkv(
            info, Rex.Media.Tests.Containers.MatroskaCraftedTests.Tracks(video),
            Rex.Media.Tests.Containers.MatroskaCraftedTests.Cluster(0, frames));

        using var harness = new ControllerHarness(autoPlay: false, pictures: true);
        harness.Files["pilot.mkv"] = bytes;
        var player = harness.Controller;
        player.Open(["pilot.mkv"]);
        harness.PumpUntil(p => p.State == SessionState.Ready && p.Info is not null);
        Assert.True(player.CanMarkEpisodeSections);
        Assert.Equal(TimeSpan.FromSeconds(10), player.Duration);
        Assert.Null(player.AvailableEpisodeSkip);
        Assert.False(player.SkipEpisodeSection());

        Assert.True(player.MarkEpisodeSection(EpisodeSectionKind.Intro, true));
        player.Seek(TimeSpan.FromSeconds(3));
        Assert.True(player.MarkEpisodeSection(EpisodeSectionKind.Intro, false));
        player.Seek(TimeSpan.FromSeconds(1));
        Assert.Equal(new EpisodeSkip(EpisodeSectionKind.Intro, TimeSpan.FromSeconds(3)),
            player.AvailableEpisodeSkip);
        Assert.True(player.SkipEpisodeSection());
        Assert.Equal(TimeSpan.FromSeconds(3), player.Position);

        player.Seek(TimeSpan.FromSeconds(5));
        Assert.True(player.MarkEpisodeSection(EpisodeSectionKind.Credits, true));
        Assert.True(player.MarkEpisodeSection(EpisodeSectionKind.Credits, false, atVideoEnd: true));
        Assert.Equal(new EpisodeSkip(EpisodeSectionKind.Credits, TimeSpan.FromSeconds(10)),
            player.AvailableEpisodeSkip);
        Assert.True(player.ClearEpisodeSection(EpisodeSectionKind.Intro));
        Assert.Null(player.CurrentSections.IntroStart);
        Assert.True(player.ClearEpisodeSection(EpisodeSectionKind.Credits));
        Assert.Null(player.AvailableEpisodeSkip);
        Assert.True(harness.Changes >= 4);
    }

    [Fact]
    [Capability("PB-10")]
    public void SongsAndUnseekableMediaCannotBeGivenIntroOrCreditsSkips()
    {
        using var harness = new ControllerHarness(autoPlay: false);
        harness.Files["song.wav"] = Count(0, 8_000 * 5);
        var player = harness.Controller;
        Assert.False(player.CanMarkEpisodeSections);
        Assert.Null(player.AvailableEpisodeSkip);
        Assert.Equal(new EpisodeSections(), player.CurrentSections);
        Assert.False(player.MarkEpisodeSection(EpisodeSectionKind.Intro, true));
        Assert.False(player.ClearEpisodeSection(EpisodeSectionKind.Credits));
        Assert.False(player.SkipEpisodeSection());

        player.Open(["song.wav"]);
        harness.PumpUntil(c => c.State == SessionState.Ready);
        Assert.False(player.CanMarkEpisodeSections);
        Assert.False(player.MarkEpisodeSection(EpisodeSectionKind.Credits, true));
        Assert.False(player.ClearEpisodeSection(EpisodeSectionKind.Intro));
        Assert.False(player.SkipEpisodeSection());
    }
}
