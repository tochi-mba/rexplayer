using Rex.Media.AppCore.Player;
using Rex.Media.Containers.Matroska;
using Rex.Media.Engine;
using Rex.Media.Library;
using Rex.Media.Settings;
using Rex.Media.TestKit;
using Rex.Media.Tests.Containers;

namespace Rex.Media.Tests.AppCore;

/// <summary>The user can act on a strong detection immediately, without accepting or persisting it.</summary>
public sealed class SectionDetectionControllerTests
{
    [Fact]
    public void BackgroundAnalysisCachesSiblingFingerprintsAndNextOpenUsesSavedSuggestions()
    {
        var folder = Directory.CreateTempSubdirectory("rexplayer-section-background-").FullName;
        try
        {
            var fileA = Path.Combine(folder, "Series.S01E01.mkv");
            var fileB = Path.Combine(folder, "Series.S01E02.MKV");
            var video = EbmlWriter.Element(MatroskaId.TrackEntry,
                EbmlWriter.UInt(MatroskaId.TrackNumber, 2),
                EbmlWriter.UInt(MatroskaId.TrackType, 1),
                EbmlWriter.Text(MatroskaId.CodecId, "V_MPEG4/ISO/AVC"),
                EbmlWriter.Element(MatroskaId.Video,
                    EbmlWriter.UInt(MatroskaId.PixelWidth, 16),
                    EbmlWriter.UInt(MatroskaId.PixelHeight, 8)));
            var info = EbmlWriter.Element(MatroskaId.Info,
                EbmlWriter.UInt(MatroskaId.TimestampScale, 1_000_000),
                EbmlWriter.Float(MatroskaId.Duration, 10_000));
            var bytes = MatroskaCraftedTests.Mkv(info, MatroskaCraftedTests.Tracks(video),
                MatroskaCraftedTests.Cluster(0, MatroskaCraftedTests.Simple(2, 0, true, 40)));
            File.WriteAllBytes(fileA, bytes);
            File.WriteAllBytes(fileB, bytes);
            var broken = Path.Combine(folder, "Series.S01E03.mkv");
            File.WriteAllBytes(broken, bytes);
            var store = RexStore.InMemory();
            new PlayerMemory(store).SetProposals(fileA,
                new SectionProposals(SectionAnalysis.SignatureOf(fileA), []));
            var analyzed = 0;
            using (var harness = new ControllerHarness(autoPlay: false, pictures: true, store: store))
            {
                harness.Files[fileA] = bytes;
                harness.Files[fileB] = bytes;
                var controller = harness.Controller;
                controller.SectionAnalyzer = (path, token) =>
                {
                    token.ThrowIfCancellationRequested();
                    if (path == broken)
                    {
                        throw new IOException("Unreadable sibling");
                    }

                    Interlocked.Increment(ref analyzed);
                    var first = path == fileA;
                    var opening = Enumerable.Range(0, 220)
                        .Select(i => new AudioLandmark((uint)(1000 + i), (first ? 300 : 550) + (i * 12)))
                        .ToArray();
                    return new EpisodeAnalysis(path, SectionAnalysis.SignatureOf(path),
                        TimeSpan.FromMinutes(43), opening, [], []);
                };
                controller.Open([fileA]);
                harness.PumpUntil(x => x.State == SessionState.Ready && x.Info is not null);
                harness.PumpUntil(x => x.CurrentProposals is { Candidates.Count: > 0 });
                Assert.Equal(2, analyzed);
                Assert.NotNull(new PlayerMemory(store).Analysis(fileB, SectionAnalysis.SignatureOf(fileB)));
                Assert.NotNull(controller.CurrentProposals!.Best(EpisodeSectionKind.Intro, TimeSpan.FromMinutes(43)));
            }

            using var again = new ControllerHarness(autoPlay: false, pictures: true, store: store);
            again.Files[fileA] = bytes;
            again.Controller.SectionAnalyzer = (_, _) => throw new InvalidOperationException(
                "A signature-matched proposal must load without decoding");
            again.Controller.Open([fileA]);
            again.PumpUntil(x => x.State == SessionState.Ready && x.CurrentProposals is not null);
            Assert.Equal(2, analyzed);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }


    [Fact]
    public void NamedChaptersMayBeSavedOnceWithoutOverwritingManualAdjustments()
    {
        static byte[] At(long nanoseconds, string title) => EbmlWriter.Element(
            MatroskaId.ChapterAtom,
            EbmlWriter.UInt(MatroskaId.ChapterTimeStart, (ulong)nanoseconds),
            EbmlWriter.Element(MatroskaId.ChapterDisplay,
                EbmlWriter.Text(MatroskaId.ChapString, title)));

        var video = EbmlWriter.Element(MatroskaId.TrackEntry,
            EbmlWriter.UInt(MatroskaId.TrackNumber, 2),
            EbmlWriter.UInt(MatroskaId.TrackType, 1),
            EbmlWriter.Text(MatroskaId.CodecId, "V_MPEG4/ISO/AVC"),
            EbmlWriter.Element(MatroskaId.Video,
                EbmlWriter.UInt(MatroskaId.PixelWidth, 16),
                EbmlWriter.UInt(MatroskaId.PixelHeight, 8)));
        var chapters = EbmlWriter.Element(MatroskaId.Chapters,
            EbmlWriter.Element(MatroskaId.EditionEntry,
                At(0, "Opening"),
                At(3_000_000_000, "Act one"),
                At(6_000_000_000, "End credits")));
        var info = EbmlWriter.Element(MatroskaId.Info,
            EbmlWriter.UInt(MatroskaId.TimestampScale, 1_000_000),
            EbmlWriter.Float(MatroskaId.Duration, 10_000));
        var bytes = MatroskaCraftedTests.Mkv(info, MatroskaCraftedTests.Tracks(video),
            chapters, MatroskaCraftedTests.Cluster(0, MatroskaCraftedTests.Simple(2, 0, true, 8)));
        using var test = new ControllerHarness(autoPlay: false, pictures: true);
        test.Files["chapter-video.mkv"] = bytes;
        var player = test.Controller;
        player.Open(["chapter-video.mkv"]);
        test.PumpUntil(p => p.State == SessionState.Ready && p.Info is not null);

        Assert.Equal(new EpisodeSections(TimeSpan.Zero, TimeSpan.FromSeconds(3),
            TimeSpan.FromSeconds(6), TimeSpan.FromSeconds(10)), player.ChapterSectionSuggestions);
        Assert.True(player.ApplyChapterSectionSuggestions());
        Assert.False(player.ApplyChapterSectionSuggestions());
        Assert.Equal(TimeSpan.FromSeconds(3), player.CurrentSections.IntroEnd);
        Assert.True(player.SetSectionBounds(EpisodeSectionKind.Credits,
            TimeSpan.FromSeconds(6), TimeSpan.FromSeconds(9.25)));
        Assert.Equal(TimeSpan.FromSeconds(9.25), player.CurrentSections.CreditsEnd);
        Assert.False(player.ApplyChapterSectionSuggestions());
        Assert.Throws<ArgumentOutOfRangeException>(() => player.SetSectionBounds(
            (EpisodeSectionKind)77, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(4)));
        Assert.False(player.RejectDetectedSection(EpisodeSectionKind.Intro));
    }

    [Fact]
    public void AutomaticSkippingCanBeOptedInSeparatelyFromDetection()
    {
        var video = EbmlWriter.Element(MatroskaId.TrackEntry,
            EbmlWriter.UInt(MatroskaId.TrackNumber, 2),
            EbmlWriter.UInt(MatroskaId.TrackType, 1),
            EbmlWriter.Text(MatroskaId.CodecId, "V_MPEG4/ISO/AVC"),
            EbmlWriter.Element(MatroskaId.Video,
                EbmlWriter.UInt(MatroskaId.PixelWidth, 16),
                EbmlWriter.UInt(MatroskaId.PixelHeight, 8)));
        var info = EbmlWriter.Element(MatroskaId.Info,
            EbmlWriter.UInt(MatroskaId.TimestampScale, 1_000_000),
            EbmlWriter.Float(MatroskaId.Duration, 10_000));
        var bytes = MatroskaCraftedTests.Mkv(info, MatroskaCraftedTests.Tracks(video),
            MatroskaCraftedTests.Cluster(0,
                [.. Enumerable.Range(0, 50).Select(i =>
                    MatroskaCraftedTests.Simple(2, (short)(i * 100), true, (byte)i))]));
        var store = RexStore.InMemory();
        var saved = new PlayerMemory(store);
        saved.SetSections("scene.mkv", new EpisodeSections(
            TimeSpan.Zero, TimeSpan.FromSeconds(3),
            TimeSpan.FromSeconds(6), TimeSpan.FromSeconds(9)));
        using var test = new ControllerHarness(autoPlay: false,
            settings: new PlayerSettings { SkipIntrosAutomatically = true },
            pictures: true, store: store);
        test.Files["scene.mkv"] = bytes;
        var player = test.Controller;
        player.Open(["scene.mkv"]);
        test.PumpUntil(p => p.State == SessionState.Ready);
        player.Seek(TimeSpan.FromSeconds(1));
        // Playback position notifications are intentionally not produced by a paused
        // session. Drive the position policy directly, without racing the renderer.
        var maybeSkip = typeof(PlayerController).GetMethod("MaybeSkipAutomatically",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        maybeSkip.Invoke(player, null);
        Assert.Equal(TimeSpan.FromSeconds(3), player.Position);
        Assert.False(player.Settings.SkipCreditsAutomatically);
        player.Seek(TimeSpan.FromSeconds(7));
        maybeSkip.Invoke(player, null);
        Assert.Equal(TimeSpan.FromSeconds(7), player.Position);
    }


    private static byte[] TestVideo()
    {
        var video = EbmlWriter.Element(MatroskaId.TrackEntry,
            EbmlWriter.UInt(MatroskaId.TrackNumber, 2),
            EbmlWriter.UInt(MatroskaId.TrackType, 1),
            EbmlWriter.Text(MatroskaId.CodecId, "V_MPEG4/ISO/AVC"),
            EbmlWriter.Element(MatroskaId.Video,
                EbmlWriter.UInt(MatroskaId.PixelWidth, 16),
                EbmlWriter.UInt(MatroskaId.PixelHeight, 8)));
        var info = EbmlWriter.Element(MatroskaId.Info,
            EbmlWriter.UInt(MatroskaId.TimestampScale, 1_000_000),
            EbmlWriter.Float(MatroskaId.Duration, 10_000));
        return MatroskaCraftedTests.Mkv(info, MatroskaCraftedTests.Tracks(video),
            MatroskaCraftedTests.Cluster(0, MatroskaCraftedTests.Simple(2, 0, true, 20)));
    }

    [Fact]
    public void AFailingBackgroundAnalyzerLogsAndLeavesPlaybackAvailable()
    {
        var folder = Directory.CreateTempSubdirectory("rexplayer-sections-error-").FullName;
        try
        {
            var path = Path.Combine(folder, "Series.S01E01.mkv");
            var bytes = TestVideo();
            File.WriteAllBytes(path, bytes);
            var log = Rex.Media.Diagnostics.RexLog.InMemory();
            using var reported = new ManualResetEventSlim();
            log.EntryWritten += (_, entry) =>
            {
                if (entry.Source == "sections" && entry.Message.Contains(
                    "Background analysis unavailable", StringComparison.Ordinal))
                {
                    reported.Set();
                }
            };
            using var test = new ControllerHarness(autoPlay: false, pictures: true, log: log);
            test.Files[path] = bytes;
            test.Controller.SectionAnalyzer = (_, _) => throw new IOException("Decoder unavailable");
            test.Controller.Open([path]);
            test.PumpUntil(x => x.State == SessionState.Ready);
            Assert.True(reported.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
            Assert.Null(test.Controller.CurrentProposals);
            Assert.Equal(SessionState.Ready, test.Controller.State);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void SwitchingEpisodesCancelsAnalysisWithoutPublishingStaleResults()
    {
        var folder = Directory.CreateTempSubdirectory("rexplayer-sections-cancel-").FullName;
        try
        {
            var first = Path.Combine(folder, "Series.S01E01.mkv");
            var second = Path.Combine(folder, "Series.S01E02.mkv");
            var bytes = TestVideo();
            File.WriteAllBytes(first, bytes);
            File.WriteAllBytes(second, bytes);
            using var started = new ManualResetEventSlim();
            using var canceled = new ManualResetEventSlim();
            using var test = new ControllerHarness(autoPlay: false, pictures: true);
            test.Files[first] = bytes;
            test.Files[second] = bytes;
            test.Controller.SectionAnalyzer = (path, token) =>
            {
                if (path == first)
                {
                    started.Set();
                    token.WaitHandle.WaitOne(TimeSpan.FromSeconds(5));
                    try
                    {
                        token.ThrowIfCancellationRequested();
                    }
                    finally
                    {
                        canceled.Set();
                    }
                }

                return new EpisodeAnalysis(path, SectionAnalysis.SignatureOf(path),
                    TimeSpan.FromMinutes(43), [], [], []);
            };
            test.Controller.Open([first]);
            test.PumpUntil(x => x.State == SessionState.Ready);
            Assert.True(started.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
            test.Controller.Open([second]);
            test.PumpUntil(x => x.State == SessionState.Ready && x.Item?.Location == second);
            Assert.True(canceled.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
            test.PumpUntil(x => x.CurrentProposals is not null);
            Assert.Equal(second, test.Controller.Item?.Location);
            Assert.Empty(test.Controller.CurrentProposals!.Candidates);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void ADecoderReturningTheWrongEpisodeIdentityCannotPublishSuggestions()
    {
        var folder = Directory.CreateTempSubdirectory("rexplayer-sections-identity-").FullName;
        try
        {
            var path = Path.Combine(folder, "Series.S01E01.mkv");
            var bytes = TestVideo();
            File.WriteAllBytes(path, bytes);
            using var invoked = new ManualResetEventSlim();
            using var test = new ControllerHarness(autoPlay: false, pictures: true);
            test.Files[path] = bytes;
            test.Controller.SectionAnalyzer = (_, _) =>
            {
                invoked.Set();
                return new EpisodeAnalysis("other.mkv", SectionAnalysis.SignatureOf(path),
                    TimeSpan.FromMinutes(43), [], [], []);
            };
            test.Controller.Open([path]);
            test.PumpUntil(x => x.State == SessionState.Ready);
            Assert.True(invoked.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
            Assert.False(SpinWait.SpinUntil(() => test.Controller.Memory.Proposals(
                path, SectionAnalysis.SignatureOf(path)) is not null,
                TimeSpan.FromMilliseconds(50)));
            Assert.Null(test.Controller.CurrentProposals);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void StrongDetectionShowsSkipBeforeConfirmationAndRejectionHidesOnlyTheSuggestion()
    {
        var folder = Directory.CreateTempSubdirectory("rexplayer-section-offer-").FullName;
        try
        {
            var path = Path.Combine(folder, "Utopia.S01E02.mkv");
            var video = EbmlWriter.Element(MatroskaId.TrackEntry,
                EbmlWriter.UInt(MatroskaId.TrackNumber, 2),
                EbmlWriter.UInt(MatroskaId.TrackType, 1),
                EbmlWriter.Text(MatroskaId.CodecId, "V_MPEG4/ISO/AVC"),
                EbmlWriter.Element(MatroskaId.Video,
                    EbmlWriter.UInt(MatroskaId.PixelWidth, 16),
                    EbmlWriter.UInt(MatroskaId.PixelHeight, 8)));
            var info = EbmlWriter.Element(MatroskaId.Info,
                EbmlWriter.UInt(MatroskaId.TimestampScale, 1_000_000),
                EbmlWriter.Float(MatroskaId.Duration, 10_000));
            var videoBytes = MatroskaCraftedTests.Mkv(
                info, MatroskaCraftedTests.Tracks(video),
                MatroskaCraftedTests.Cluster(0,
                    [.. Enumerable.Range(0, 25).Select(i => MatroskaCraftedTests.Simple(2, (short)(i * 200), true, (byte)i))]));
            File.WriteAllBytes(path, videoBytes);

            var store = RexStore.InMemory();
            var memory = new PlayerMemory(store);
            var signature = SectionAnalysis.SignatureOf(path);
            var proposal = new SectionProposals(signature, [
                new SectionCandidate(EpisodeSectionKind.Intro, TimeSpan.Zero,
                    TimeSpan.FromSeconds(4), 0.96, "A repeated opening", "shared audio"),
            ]);
            memory.SetProposals(path, proposal);
            using var harness = new ControllerHarness(autoPlay: false, settings: new PlayerSettings(),
                store: store, pictures: true);
            harness.Files[path] = videoBytes;
            var player = harness.Controller;
            player.SectionAnalyzer = (_, _) => throw new InvalidOperationException("Cached proposals must not re-decode");
            player.Open([path]);
            harness.PumpUntil(c => c.State == SessionState.Ready && c.Info is not null && c.CurrentProposals is not null);

            Assert.True(player.CanMarkEpisodeSections);
            Assert.Equal(proposal.Signature, player.CurrentProposals?.Signature);
            Assert.Equal(new EpisodeSkip(EpisodeSectionKind.Intro, TimeSpan.FromSeconds(4)), player.AvailableEpisodeSkip);
            Assert.True(player.SkipEpisodeSection());
            Assert.Equal(TimeSpan.FromSeconds(4), player.Position);
            Assert.Equal(new EpisodeSections(), player.CurrentSections);
            Assert.True(player.RejectDetectedSection(EpisodeSectionKind.Intro));
            player.Seek(TimeSpan.FromSeconds(2));
            Assert.Null(player.AvailableEpisodeSkip);
            Assert.True(memory.Proposals(path, signature)!.IntroRejected);
            Assert.True(player.RejectDetectedSection(EpisodeSectionKind.Intro, false));
            Assert.NotNull(player.AvailableEpisodeSkip);

            Assert.True(player.ConfirmDetectedSection(EpisodeSectionKind.Intro));
            Assert.Equal(TimeSpan.FromSeconds(4), player.CurrentSections.IntroEnd);
            Assert.False(player.SetSectionBounds(EpisodeSectionKind.Intro,
                TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(2)));
            Assert.True(player.SetSectionBounds(EpisodeSectionKind.Intro,
                TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(3.25)));
            Assert.Equal(new EpisodeSkip(EpisodeSectionKind.Intro, TimeSpan.FromSeconds(3.25)), player.AvailableEpisodeSkip);
            Assert.True(player.RejectDetectedSection(EpisodeSectionKind.Intro));
            Assert.NotNull(player.AvailableEpisodeSkip);
            Assert.True(player.ClearEpisodeSection(EpisodeSectionKind.Intro));
            Assert.Null(player.AvailableEpisodeSkip);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
