using Rex.Media.AppCore.Player;
using Rex.Media.Library;
using Rex.Media.Primitives;

namespace Rex.Media.Tests.AppCore;

/// <summary>Deterministic fingerprints and proposals independent of the renderer or user's media library.</summary>
public sealed class SectionDetectionAlgorithmsTests
{
    private static readonly TimeSpan EpisodeLength = TimeSpan.FromMinutes(43);

    private static IReadOnlyList<AudioLandmark> Hits(int start, int count = 220, int offset = 0) =>
        Enumerable.Range(0, count).Select(n => new AudioLandmark((uint)(1000 + n), start + (n * 12) + offset)).ToArray();

    private static EpisodeAnalysis Episode(string path, int openOffset = 0, int closeOffset = 0,
        IReadOnlyList<CreditFrameSample>? frames = null) =>
        new(path, "12345:678", EpisodeLength, Hits(300 + openOffset), Hits(75000 + closeOffset), frames ?? []);

    private static CreditFrameSample Frame(int second, double dark = 0.90, double bright = 0.07) =>
        new(TimeSpan.FromSeconds(second), 0.11, dark, bright, Enumerable.Repeat(0.1f, 64).ToArray());

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
    public void OffsetVotesRequireAContiguousRunAndRejectIsolatedNoise()
    {
        Assert.Null(LandmarkMatcher.Match([], []));
        Assert.Null(LandmarkMatcher.Match(Hits(10, 2), Hits(20, 2)));
        Assert.Null(LandmarkMatcher.Match(Hits(10, 12), Hits(20, 12).Select(x => x with { Hash = x.Hash + 9000 }).ToArray()));
        Assert.Null(LandmarkMatcher.Match(Hits(10, 7).Concat(Hits(2000, 7)).ToArray(), Hits(9000, 7).Concat(Hits(11000, 7)).ToArray()));
        var first = Hits(350);
        var second = Hits(350, offset: 900);
        var match = Assert.IsType<LandmarkMatch>(LandmarkMatcher.Match(first, second));
        Assert.Equal(first.Count, match.Anchors);
        Assert.Equal(TimeSpan.FromSeconds((350 + 900) * AudioLandmarks.SecondsPerFrame), match.SecondStart);
        Assert.Equal(TimeSpan.FromSeconds(350 * AudioLandmarks.SecondsPerFrame), match.FirstStart);
        Assert.True(match.Confidence > 0.8);

        var split = Hits(100, 30).Concat(Hits(2000, 90)).ToArray();
        var other = split.Select(hit => hit with { Frame = hit.Frame + 500 }).ToArray();
        Assert.Equal(90, LandmarkMatcher.Match(split, other)!.Anchors);

        var tooBrief = Enumerable.Range(0, 10).Select(i => new AudioLandmark((uint)i, i)).ToArray();
        Assert.Null(LandmarkMatcher.Match(tooBrief, tooBrief));
        var negative = Hits(50, 12);
        var secondNegative = negative.Select(hit => hit with { Frame = hit.Frame - 100 }).ToArray();
        Assert.Null(LandmarkMatcher.Match(negative, secondNegative));
        Assert.Throws<ArgumentNullException>(() => LandmarkMatcher.Match(null!, second));
        Assert.Throws<ArgumentNullException>(() => LandmarkMatcher.Match(first, null!));
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
            Assert.Equal(32, sample.Rows.Count);
            Assert.InRange(sample.MeanLuma, 0, 1);
            Assert.InRange(sample.DarkFraction, 0, 1);
            Assert.InRange(sample.BrightFraction, 0, 1);
        }

