using Rex.Media.AppCore.Player;

namespace Rex.Media.Tests.AppCore;

/// <summary>
/// The ways real seasons differ from the textbook, each on synthetic landmarks and pictures: cold
/// opens, recaps, shortened or missing intros, gag credits, scenes after the credits, credits over
/// the final scene, very short episodes, double episodes and stray files in the season folder.
/// </summary>
public sealed class SectionDetectionEdgeCaseTests
{
    private static readonly TimeSpan Length = TimeSpan.FromMinutes(45);
    private static readonly int End = (int)Length.TotalSeconds;

    /// <summary>The frame index of a time, in 32 ms hops.</summary>
    private static int At(double seconds) => (int)Math.Round(seconds / AudioLandmarks.SecondsPerFrame);

    /// <summary>
    /// A stretch of sound: distinct landmarks every fourth hop (about 8 a second) whose hashes start
    /// at <paramref name="sound"/>, so the same sound in two episodes shares its hashes.
    /// </summary>
    private static AudioLandmark[] Sound(uint sound, double from, double seconds) =>
        [.. Enumerable.Range(0, (int)(seconds / (4 * AudioLandmarks.SecondsPerFrame)))
            .Select(n => new AudioLandmark(sound + (uint)n, At(from) + (n * 4)))];

    /// <summary>Audio no other episode has: a different hash range per episode and moment.</summary>
    private static AudioLandmark[] Unique(int episode, double from, double seconds) =>
        Sound((uint)(1_000_000 + episode * 100_000 + (int)from * 10), from, seconds);

    private const uint Theme = 10_000;
    private const uint EndTheme = 20_000;

    private static EpisodeAnalysis Episode(int number, AudioLandmark[] opening, AudioLandmark[]? closing = null,
        CreditFrameSample[]? frames = null, TimeSpan? duration = null, string show = "Show") =>
        new($"{show}.S01E{number:00}.mkv", "sig" + number, duration ?? Length, opening, closing ?? Unique(number, End - 300, 290), frames ?? []);

    private static CreditFrameSample Text(double second) =>
        new(TimeSpan.FromSeconds(second), 0.08, 0.93, 0.03, Enumerable.Repeat(0.05f, CreditFrameEvidence.Bands).ToArray(), 0.2);

    private static CreditFrameSample Picture(double second) =>
        new(TimeSpan.FromSeconds(second), 0.45, 0.2, 0.05, Enumerable.Repeat(0.45f, CreditFrameEvidence.Bands).ToArray(), 0.1);

    private static CreditFrameSample[] Pictures(double from, double to, Func<double, CreditFrameSample> kind) =>
        [.. Enumerable.Range(0, (int)(to - from)).Select(i => kind(from + i))];

    private static SectionCandidate? Intro(EpisodeAnalysis episode, params EpisodeAnalysis[] siblings) =>
        SectionDetector.Detect(episode, siblings).Candidates.SingleOrDefault(candidate => candidate.Kind == EpisodeSectionKind.Intro);

    private static SectionCandidate? Credits(EpisodeAnalysis episode, params EpisodeAnalysis[] siblings) =>
        SectionDetector.Detect(episode, siblings).Candidates.SingleOrDefault(candidate => candidate.Kind == EpisodeSectionKind.Credits);

    private static void Near(double expected, TimeSpan actual) => Assert.InRange(actual.TotalSeconds, expected - 0.6, expected + 0.6);

    [Fact]
    public void AColdOpenOfAnyLengthMovesTheIntroNotTheMatch()
    {
        // Each episode opens with its own scene of a different length, then the same 50 s titles.
        var episodes = new[] { 30, 95, 240, 410, 600 }.Select((open, index) =>
            Episode(index + 1, [.. Unique(index + 1, 0, open), .. Sound(Theme, open, 50), .. Unique(index + 1, open + 50, 120)])).ToArray();
        var intro = Assert.IsType<SectionCandidate>(Intro(episodes[3], [.. episodes.Where(e => e != episodes[3])]));
        Near(410, intro.Start);
        Near(460, intro.End);
        Assert.True(intro.Confidence >= SectionProposals.OfferThreshold);
        Assert.Contains("4 of 4", intro.Reason);
    }

