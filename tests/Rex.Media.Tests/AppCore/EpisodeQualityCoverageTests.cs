using System.IO.Compression;
using System.Text;
using Rex.Media.AppCore.Player;

namespace Rex.Media.Tests.AppCore;

/// <summary>Exercise the saved, proposed and persisted parts of episode skipping together.</summary>
public sealed class EpisodeQualityCoverageTests
{
    private static readonly TimeSpan Length = TimeSpan.FromMinutes(42);

    [Fact]
    public void ViewsPreferSavedBoundariesAndNeverShowARejectedProposal()
    {
        var intro = new SectionCandidate(EpisodeSectionKind.Intro, TimeSpan.FromSeconds(8),
            TimeSpan.FromSeconds(75), 0.97, "Repeated opening", "audio");
        var credits = new SectionCandidate(EpisodeSectionKind.Credits, TimeSpan.FromMinutes(40),
            TimeSpan.FromMinutes(41), 0.95, "Repeated closing", "audio");
        var proposal = new SectionProposals("signed", [intro, credits]);

        var offered = EpisodeSectionView.For(new EpisodeSections(), proposal, Length);
        Assert.Equal(2, offered.Count);
        Assert.Equal("intro", offered[0].Noun);
        Assert.Equal("credits", offered[1].Noun);
        Assert.Equal(intro.Start, offered[0].Start);
        Assert.Equal(credits.End, offered[1].End);
        Assert.True(offered[0].CanHide);
        Assert.False(offered[0].Saved);

        var hidden = EpisodeSectionView.For(new EpisodeSections(),
            proposal.Reject(EpisodeSectionKind.Intro), Length);
        Assert.True(hidden[0].Hidden);
        Assert.Null(hidden[0].Start);
        Assert.Equal(intro, hidden[0].Detected);
        Assert.True(hidden[0].CanHide);
        Assert.False(hidden[1].Hidden);

        var saved = new EpisodeSections(TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(90));
        var shown = EpisodeSectionView.For(saved, proposal, Length);
        Assert.True(shown[0].Saved);
        Assert.True(shown[0].HasSavedBoundary);
        Assert.False(shown[0].CanHide);
        Assert.Equal(TimeSpan.FromSeconds(20), shown[0].Start);
        Assert.Equal(TimeSpan.FromSeconds(90), shown[0].End);
        Assert.Equal(intro, shown[0].Detected);

        var partial = EpisodeSectionView.For(
            new EpisodeSections(IntroStart: TimeSpan.FromSeconds(20)), proposal, Length);
        Assert.True(partial[0].HasSavedBoundary);
        Assert.Null(partial[0].Start);
        Assert.Null(partial[0].End);
        Assert.False(partial[0].CanHide);
        Assert.Equal(2, EpisodeSectionView.For(new EpisodeSections(), null, Length).Count);
        Assert.Throws<ArgumentNullException>(() => EpisodeSectionView.For(null!, proposal, Length));
    }

    [Fact]
    public void SpansAndSkipHintsRespectExplicitCreditsAndScenesAfterThem()
    {
        var saved = new EpisodeSections(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(75),
            TimeSpan.FromMinutes(40));
        Assert.Equal((TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(75)),
            saved.Span(EpisodeSectionKind.Intro, Length));
        Assert.Equal((TimeSpan.FromMinutes(40), Length),
            saved.Span(EpisodeSectionKind.Credits, Length));
        Assert.Null(new EpisodeSections(IntroEnd: TimeSpan.FromSeconds(75))
            .Span(EpisodeSectionKind.Intro, Length));
        Assert.Throws<ArgumentOutOfRangeException>(() => saved.Span((EpisodeSectionKind)42, Length));

        var intro = EpisodeSkipPrompt.For(
            new EpisodeSkip(EpisodeSectionKind.Intro, TimeSpan.FromSeconds(75)),
            null, Length, TimeSpan.FromSeconds(40), false, null);
        Assert.Equal("Go to where the intro ends (I)", intro.Hint(null, "I"));

        var credits = EpisodeSkipPrompt.For(
            new EpisodeSkip(EpisodeSectionKind.Credits, Length - TimeSpan.FromSeconds(5)),
            null, Length, Length - TimeSpan.FromSeconds(15), false, null);
        Assert.Equal("Go to the end", credits.Hint(null, ""));
        var next = EpisodeSkipPrompt.For(
            new EpisodeSkip(EpisodeSectionKind.Credits, Length - TimeSpan.FromSeconds(5)),
            null, Length, Length - TimeSpan.FromSeconds(15), true, null);
        Assert.Equal("Play Episode 3 (Shift+N)", next.Hint("Episode 3", "Shift+N"));
        var scene = EpisodeSkipPrompt.For(
            new EpisodeSkip(EpisodeSectionKind.Credits, Length - TimeSpan.FromMinutes(2)),
            null, Length, Length - TimeSpan.FromMinutes(3), true, null);
        Assert.Equal("Go to the scene after the credits", scene.Hint(null, ""));
    }