        using var gpu = VideoFrame.OnGraphicsCard(PixelFormat.Nv12, 16, 16, new MemoryStream());
        Assert.Throws<NotSupportedException>(() => CreditFrameEvidence.Measure(gpu, TimeSpan.Zero));
        Assert.Throws<ArgumentNullException>(() => CreditFrameEvidence.Measure(null!, TimeSpan.Zero));
    }

    [Fact]
    public void ScrollingAndEndCreditRunsDoNotIncludePostCreditScenes()
    {
        Assert.Equal(0, CreditFrameEvidence.Scroll(Frame(1), Frame(2) with { Rows = [0.1f, 0.2f] }));
        static CreditFrameSample Moving(int second, int shift)
        {
            var rows = new float[64];
            for (var y = 12; y < 48; y += 8)
            {
                rows[y + shift] = 1;
            }

            return Frame(second, dark: 0.65) with { Rows = rows };
        }

        Assert.True(CreditFrameEvidence.Scroll(Moving(2390, 0), Moving(2391, 4)) > 0);
        Assert.NotNull(CreditFrameEvidence.Find(
            Enumerable.Range(0, 10).Select(i => Moving(2390 + i, (i % 3) * 4)).ToArray(), EpisodeLength));
        Assert.Throws<ArgumentNullException>(() => CreditFrameEvidence.Scroll(null!, Frame(1)));
        Assert.Throws<ArgumentNullException>(() => CreditFrameEvidence.Scroll(Frame(1), null!));
        Assert.Null(CreditFrameEvidence.Find([], EpisodeLength));
        Assert.Null(CreditFrameEvidence.Find(Enumerable.Range(0, 6).Select(i => Frame(i)).ToArray(), EpisodeLength));
        Assert.Null(CreditFrameEvidence.Find(Enumerable.Range(0, 15).Select(i => Frame(i)).ToArray(),
            TimeSpan.FromMinutes(3)));
        Assert.Null(CreditFrameEvidence.Find(Enumerable.Range(0, 15).Select(i => Frame(2000 + i, dark: 0.2)).ToArray(),
            EpisodeLength));

        var credits = Enumerable.Range(0, 90).Select(i => Frame(2390 + i)).ToArray();
        var after = Enumerable.Range(0, 20).Select(i => Frame(2480 + i, dark: 0.10)).ToArray();
        var result = CreditFrameEvidence.Find([.. credits, .. after], EpisodeLength);
        Assert.NotNull(result);
        Assert.Equal(TimeSpan.FromSeconds(2390), result.Value.Start);
        Assert.Equal(TimeSpan.FromSeconds(2480), result.Value.End);
        Assert.True(result.Value.Confidence >= 0.6);
        Assert.Null(CreditFrameEvidence.Find(Enumerable.Range(0, 20).Select(i =>
            Frame(2400 + i, bright: 0)).ToArray(), EpisodeLength));
        Assert.Throws<ArgumentNullException>(() => CreditFrameEvidence.Find(null!, EpisodeLength));
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
        Assert.All(single.Candidates, candidate => Assert.Contains("2 episodes", candidate.Reason));
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
        Assert.Contains("3 episodes", intro.Reason);
        var credits = Assert.Single(detected.Candidates, x => x.Kind == EpisodeSectionKind.Credits);
        Assert.True(credits.End < EpisodeLength);
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
        memory.SetAnalysis(episode);
        Assert.Equal(episode.Signature, memory.Analysis(episode.Path, episode.Signature)!.Signature);
        Assert.Equal(episode.Opening, memory.Analysis(episode.Path, episode.Signature)!.Opening);
        Assert.Null(memory.Analysis(episode.Path, "new-signature"));
        Assert.Throws<ArgumentNullException>(() => memory.SetProposals(episode.Path, null!));
        Assert.Throws<ArgumentNullException>(() => memory.SetAnalysis(null!));
        memory.RejectProposal("no such file", EpisodeSectionKind.Credits, true);
        Assert.Equal(proposal.Candidates.Count, memory.Proposals(episode.Path, "signature-2")!.Candidates.Count);
    }
    [Fact]
    public void ASharedOffsetWithoutEightContinuousAnchorsIsNotAnEpisodeMatch()
    {
        var separated = Hits(100, 6).Concat(Hits(2000, 6)).ToArray();
        var shifted = separated.Select(hit => hit with { Frame = hit.Frame + 500 }).ToArray();
        Assert.Null(LandmarkMatcher.Match(separated, shifted));
    }

    [Fact]
    public void HighConfidenceVideoAloneOffersCreditsAndShortAudioAgreementIsDiscarded()
    {
        var frames = Enumerable.Range(0, 100).Select(i => Frame(2390 + i)).ToArray();
        var own = Episode("Series.S01E02.mkv", frames: frames) with { Opening = [], Closing = [] };
        var videoOnly = SectionDetector.Detect(own, []);
        var detected = Assert.Single(videoOnly.Candidates);
        Assert.Equal(EpisodeSectionKind.Credits, detected.Kind);
        Assert.Equal("video frames", detected.Source);
        Assert.True(detected.Confidence >= SectionProposals.OfferThreshold);

        // The offset histogram agrees, but a four-second match is not a credit run.
        var brief = Episode("Series.S01E02.mkv") with
        {
            Opening = [],
            Closing = Hits(79000, 12),
            Frames = [],
        };
        var sibling = Episode("Series.S01E03.mkv") with
        {
            Opening = [],
            Closing = Hits(79000, 12, 300),
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