    [Fact]
    public void ARecapMatchesOneEpisodeAndTheTitlesMatchThemAll()
    {
        // Episode 3 opens with 70 s of episode 2 ("previously"), then the titles. The recap is a
        // longer match than the titles, but only one sibling shares it.
        const uint Episode2Story = 30_000;
        var e1 = Episode(1, [.. Unique(1, 0, 60), .. Sound(Theme, 60, 40), .. Unique(1, 100, 200)]);
        var e2 = Episode(2, [.. Sound(Episode2Story, 0, 90), .. Sound(Theme, 90, 40), .. Unique(2, 130, 200)]);
        var e3 = Episode(3, [.. Sound(Episode2Story, 0, 70), .. Sound(Theme, 75, 40), .. Unique(3, 115, 200)]);
        var e4 = Episode(4, [.. Unique(4, 0, 20), .. Sound(Theme, 20, 40), .. Unique(4, 60, 200)]);
        var e5 = Episode(5, [.. Unique(5, 0, 120), .. Sound(Theme, 120, 40), .. Unique(5, 160, 200)]);
        var intro = Assert.IsType<SectionCandidate>(Intro(e3, e1, e2, e4, e5));
        Near(75, intro.Start);
        Near(115, intro.End);
    }

    [Fact]
    public void AShortenedIntroInOneEpisodeDoesNotShortenTheOthers()
    {
        // Episode 4 plays only the first 15 s of the 60 s titles; the others play all of it.
        var full = Enumerable.Range(1, 3).Select(n => Episode(n, [.. Unique(n, 0, 30 * n), .. Sound(Theme, 30 * n, 60), .. Unique(n, 30 * n + 60, 100)])).ToArray();
        var shortened = Episode(4, [.. Unique(4, 0, 50), .. Sound(Theme, 50, 15), .. Unique(4, 65, 100)]);
        var intro = Assert.IsType<SectionCandidate>(Intro(full[1], full[0], full[2], shortened));
        Near(60, intro.Start);
        Near(120, intro.End);
        var own = Assert.IsType<SectionCandidate>(Intro(shortened, full));
        Near(50, own.Start);
        Near(65, own.End);
    }

    [Fact]
    public void APremiereWithItsOwnTitlesAndAShowWithoutTitlesGetNoIntro()
    {
        const uint PilotTitles = 40_000;
        var pilot = Episode(1, [.. Unique(1, 0, 100), .. Sound(PilotTitles, 100, 45), .. Unique(1, 145, 100)]);
        var rest = Enumerable.Range(2, 4).Select(n => Episode(n, [.. Unique(n, 0, 20 * n), .. Sound(Theme, 20 * n, 45), .. Unique(n, 20 * n + 45, 100)])).ToArray();
        Assert.Null(Intro(pilot, rest));
        Assert.NotNull(Intro(rest[0], [pilot, .. rest[1..]]));

        var untitled = Enumerable.Range(1, 5).Select(n => Episode(n, Unique(n, 0, 600))).ToArray();
        Assert.Null(Intro(untitled[0], untitled[1..]));
    }

    [Fact]
    public void ALogoStingShorterThanEightSecondsIsNotAnIntro()
    {
        // A distributor's seven-second sting opens every episode: repeated, but not worth a button.
        const uint Sting = 50_000;
        var episodes = Enumerable.Range(1, 4).Select(n => Episode(n, [.. Sound(Sting, 0, 7), .. Unique(n, 8, 300)])).ToArray();
        Assert.NotNull(LandmarkMatcher.Match(episodes[0].Opening, episodes[1].Opening));
        Assert.Null(Intro(episodes[0], episodes[1..]));
    }

