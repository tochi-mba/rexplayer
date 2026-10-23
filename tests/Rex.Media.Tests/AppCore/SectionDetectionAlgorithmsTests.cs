using Rex.Media.AppCore.Player;
using Rex.Media.Library;
using Rex.Media.Primitives;

namespace Rex.Media.Tests.AppCore;

/// <summary>Deterministic fingerprints and proposals independent of the renderer or user's media library.</summary>
public sealed class SectionDetectionAlgorithmsTests
{
    private static readonly TimeSpan EpisodeLength = TimeSpan.FromMinutes(43);

    private static IReadOnlyList<AudioLandmark> Hits(int start, int count = 220, int offset = 0, uint hashes = 1000) =>
        Enumerable.Range(0, count).Select(n => new AudioLandmark(hashes + (uint)n, start + (n * 12) + offset)).ToArray();

    private static EpisodeAnalysis Episode(string path, int openOffset = 0, int closeOffset = 0,
        IReadOnlyList<CreditFrameSample>? frames = null) =>
        new(path, "12345:678", EpisodeLength, Hits(300 + openOffset), Hits(75000 + closeOffset, hashes: 50_000), frames ?? []);

    private static LandmarkMatch? Best(IReadOnlyList<AudioLandmark> first, IReadOnlyList<AudioLandmark> second) =>
        LandmarkMatcher.MatchAll(first, second, 1).SingleOrDefault();

    private static CreditRun? Strongest(IReadOnlyList<CreditFrameSample> frames, TimeSpan duration) =>
        CreditFrameEvidence.Runs(frames, duration).OrderByDescending(run => run.Confidence).FirstOrDefault();

    private static CreditFrameSample Frame(int second, double dark = 0.90, double bright = 0.07, double strokes = 0.2) =>
        new(TimeSpan.FromSeconds(second), 0.11, dark, bright, Enumerable.Repeat(0.1f, CreditFrameEvidence.Bands).ToArray(), strokes);

    [Fact]
    public void LandmarkFingerprintingIsSparseDeterministicAndGainTolerant()
    {
        Assert.Empty(AudioLandmarks.Fingerprint([]));
        Assert.Empty(AudioLandmarks.Fingerprint(new float[512]));
        Assert.Empty(AudioLandmarks.Fingerprint(new float[8192]));
        Assert.Throws<ArgumentOutOfRangeException>(() => AudioLandmarks.Fingerprint([], -1));

        var sound = new float[AudioLandmarks.Rate * 7];
        for (var i = 0; i < sound.Length; i++)
        {
            var seconds = i / (double)AudioLandmarks.Rate;
            sound[i] = (float)(0.3 * Math.Sin(2 * Math.PI * 380 * seconds) +
                0.2 * Math.Sin(2 * Math.PI * (720 + ((int)seconds * 31)) * seconds));
        }

        sound[3200] = float.NaN;
        var signatures = AudioLandmarks.Fingerprint(sound);
        Assert.NotEmpty(signatures);
        Assert.Equal(signatures, AudioLandmarks.Fingerprint(sound));
        var moved = AudioLandmarks.Fingerprint(sound, frameOffset: 1000);
        Assert.Equal(signatures.Count, moved.Count);
        Assert.All(moved.Zip(signatures), pair => Assert.Equal(1000, pair.First.Frame - pair.Second.Frame));
        Assert.All(moved.Zip(signatures), pair => Assert.Equal(pair.First.Hash, pair.Second.Hash));
    }