    [Fact]
    public void DiskCacheRoundTripsLandmarksAndCreditFramesAndRejectsOldSignatures()
    {
        var folder = Directory.CreateTempSubdirectory("rex-episode-analysis-").FullName;
        try
        {
            var cache = new SectionAnalysisCache(folder);
            const string file = "S01E02.mkv";
            var opening = new AudioLandmark[] { new(11u, 100), new(12u, 106) };
            var closing = new AudioLandmark[] { new(21u, 200) };
            var frames = new CreditFrameSample[]
            {
                new(TimeSpan.FromSeconds(35), 0.3, 0.8, 0.1, [0.25f, 0.5f], 0.12),
            };
            var episode = new EpisodeAnalysis(file, "signature", Length, opening, closing, frames);
            cache.Set(episode);
            Assert.Null(cache.Get(file, "changed-content"));
            Assert.Null(cache.Get("missing.mkv", "signature"));
            var found = Assert.IsType<EpisodeAnalysis>(new SectionAnalysisCache(folder).Get(file, "signature"));
            Assert.Equal(episode.Duration, found.Duration);
            Assert.Equal(opening, found.Opening);
            Assert.Equal(closing, found.Closing);
            Assert.Single(found.Frames);
            Assert.Equal(frames[0].At, found.Frames[0].At);
            Assert.Equal(frames[0].Rows, found.Frames[0].Rows);

            using var bytes = new MemoryStream();
            SectionAnalysisCache.Write(bytes, episode);
            bytes.Position = 0;
            Assert.NotNull(SectionAnalysisCache.Read(bytes, file));
            Assert.Throws<ArgumentNullException>(() => cache.Set(null!));
            Assert.Throws<ArgumentNullException>(() => cache.Get(null!, "signature"));
            Assert.Throws<ArgumentNullException>(() => cache.Get(file, null!));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, -1)]
    [InlineData(true, 4000001)]
    public void CacheRejectsOldHeadersAndCorruptCounts(bool validHeader, int count)
    {
        using var bytes = new MemoryStream();
        using (var zlib = new ZLibStream(bytes, CompressionLevel.Fastest, leaveOpen: true))
        using (var writer = new BinaryWriter(zlib, Encoding.UTF8))
        {
            writer.Write(validHeader ? 0x41535852 : 0);
            writer.Write(SectionAnalysis.FeatureVersion);
            writer.Write("signature");
            writer.Write(Length.Ticks);
            writer.Write(count);
        }
        bytes.Position = 0;
        if (!validHeader)
        {
            Assert.Null(SectionAnalysisCache.Read(bytes, "S01E01.mkv"));
        }
        else
        {
            Assert.Throws<InvalidDataException>(() => SectionAnalysisCache.Read(bytes, "S01E01.mkv"));
        }
    }

    [Fact]
    public void CacheEvictsTheOldestAndIgnoresAnUnwritableFolder()
    {
        var folder = Directory.CreateTempSubdirectory("rex-episode-limit-").FullName;
        try
        {
            var cache = new SectionAnalysisCache(folder);
            for (var i = 0; i <= SectionAnalysisCache.Limit; i++)
            {
                cache.Set(new EpisodeAnalysis($"episode-{i}.mkv", "same", Length, [], [], []));
            }

            Assert.Equal(SectionAnalysisCache.Limit, Directory.GetFiles(folder, "*.sections").Length);
            var notAFolder = Path.Combine(folder, "file-instead-of-directory");
            File.WriteAllText(notAFolder, "not a folder");
            new SectionAnalysisCache(notAFolder).Set(
                new EpisodeAnalysis("ignored.mkv", "same", Length, [], [], []));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