    [Fact]
    public void ADistributorLogoOfDifferentLengthsShiftsTheWholeEpisode()
    {
        // The same episode structure behind a 4 s logo in one file and a 12 s logo in another.
        var a = Episode(1, [.. Unique(1, 0, 4), .. Unique(11, 4, 60), .. Sound(Theme, 64, 50), .. Unique(1, 114, 100)]);
        var b = Episode(2, [.. Unique(2, 0, 12), .. Unique(12, 12, 75), .. Sound(Theme, 87, 50), .. Unique(2, 137, 100)]);
        var intro = Assert.IsType<SectionCandidate>(Intro(b, a));
        Near(87, intro.Start);
        Near(137, intro.End);
        // A single comparison cannot rule out a recap, so it is offered but never fully trusted.
        Assert.InRange(intro.Confidence, SectionProposals.OfferThreshold, 0.85);
    }

    [Fact]
    public void TitlesUnderDialogueStayOneIntroDespiteMaskedStretches()
    {
        // Speech hides the theme's landmarks for up to two and a half seconds at a time.
        static AudioLandmark[] Masked(double from) =>
            [.. Sound(Theme, from, 60).Where(hit => (hit.Frame - At(from)) % 156 >= 78)];
        var episodes = Enumerable.Range(1, 4).Select(n => Episode(n, [.. Unique(n, 0, 25 * n), .. Masked(25 * n), .. Unique(n, 25 * n + 60, 100)])).ToArray();
        var intro = Assert.IsType<SectionCandidate>(Intro(episodes[1], [episodes[0], .. episodes[2..]]));
        Assert.InRange(intro.Start.TotalSeconds, 50, 53);
        Assert.InRange(intro.End.TotalSeconds, 108, 110.6);
    }

    [Fact]
    public void ASharedClosingSegmentBeatsGagCreditsEarlierInTheEpisode()
    {
        // A credits-looking gag three minutes before the end in episode 2 only, and the season's
        // shared end theme playing over the final scene (no text on black at all).
        var gag = Pictures(End - 200, End - 170, Text);
        var scene = Pictures(End - 170, End, Picture);
        CreditFrameSample[] frames = [.. gag, .. scene];
        var episodes = Enumerable.Range(1, 4).Select(n => Episode(n, Unique(n, 0, 300),
            [.. Unique(n, End - 300, 240), .. Sound(EndTheme, End - 55, 50)], n == 2 ? frames : null)).ToArray();
        var credits = Assert.IsType<SectionCandidate>(Credits(episodes[1], [episodes[0], .. episodes[2..]]));
        Assert.Equal("shared audio", credits.Source);
        Near(End - 55, credits.Start);
        // Nothing but five seconds follows the theme: the credits run to the end of the file.
        Assert.Equal(Length, credits.End);
    }

    [Fact]
    public void ASceneAfterTheCreditsIsNeverSkippedAndTheThemeStartingEarlyDoesNotSkipTheLastScene()
    {
        // The end theme starts 15 s before the credit text, over the last scene; after the credits
        // comes a 40 s scene. Credits are where the text is.
        CreditFrameSample[] frames = [.. Pictures(End - 120, End - 95, Picture), .. Pictures(End - 95, End - 40, Text), .. Pictures(End - 40, End, Picture)];
        var episodes = Enumerable.Range(1, 3).Select(n => Episode(n, Unique(n, 0, 300),
            [.. Unique(n, End - 300, 180), .. Sound(EndTheme, End - 110, 70)], frames)).ToArray();
        var credits = Assert.IsType<SectionCandidate>(Credits(episodes[0], episodes[1..]));
        Assert.Equal("shared audio + frames", credits.Source);
        Near(End - 95, credits.Start);
        Near(End - 40, credits.End);

        var prompt = EpisodeSkipPrompt.For(new EpisodeSkip(EpisodeSectionKind.Credits, credits.End), credits, Length,
            credits.Start + TimeSpan.FromSeconds(5), hasNextEpisode: true, automaticAt: null);
        Assert.Equal(EpisodeSkipAction.SkipCredits, prompt.Primary);
        Assert.Equal(EpisodeSkipAction.NextEpisode, prompt.Secondary);
    }

