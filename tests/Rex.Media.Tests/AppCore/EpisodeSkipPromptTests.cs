using Rex.Media.AppCore.Player;

namespace Rex.Media.Tests.AppCore;

/// <summary>What the skip button says and does in each situation, decided without a window.</summary>
public sealed class EpisodeSkipPromptTests
{
    private static readonly TimeSpan Length = TimeSpan.FromMinutes(44);

    private static SectionCandidate Detected(EpisodeSectionKind kind, TimeSpan end) =>
        new(kind, end - TimeSpan.FromSeconds(50), end, 0.91, "Repeats", "shared audio");

    [Fact]
    public void ButtonsSayExactlyWhatTheyDo()
    {
        Assert.Equal("Skip intro", EpisodeSkipPrompt.Label(EpisodeSkipAction.SkipIntro));
        Assert.Equal("Skip credits", EpisodeSkipPrompt.Label(EpisodeSkipAction.SkipCredits));
        Assert.Equal("Next episode", EpisodeSkipPrompt.Label(EpisodeSkipAction.NextEpisode));
        Assert.Throws<ArgumentOutOfRangeException>(() => EpisodeSkipPrompt.Label((EpisodeSkipAction)9));
        Assert.Throws<ArgumentNullException>(() => EpisodeSkipPrompt.For(null!, null, Length, TimeSpan.Zero, false, null));
    }

    [Fact]
    public void AnIntroIsSkippedToItsEndWhetherDetectedOrSaved()
    {
        var end = TimeSpan.FromSeconds(140);
        var detected = EpisodeSkipPrompt.For(new EpisodeSkip(EpisodeSectionKind.Intro, end),
            Detected(EpisodeSectionKind.Intro, end), Length, TimeSpan.FromSeconds(100), hasNextEpisode: true, automaticAt: null);
        Assert.Equal(EpisodeSkipAction.SkipIntro, detected.Primary);
        Assert.Null(detected.Secondary);
        Assert.Equal(end, detected.Target);
        Assert.True(detected.Detected);
        Assert.Equal(0.91, detected.Confidence);
        Assert.Equal("Skip intro", detected.PrimaryText);

        var saved = EpisodeSkipPrompt.For(new EpisodeSkip(EpisodeSectionKind.Intro, end), null, Length,
            TimeSpan.FromSeconds(100), hasNextEpisode: false, automaticAt: null);
        Assert.False(saved.Detected);
        Assert.Null(saved.Confidence);
    }

    [Fact]
    public void CreditsThatEndTheFileOfferTheNextEpisodeWhenThereIsOne()
    {
        var skip = new EpisodeSkip(EpisodeSectionKind.Credits, Length - TimeSpan.FromSeconds(4));
        var next = EpisodeSkipPrompt.For(skip, null, Length, Length - TimeSpan.FromSeconds(30), hasNextEpisode: true, automaticAt: null);
        Assert.Equal(EpisodeSkipAction.NextEpisode, next.Primary);
        Assert.Null(next.Secondary);
        Assert.Equal("Next episode", next.PrimaryText);

        var last = EpisodeSkipPrompt.For(skip, null, Length, Length - TimeSpan.FromSeconds(30), hasNextEpisode: false, automaticAt: null);
        Assert.Equal(EpisodeSkipAction.SkipCredits, last.Primary);
        Assert.Null(last.Secondary);
    }

    [Fact]
    public void CreditsBeforeASceneSkipToTheSceneAndStillOfferTheNextEpisode()
    {
        var skip = new EpisodeSkip(EpisodeSectionKind.Credits, Length - EpisodeSkipPrompt.SceneAfterCreditsThreshold);
        var prompt = EpisodeSkipPrompt.For(skip, null, Length, Length - TimeSpan.FromSeconds(60), hasNextEpisode: true, automaticAt: null);
        Assert.Equal(EpisodeSkipAction.SkipCredits, prompt.Primary);
        Assert.Equal(EpisodeSkipAction.NextEpisode, prompt.Secondary);
        Assert.Equal(skip.End, prompt.Target);
    }

    [Fact]
    public void AnAutomaticSkipCountsDownInWholeSecondsAndNeverShowsZero()
    {
        var skip = new EpisodeSkip(EpisodeSectionKind.Intro, TimeSpan.FromSeconds(140));
        var at = TimeSpan.FromSeconds(100);
        Assert.Equal(5, EpisodeSkipPrompt.For(skip, null, Length, at, false, at + AutomaticSkip.Delay).AutomaticInSeconds);
        var soon = EpisodeSkipPrompt.For(skip, null, Length, at + TimeSpan.FromSeconds(3.2), false, at + AutomaticSkip.Delay);
        Assert.Equal(2, soon.AutomaticInSeconds);
        Assert.Equal("Skipping intro in 2", soon.PrimaryText);
        Assert.Equal(1, EpisodeSkipPrompt.For(skip, null, Length, at + TimeSpan.FromSeconds(9), false, at).AutomaticInSeconds);
        var credits = new EpisodeSkip(EpisodeSectionKind.Credits, Length - TimeSpan.FromSeconds(60));
        Assert.Equal("Skipping credits in 5", EpisodeSkipPrompt.For(credits, null, Length, credits.End - TimeSpan.FromSeconds(20), true, credits.End - TimeSpan.FromSeconds(15)).PrimaryText);
        var final = new EpisodeSkip(EpisodeSectionKind.Credits, Length);
        Assert.Equal("Next episode in 3", EpisodeSkipPrompt.For(final, null, Length, Length - TimeSpan.FromSeconds(20), true, Length - TimeSpan.FromSeconds(17)).PrimaryText);
    }
}