    [Fact]
    public void OffsetVotesRequireADenseContinuousRunAndRejectIsolatedNoise()
    {
        Assert.Null(Best([], []));
        Assert.Null(Best(Hits(10, 2), Hits(20, 2)));
        Assert.Null(Best(Hits(10, 30), Hits(20, 30).Select(x => x with { Hash = x.Hash + 9000 }).ToArray()));
        Assert.Null(Best(Hits(10, 7).Concat(Hits(2000, 7)).ToArray(), Hits(9000, 7).Concat(Hits(11000, 7)).ToArray()));
        var first = Hits(350);
        var second = Hits(350, offset: 900);
        var match = Assert.IsType<LandmarkMatch>(Best(first, second));
        Assert.Equal(first.Count, match.Anchors);
        Assert.Equal(TimeSpan.FromSeconds((350 + 900) * AudioLandmarks.SecondsPerFrame), match.SecondStart);
        Assert.Equal(TimeSpan.FromSeconds(350 * AudioLandmarks.SecondsPerFrame), match.FirstStart);
        Assert.True(match.Confidence > 0.8);

        // Two shared stretches at one offset, more than three seconds apart, are two runs.
        var split = Hits(100, 30).Concat(Hits(2000, 90, hashes: 5000)).ToArray();
        var other = split.Select(hit => hit with { Frame = hit.Frame + 500 }).ToArray();
        Assert.Equal(90, Best(split, other)!.Anchors);
        Assert.Equal(2, LandmarkMatcher.MatchAll(split, other).Count);
        Assert.Single(LandmarkMatcher.MatchAll(split, other, limit: 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => LandmarkMatcher.MatchAll(split, other, limit: 0));

        // Fewer than sixteen anchors, or anchors spread thinner than two a second, are chance.
        var tooFew = Enumerable.Range(0, 15).Select(i => new AudioLandmark((uint)i, i * 8)).ToArray();
        Assert.Null(Best(tooFew, tooFew));
        var sparse = Enumerable.Range(0, 40).Select(i => new AudioLandmark((uint)i, i * 90)).ToArray();
        Assert.Null(Best(sparse, sparse));
        var tooBrief = Enumerable.Range(0, 20).Select(i => new AudioLandmark((uint)i, i * 3)).ToArray();
        Assert.Null(Best(tooBrief, tooBrief));
        var negative = Hits(50, 40);
        var secondNegative = negative.Select(hit => hit with { Frame = hit.Frame - 100 }).ToArray();
        Assert.Null(Best(negative, secondNegative));
        Assert.Throws<ArgumentNullException>(() => Best(null!, second));
        Assert.Throws<ArgumentNullException>(() => Best(first, null!));
    }

    [Fact]
    public void AHashRepeatedAllThroughAWindowCannotVote()
    {
        // A hum puts the same pair of peaks in every hop of both episodes: every offset gets its
        // votes and none stands out, so it is dropped like a stop word.
        var hum = Enumerable.Range(0, 3000).Select(frame => new AudioLandmark(7, frame)).ToArray();
        Assert.Empty(LandmarkMatcher.MatchAll(hum, hum));
        var theme = Hits(400);
        var match = Assert.Single(LandmarkMatcher.MatchAll([.. hum, .. theme], [.. hum, .. Hits(400, offset: 250)]));
        Assert.Equal(220, match.Anchors);
    }

    [Fact]
    public void NeighbouringOffsetsShareTheirVotes()
    {
        // Half the landmarks land one 32 ms hop later in the other episode: still one match.
        var first = Hits(500);
        var second = first.Select((hit, index) => hit with { Frame = hit.Frame + 300 + (index % 2) }).ToArray();
        var match = Assert.IsType<LandmarkMatch>(Best(first, second));
        Assert.Equal(220, match.Anchors);
    }

    [Fact]
    public void EpisodeGroupingRecognizesRealEpisodeNamesNotExtras()
    {
        Assert.Equal(new EpisodeIdentity("UTOPIA", 1, 2), EpisodeGrouping.Identify("Utopia.S01E02.Title.mkv"));
        Assert.Equal(new EpisodeIdentity("THE REHEARSAL", 1, 3), EpisodeGrouping.Identify("The.Rehearsal.1x03.mp4"));
        Assert.Null(EpisodeGrouping.Identify("Extra footage.mkv"));
        Assert.Null(EpisodeGrouping.Identify("Utopia.S01E00.sample.mkv"));
        Assert.Null(EpisodeGrouping.Identify("Utopia.S01E02.sample.mkv"));
        Assert.Null(EpisodeGrouping.Identify("Utopia.S01E02.special.mkv"));
        Assert.Null(EpisodeGrouping.Identify("Utopia.S01E02.trailer.mkv"));
        Assert.Null(EpisodeGrouping.Identify("Utopia.S01E02.extra.mkv"));
        Assert.Null(EpisodeGrouping.Identify("A.S01E02.mkv"));
        Assert.Equal(new EpisodeIdentity("UTOPIA", 1, 4), EpisodeGrouping.Identify("/media/Utopia/Season 1/S01E04.mkv"));
        Assert.Equal(new EpisodeIdentity("MEDIA", 1, 4), EpisodeGrouping.Identify("/media/Season 1/S01E04.mkv"));
        Assert.Throws<ArgumentException>(() => EpisodeGrouping.Identify(""));

        var files = new[] { "Utopia.S01E02.mkv", "Utopia.S01E03.mkv", "Utopia.S01E03.mkv",
            "Utopia.S02E04.mkv", "Other.S01E04.mkv", "Utopia.S01E05.trailer.mkv" };
        Assert.Equal(["Utopia.S01E03.mkv"], EpisodeGrouping.Siblings("Utopia.S01E02.mkv", files));
        Assert.Empty(EpisodeGrouping.Siblings("film.mkv", files));
        Assert.Throws<ArgumentNullException>(() => EpisodeGrouping.Siblings("Utopia.S01E02.mkv", null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => EpisodeGrouping.Siblings("Utopia.S01E02.mkv", files, -1));

        // The nearest episodes first, one file for each, as many as asked for.
        var season = new[] { "Show.S01E01.mkv", "Show.S01E09.mkv", "Show.S01E04.mkv", "Show.S01E06.mkv", "Show.S01E04.720p.mkv", "Show.S01E05.mkv" };
        Assert.Equal(["Show.S01E04.720p.mkv", "Show.S01E06.mkv", "Show.S01E01.mkv"], EpisodeGrouping.Siblings("Show.S01E05.mkv", season, 3));
        Assert.Empty(EpisodeGrouping.Siblings("Show.S01E05.mkv", season, 0));
    }

    [Fact]
    public void SpecialsExtrasAndDoubleEpisodesAreToldApart()
    {
        // Season 0 holds specials; extras are known by their name or their folder.
        Assert.Null(EpisodeGrouping.Identify("Show.S00E01.mkv"));
        Assert.Null(EpisodeGrouping.Identify("/tv/Show/Featurettes/Show.S01E01.mkv"));
        Assert.Null(EpisodeGrouping.Identify("/tv/Show/Extras/Show.S01E01.mkv"));
        Assert.Null(EpisodeGrouping.Identify("Show.S01E01.Behind.The.Scenes.mkv"));
        Assert.Null(EpisodeGrouping.Identify("Show.S01E01.Deleted.Scenes.mkv"));
        Assert.Null(EpisodeGrouping.Identify("Show.S01E01.Making.Of.mkv"));

        var twoParter = EpisodeGrouping.Identify("Show.S01E01E02.mkv")!;
        Assert.Equal(1, twoParter.Episode);
        Assert.Equal(2, twoParter.Last);
        Assert.Equal(2, EpisodeGrouping.Identify("Show.S01E01-E02.mkv")!.Through);
        Assert.Equal(2, EpisodeGrouping.Identify("Show.S01E01-02.mkv")!.Through);
        Assert.Equal(4, EpisodeGrouping.Identify("Show.1x03-x04.mkv")!.Through);
        Assert.Null(EpisodeGrouping.Identify("Show.S01E03-02.mkv")!.Through);
        Assert.Equal(3, EpisodeGrouping.Identify("Show.S01E03.mkv")!.Last);
    }

    [Fact]
    public void MultiEpisodeFilesDoNotClaimTheSameEpisodeAsSeparateSiblings()
    {
        var doubleEpisode = EpisodeGrouping.Identify("Show.S01E01E02.mkv")!;
        var second = EpisodeGrouping.Identify("Show.S01E02.mkv")!;
        var third = EpisodeGrouping.Identify("Show.S01E03.mkv")!;

        Assert.False(EpisodeGrouping.AreSiblings(doubleEpisode, second));
        Assert.False(EpisodeGrouping.AreSiblings(second, doubleEpisode));
        Assert.True(EpisodeGrouping.AreSiblings(doubleEpisode, third));
        Assert.Equal(["Show.S01E03.mkv"], EpisodeGrouping.Siblings(
            "Show.S01E01E02.mkv",
            ["Show.S01E01.mkv", "Show.S01E02.mkv", "Show.S01E03.mkv", "Show.S00E01.mkv"]));
    }

    [Fact]
    public void TheNextEpisodeFollowsTheLastNumberInTheSameSeason()
    {
        var folder = new[] { "Show.S01E01E02.mkv", "Show.S01E03.mkv", "Show.S01E05.mkv", "Show.S01E04.mkv",
            "Show.S02E01.mkv", "Other.S01E03.mkv", "Show.S01E03.Featurette.mkv", "Show.S00E04.mkv" };
        Assert.Equal("Show.S01E03.mkv", EpisodeGrouping.Next("Show.S01E01E02.mkv", folder));
        Assert.Equal("Show.S01E04.mkv", EpisodeGrouping.Next("Show.S01E03.mkv", folder));
        // The nearest later season wins, even when the library has no intervening season.
        Assert.Equal("Show.S02E01.mkv", EpisodeGrouping.Next("Show.S01E05.mkv", folder));
        Assert.Null(EpisodeGrouping.Next("Show.S02E01.mkv", folder));
        Assert.Equal("Show.S03E01.mkv", EpisodeGrouping.Next("Show.S01E05.mkv", ["Show.S03E01.mkv", "Show.S01E04.mkv"]));
        Assert.Equal("Show.S03E02.mkv", EpisodeGrouping.Next("Show.S01E05.mkv",
            ["Show.S05E01.mkv", "Show.S03E04.mkv", "Other.S02E01.mkv", "Show.S00E03.mkv", "Show.S03E02.mkv"]));
        Assert.Null(EpisodeGrouping.Next("Show.S03E02.mkv", ["Show.S01E03.mkv", "Show.S00E01.mkv", "Other.S04E01.mkv"]));
        Assert.Null(EpisodeGrouping.Next("film.mkv", folder));
        Assert.True(EpisodeGrouping.IsSeasonFolder("Season 2"));
        Assert.True(EpisodeGrouping.IsSeasonFolder("S02"));
        Assert.False(EpisodeGrouping.IsSeasonFolder("Featurettes"));
        Assert.False(EpisodeGrouping.IsSeasonFolder("Season 2 Extras"));
        Assert.False(EpisodeGrouping.IsSeasonFolder(""));
        Assert.Throws<ArgumentNullException>(() => EpisodeGrouping.Next("Show.S01E03.mkv", null!));
    }

    [Fact]
    public void CreditFramesAreMeasuredWithoutOpticalCharacterRecognition()
    {
        foreach (var format in new[] { PixelFormat.Gray8, PixelFormat.Bgra32, PixelFormat.P010, PixelFormat.I420P10 })
        {
            using var image = VideoFrame.Rent(format, 32, 32);
            for (var y = 0; y < 32; y++)
            {
                image.Row(0, y).Fill((byte)(y % 8 == 0 ? 240 : 20));
            }

            var sample = CreditFrameEvidence.Measure(image, TimeSpan.FromMinutes(42));
            Assert.Equal(TimeSpan.FromMinutes(42), sample.At);
            Assert.Equal(CreditFrameEvidence.Bands, sample.Rows.Count);
            Assert.InRange(sample.MeanLuma, 0, 1);
            Assert.InRange(sample.DarkFraction, 0, 1);
            Assert.InRange(sample.BrightFraction, 0, 1);
            Assert.InRange(sample.Strokes, 0, 1);
        }

        using var gpu = VideoFrame.OnGraphicsCard(PixelFormat.Nv12, 16, 16, new MemoryStream());
        Assert.Throws<NotSupportedException>(() => CreditFrameEvidence.Measure(gpu, TimeSpan.Zero));
        Assert.Throws<ArgumentNullException>(() => CreditFrameEvidence.Measure(null!, TimeSpan.Zero));
    }

    [Fact]
    public void ThinLettersScoreAsStrokesAndALampDoesNot()
    {
        using var letters = VideoFrame.Rent(PixelFormat.Gray8, 64, 64);
        using var lamp = VideoFrame.Rent(PixelFormat.Gray8, 64, 64);
        using var black = VideoFrame.Rent(PixelFormat.Gray8, 64, 64);
        for (var y = 0; y < 64; y++)
        {
            var text = letters.Row(0, y);
            var light = lamp.Row(0, y);
            black.Row(0, y).Clear();
            for (var x = 0; x < 64; x++)
            {
                // Two-pixel strokes across a line of text, against a band of light the width of the picture.
                text[x] = (byte)(y is >= 28 and < 36 && x is >= 8 and < 56 && x % 4 < 2 ? 230 : 10);
                light[x] = (byte)(y is >= 24 and < 36 ? 230 : 10);
            }
        }

        var credit = CreditFrameEvidence.Measure(letters, TimeSpan.Zero);
        var dark = CreditFrameEvidence.Measure(lamp, TimeSpan.Zero);
        Assert.True(credit.Strokes > 0.3);
        Assert.True(dark.Strokes < 0.1);
        Assert.True(CreditFrameEvidence.LooksLikeText(credit));
        Assert.False(CreditFrameEvidence.LooksLikeText(dark));
        var empty = CreditFrameEvidence.Measure(black, TimeSpan.Zero);
        Assert.Equal(0, empty.Strokes);
        Assert.True(CreditFrameEvidence.IsBlack(empty));
        Assert.False(CreditFrameEvidence.IsBlack(credit));
        Assert.Throws<ArgumentNullException>(() => CreditFrameEvidence.LooksLikeText(null!));
        Assert.Throws<ArgumentNullException>(() => CreditFrameEvidence.IsBlack(null!));
    }

    private static CreditFrameSample Moving(int second, int shift, double dark = 0.65)
    {
        var rows = new float[CreditFrameEvidence.Bands];
        for (var y = 20; y < 70; y += 10)
        {
            rows[y + shift] = 1;
        }

        return Frame(second, dark: dark) with { Rows = rows };
    }

    [Fact]
    public void ScrollingTextIsAVerticalShiftOfTheRowProfile()
    {
        Assert.Equal(0, CreditFrameEvidence.Scroll(Frame(1), Frame(2) with { Rows = [0.1f, 0.2f] }));
        Assert.Equal(0, CreditFrameEvidence.Scroll(Frame(1) with { Rows = [0.1f, 0.2f] }, Frame(2) with { Rows = [0.1f, 0.2f] }));
        Assert.True(CreditFrameEvidence.Scroll(Moving(2390, 0), Moving(2391, 4)) > 0);
        Assert.Equal(0, CreditFrameEvidence.Scroll(Moving(2390, 0), Moving(2391, 0)));
        Assert.Throws<ArgumentNullException>(() => CreditFrameEvidence.Scroll(null!, Frame(1)));
        Assert.Throws<ArgumentNullException>(() => CreditFrameEvidence.Scroll(Frame(1), null!));

        // Moderately dark text counts only while it scrolls between pictures a second apart.
        var rolling = Enumerable.Range(0, 30).Select(i => Moving(2390 + i, (i % 3) * 3)).ToArray();
        var run = Assert.IsType<CreditRun>(Strongest(rolling, EpisodeLength));
        Assert.Equal(TimeSpan.FromSeconds(2389.5), run.Start);
        var still = Enumerable.Range(0, 30).Select(i => Moving(2390 + i, 0)).ToArray();
        Assert.Null(Strongest(still, EpisodeLength));
    }

    [Fact]
    public void EndCreditRunsKeepAScenePlayingAfterThem()
    {
        Assert.Null(Strongest([], EpisodeLength));
        Assert.Null(Strongest(Enumerable.Range(0, 2).Select(i => Frame(2400 + i)).ToArray(), EpisodeLength));
        Assert.Empty(CreditFrameEvidence.Runs(Enumerable.Range(0, 15).Select(i => Frame(i)).ToArray(), TimeSpan.FromMinutes(5)));
        Assert.Null(Strongest(Enumerable.Range(0, 15).Select(i => Frame(2000 + i, dark: 0.2)).ToArray(), EpisodeLength));
        Assert.Throws<ArgumentNullException>(() => CreditFrameEvidence.Runs(null!, EpisodeLength));

        // Fifty seconds of scene after the credits: the credits end where the scene starts.
        var credits = Enumerable.Range(0, 90).Select(i => Frame(2440 + i)).ToArray();
        var after = Enumerable.Range(0, 40).Select(i => Frame(2530 + i, dark: 0.10)).ToArray();
        var result = Assert.IsType<CreditRun>(Strongest([.. credits, .. after], EpisodeLength));
        Assert.Equal(TimeSpan.FromSeconds(2439.5), result.Start);
        Assert.Equal(TimeSpan.FromSeconds(2529.5), result.End);
        Assert.False(result.ReachesEnd);
        Assert.True(result.Confidence >= SectionProposals.OfferThreshold);
        Assert.Null(Strongest(Enumerable.Range(0, 20).Select(i =>
            Frame(2400 + i, bright: 0)).ToArray(), EpisodeLength));
    }

    [Fact]
    public void ALogoOrCardShorterThanTheTailLimitBelongsToTheCredits()
    {
        var end = (int)EpisodeLength.TotalSeconds;
        var credits = Enumerable.Range(0, 40).Select(i => Frame(end - 50 + i)).ToArray();
        var logo = Enumerable.Range(0, 9).Select(i => Frame(end - 10 + i, dark: 0.0, bright: 0.2)).ToArray();
        var run = Assert.IsType<CreditRun>(Strongest([.. credits, .. logo], EpisodeLength));
        Assert.True(run.ReachesEnd);
        Assert.Equal(EpisodeLength, run.End);
    }

    [Fact]
    public void BlackBetweenCardsJoinsThemAndALongGapSplitsThem()
    {
        var end = (int)EpisodeLength.TotalSeconds;
        CreditFrameSample Black(int second) => Frame(second, dark: 1, bright: 0, strokes: 0);
        var cards = new[] { Frame(end - 60), Frame(end - 59), Black(end - 58), Black(end - 57), Frame(end - 56), Frame(end - 45), Frame(end - 35) };
        var run = Assert.Single(CreditFrameEvidence.Runs(cards, EpisodeLength));
        Assert.Equal(TimeSpan.FromSeconds(end - 60.5), run.Start);

        var apart = new[] { Frame(end - 200), Frame(end - 199), Frame(end - 192), Frame(end - 150), Frame(end - 149), Frame(end - 141) };
        Assert.Equal(2, CreditFrameEvidence.Runs(apart, EpisodeLength).Count);
    }

    [Fact]
    public void TextOnBlackFarFromTheEndIsTooUnsureToOffer()
    {
        // A gag-credits scene or an act card three minutes before the end is not the closing credits.
        var end = (int)EpisodeLength.TotalSeconds;
        var gag = Enumerable.Range(0, 40).Select(i => Frame(end - 200 + i)).ToArray();
        var scene = Enumerable.Range(0, 30).Select(i => Frame(end - 160 + (i * 5), dark: 0.2)).ToArray();
        var lastScene = scene.Where(frame => frame.At < TimeSpan.FromSeconds(end - 52)).ToArray();
        var run = Assert.Single(CreditFrameEvidence.Runs([.. gag, .. scene], EpisodeLength));
        Assert.True(run.Confidence < SectionProposals.OfferThreshold);
        Assert.Empty(SectionDetector.Detect(new EpisodeAnalysis("Show.S01E02.mkv", "s", EpisodeLength, [], [], [.. gag, .. scene]), []).Candidates);

        // With the real credits at the end as well, those win.
        var real = Enumerable.Range(0, 45).Select(i => Frame(end - 50 + i)).ToArray();
        var best = Assert.IsType<CreditRun>(Strongest([.. gag, .. lastScene, .. real], EpisodeLength));
        Assert.Equal(TimeSpan.FromSeconds(end - 50.5), best.Start);
    }

    [Fact]
    public void RefinementDecodesOnlyWhereTheEvidenceChanges()
    {
        var start = TimeSpan.FromSeconds(2220);
        var keys = new[] { Frame(2230, dark: 0.2), Frame(2240, dark: 0.2), Frame(2490, dark: 0.2), Frame(2500), Frame(2510), Frame(2520) };
        var windows = CreditFrameEvidence.RefinementWindows(keys, start, TimeSpan.FromSeconds(2580));
        Assert.Equal([(TimeSpan.FromSeconds(2490), TimeSpan.FromSeconds(2500)), (TimeSpan.FromSeconds(2558), TimeSpan.FromSeconds(2580))], windows);

        // Credits running to the last keyframe close to the end: the gap to the end is decoded.
        var late = new[] { Frame(2560, dark: 0.2), Frame(2575) };
        Assert.Equal([(TimeSpan.FromSeconds(2560), TimeSpan.FromSeconds(2575)), (TimeSpan.FromSeconds(2575), TimeSpan.FromSeconds(2580))],
            CreditFrameEvidence.RefinementWindows(late, start, TimeSpan.FromSeconds(2580)));
        Assert.Equal([(TimeSpan.FromSeconds(2500), TimeSpan.FromSeconds(2570)), (TimeSpan.FromSeconds(2570), TimeSpan.FromSeconds(2575))],
            CreditFrameEvidence.RefinementWindows([Frame(2500, dark: 0.2), Frame(2570), Frame(2575, dark: 0.2)], TimeSpan.FromSeconds(2500), TimeSpan.FromSeconds(2580)));

        // Text as the very first keyframe, and keyframes a second apart need no extra decoding.
        Assert.Equal([(start, TimeSpan.FromSeconds(2230))],
            CreditFrameEvidence.RefinementWindows([Frame(2230), Frame(2579)], start, TimeSpan.FromSeconds(2580)));
        Assert.Empty(CreditFrameEvidence.RefinementWindows([Frame(2570, dark: 0.2), Frame(2571)], TimeSpan.FromSeconds(2570), TimeSpan.FromSeconds(2571.5)));

        // Never more than three minutes: the windows nearest the end are kept.
        var flicker = Enumerable.Range(0, 30).Select(i => Frame(1000 + (i * 50), dark: i % 2 == 0 ? 0.2 : 0.9)).ToArray();
        var budgeted = CreditFrameEvidence.RefinementWindows(flicker, TimeSpan.FromSeconds(1000), TimeSpan.FromSeconds(2600));
        Assert.True(budgeted.Sum(window => (window.End - window.Start).TotalSeconds) <= 180);
        Assert.Equal(TimeSpan.FromSeconds(2600), budgeted[^1].End);
        Assert.Throws<ArgumentNullException>(() => CreditFrameEvidence.RefinementWindows(null!, start, EpisodeLength));
    }

    [Fact]
    public void DuplicateEpisodeCopiesDoNotCreateOrInflateIndependentEvidence()
    {
        var own = Episode("Series.S01E02.mkv");
        Assert.Empty(SectionDetector.Detect(own, [Episode("Series.S01E02.copy.mkv")]).Candidates);
        var sibling = Episode("Series.S01E03.mkv");
        var single = SectionDetector.Detect(own, [sibling]);
        var duplicates = SectionDetector.Detect(own, [sibling, sibling with { Path = "Series.S01E03.copy.mkv" }]);
        Assert.Equal(single.Candidates, duplicates.Candidates);
        Assert.All(single.Candidates, candidate => Assert.Equal("shared audio", candidate.Source));
        Assert.All(single.Candidates, candidate => Assert.Contains("1 of 1 other episode", candidate.Reason));
    }

    [Fact]
    public void MultipleEpisodeMatchesProvideHighConfidenceOneClickProposals()
    {
        var frames = Enumerable.Range(0, 100).Select(i => Frame(2390 + i)).ToArray();
        var episode = Episode("Utopia.S01E02.mkv", frames: frames);
        var siblingA = Episode("Utopia.S01E03.mkv", openOffset: 250, closeOffset: 300);
        var siblingB = Episode("Utopia.S01E04.mkv", openOffset: 700, closeOffset: 520);
        var detected = SectionDetector.Detect(episode, [siblingA, siblingB, Episode("Elsewhere.S01E02.mkv")]);
        Assert.Equal(episode.Signature, detected.Signature);
        var intro = Assert.Single(detected.Candidates, x => x.Kind == EpisodeSectionKind.Intro);
        Assert.Equal("shared audio", intro.Source);
        Assert.InRange(intro.Confidence, SectionProposals.OfferThreshold, 1);
        Assert.Contains("2 of 2 other episodes", intro.Reason);
        var credits = Assert.Single(detected.Candidates, x => x.Kind == EpisodeSectionKind.Credits);
        Assert.Equal("shared audio + frames", credits.Source);
        Assert.Equal(TimeSpan.FromSeconds(2489.5), credits.End);
        Assert.Equal(new EpisodeSkip(EpisodeSectionKind.Intro, intro.End),
            detected.Offer(new EpisodeSections(), intro.Start + TimeSpan.FromSeconds(10), EpisodeLength));
        Assert.Equal(new EpisodeSkip(EpisodeSectionKind.Credits, credits.End),
            detected.Offer(new EpisodeSections(), credits.Start + TimeSpan.FromSeconds(10), EpisodeLength));

        Assert.Empty(SectionDetector.Detect(episode with { Duration = TimeSpan.FromSeconds(10) }, [siblingA]).Candidates);
        Assert.Empty(SectionDetector.Detect(episode with { Path = "movie.mkv" }, [siblingA]).Candidates);
        Assert.Empty(SectionDetector.Detect(episode with { Opening = [], Closing = [], Frames = [] }, [siblingA]).Candidates);
        Assert.Throws<ArgumentNullException>(() => SectionDetector.Detect(null!, []));
        Assert.Throws<ArgumentNullException>(() => SectionDetector.Detect(episode, null!));
    }

    [Fact]
    public void ProposalCacheKeepsRejectionsAndInvalidatesChangedMedia()
    {
        var store = RexStore.InMemory();
        var memory = new PlayerMemory(store);
        var episode = Episode("Utopia.S01E02.mkv");
        var proposal = new SectionProposals("signature-1", [
            new SectionCandidate(EpisodeSectionKind.Intro, TimeSpan.FromSeconds(10),
                TimeSpan.FromSeconds(90), 0.93, "Recurring opener", "shared audio"),
        ]);
        Assert.Null(memory.Proposals(episode.Path, proposal.Signature));
        memory.SetProposals(episode.Path, proposal);
        Assert.Equal(proposal.Signature, memory.Proposals(episode.Path, proposal.Signature)!.Signature);
        Assert.Equal(proposal.Candidates[0], memory.Proposals(episode.Path, proposal.Signature)!.Candidates[0]);
        Assert.Null(memory.Proposals(episode.Path, "new file content"));
        memory.RejectProposal(episode.Path, EpisodeSectionKind.Intro, true);
        Assert.True(memory.Proposals(episode.Path, proposal.Signature)!.IntroRejected);
        memory.SetProposals(episode.Path, proposal with { Signature = "signature-2" });
        Assert.True(memory.Proposals(episode.Path, "signature-2")!.IntroRejected);
        memory.RejectProposal(episode.Path, EpisodeSectionKind.Intro, false);
        Assert.False(memory.Proposals(episode.Path, "signature-2")!.IntroRejected);
        Assert.Throws<ArgumentNullException>(() => memory.SetProposals(episode.Path, null!));
        Assert.Throws<ArgumentNullException>(() => memory.WithRejections(episode.Path, null!));

        // A rejection made before anything was kept (while the analysis still runs) is kept,
        // and applies to what is detected afterwards.
        memory.RejectProposal("Show.S01E09.mkv", EpisodeSectionKind.Credits, true);
        Assert.Null(memory.Proposals("Show.S01E09.mkv", "signature-9"));
        Assert.True(memory.WithRejections("Show.S01E09.mkv", proposal with { Signature = "signature-9" }).CreditsRejected);
        memory.SetProposals("Show.S01E09.mkv", proposal with { Signature = "signature-9" });
        Assert.True(memory.Proposals("Show.S01E09.mkv", "signature-9")!.CreditsRejected);
        Assert.Equal(proposal.Candidates.Count, memory.Proposals(episode.Path, "signature-2")!.Candidates.Count);
    }

    [Fact]
    public void ShowSkippingIsRememberedPerShowAndForgottenWhenOff()
    {
        var memory = new PlayerMemory(RexStore.InMemory());
        Assert.Equal(new ShowSkipping(false, false), memory.ShowSkips("UTOPIA"));
        memory.SetShowSkips("UTOPIA", new ShowSkipping(true, false));
        Assert.Equal(new ShowSkipping(true, false), memory.ShowSkips("UTOPIA"));
        Assert.Equal(new ShowSkipping(false, false), memory.ShowSkips("OTHER"));
        memory.SetShowSkips("UTOPIA", new ShowSkipping(false, false));
        Assert.Empty(memory.Store.WithPrefix("episode-show-skip/"));
        Assert.Throws<ArgumentNullException>(() => memory.SetShowSkips("UTOPIA", null!));
    }

    [Fact]
    public void HighConfidenceVideoAloneOffersCreditsAndShortAudioAgreementIsDiscarded()
    {
        var frames = Enumerable.Range(0, 100).Select(i => Frame(2470 + i)).ToArray();
        var own = Episode("Series.S01E02.mkv", frames: frames) with { Opening = [], Closing = [] };
        var videoOnly = SectionDetector.Detect(own, []);
        var detected = Assert.Single(videoOnly.Candidates);
        Assert.Equal(EpisodeSectionKind.Credits, detected.Kind);
        Assert.Equal("video frames", detected.Source);
        Assert.True(detected.Confidence >= SectionProposals.OfferThreshold);

        // The offset histogram agrees, but a six-second match is a sting, not a credit run.
        var brief = Episode("Series.S01E02.mkv") with
        {
            Opening = [],
            Closing = Hits(79000, 16),
            Frames = [],
        };
        var sibling = Episode("Series.S01E03.mkv") with
        {
            Opening = [],
            Closing = Hits(79000, 16, 300),
            Frames = [],
        };
        Assert.Empty(SectionDetector.Detect(brief, [sibling]).Candidates);
    }

    [Fact]
    public void RejectedCreditsAndUnknownKindsPreserveTheManualBoundaries()
    {
        var original = new SectionProposals("file", [
            new SectionCandidate(EpisodeSectionKind.Credits, TimeSpan.FromMinutes(40),
                TimeSpan.FromMinutes(41), 0.91, "Ending", "frames"),
        ]);
        var withCreditsRejected = original.Reject(EpisodeSectionKind.Credits);
        Assert.True(withCreditsRejected.CreditsRejected);
        Assert.Null(withCreditsRejected.Best(EpisodeSectionKind.Credits, EpisodeLength));
        Assert.False(withCreditsRejected.Reject(EpisodeSectionKind.Credits, false).CreditsRejected);
        Assert.Throws<ArgumentOutOfRangeException>(() => original.Reject((EpisodeSectionKind)42));
        Assert.Equal(["Utopia.S01E03.mkv"],
            EpisodeGrouping.Siblings("Utopia.S01E02.mkv",
                ["Utopia.S01E03.mkv", "UTOPIA.s01e03.mkv", "Utopia.S01E02.mkv"]));
    }
}