    [Fact]
    public void APreviewOfTheNextEpisodeAfterTheCreditsIsKept()
    {
        // Text-only credits, then a 30 s "next time" preview: pictures alone find the credits and
        // stop where the preview starts.
        CreditFrameSample[] frames = [.. Pictures(End - 200, End - 80, Picture), .. Pictures(End - 80, End - 30, Text), .. Pictures(End - 30, End, Picture)];
        var credits = Assert.IsType<SectionCandidate>(Credits(Episode(1, Unique(1, 0, 300), frames: frames)));
        Assert.Equal("video frames", credits.Source);
        Near(End - 80, credits.Start);
        Near(End - 30, credits.End);
    }

    [Fact]
    public void CreditsOverTheFinalSceneFallBackToTheSharedAudioAndStopBeforeAScene()
    {
        // No text on black: the shared end theme alone, followed by a 60 s scene that stays.
        var episodes = Enumerable.Range(1, 3).Select(n => Episode(n, Unique(n, 0, 300),
            [.. Unique(n, End - 300, 180), .. Sound(EndTheme, End - 110, 45), .. Unique(n + 10, End - 65, 60)],
            Pictures(End - 120, End, Picture))).ToArray();
        var credits = Assert.IsType<SectionCandidate>(Credits(episodes[2], episodes[..2]));
        Assert.Equal("shared audio", credits.Source);
        Near(End - 110, credits.Start);
        Near(End - 65, credits.End);
    }

    [Fact]
    public void VeryShortEpisodesAreLeftAlone()
    {
        var brief = TimeSpan.FromMinutes(5);
        var episodes = Enumerable.Range(1, 3).Select(n => Episode(n, [.. Unique(n, 0, 10), .. Sound(Theme, 10, 30)], [],
            Pictures(240, 300, Text), brief)).ToArray();
        Assert.Empty(SectionDetector.Detect(episodes[0], episodes[1..]).Candidates);
        Assert.Empty(CreditFrameEvidence.Runs(episodes[0].Frames, brief));
    }

    [Fact]
    public void ADoubleEpisodeIsASiblingAndACopyOfTheSameEpisodeIsNot()
    {
        var two = Episode(1, [.. Unique(1, 0, 40), .. Sound(Theme, 40, 50), .. Unique(1, 90, 100)]) with { Path = "Show.S01E01E02.mkv" };
        var three = Episode(3, [.. Unique(3, 0, 70), .. Sound(Theme, 70, 50), .. Unique(3, 120, 100)]);
        Assert.NotNull(Intro(three, two));

        // A second copy of episode 3 matches from start to end; it must not make a whole-file intro.
        var copy = three with { Path = "Show.S01E03.720p.mkv" };
        Assert.Null(Intro(three, copy));
    }

    [Fact]
    public void SpecialsExtrasAndOtherShowsInTheFolderAreNeverCompared()
    {
        var episode = Episode(2, [.. Unique(2, 0, 40), .. Sound(Theme, 40, 50), .. Unique(2, 90, 100)]);
        EpisodeAnalysis Stray(string path) => Episode(9, [.. Unique(9, 0, 10), .. Sound(Theme, 10, 50), .. Unique(9, 60, 100)]) with { Path = path };
        Assert.Null(Intro(episode, Stray("Show.S00E01.mkv"), Stray("Featurettes/Show.S01E05.mkv"),
            Stray("Show.S01E06.Behind.The.Scenes.mkv"), Stray("Other.S01E04.mkv"), Stray("Show.S02E01.mkv"), Stray("Show sample.mkv")));
        Assert.NotNull(Intro(episode, Stray("Show.S01E05.mkv")));
    }

    [Fact]
    public void WithFourOrMoreSiblingsOneAgreeingEpisodeIsNotEnough()
    {
        // Only episode 2 shares a stretch of score with episode 1; three others share nothing.
        const uint Score = 60_000;
        var e1 = Episode(1, [.. Unique(1, 0, 200), .. Sound(Score, 200, 40), .. Unique(1, 240, 100)]);
        var e2 = Episode(2, [.. Unique(2, 0, 300), .. Sound(Score, 300, 40), .. Unique(2, 340, 100)]);
        var others = Enumerable.Range(3, 3).Select(n => Episode(n, Unique(n, 0, 500))).ToArray();
        Assert.Null(Intro(e1, [e2, .. others]));
        Assert.NotNull(Intro(e1, e2));
    }
}
