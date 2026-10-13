using Rex.Media.AppCore.Player;

namespace Rex.Media.Tests.AppCore;

/// <summary>What the window shows for the intro and credits, and the automatic skip's countdown, decided without a window.</summary>
public sealed class EpisodeSectionViewTests
{
    private static readonly TimeSpan Length = TimeSpan.FromMinutes(44);
    private static readonly SectionCandidate Intro = new(EpisodeSectionKind.Intro, TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(110), 0.93, "Repeats", "shared audio");
    private static readonly SectionCandidate Credits = new(EpisodeSectionKind.Credits, TimeSpan.FromMinutes(42), Length, 0.88, "Text", "video frames");

    [Fact]
    public void SavedBoundariesComeFirstThenDetectionsThatAreNotHidden()
    {
        var proposals = new SectionProposals("s", [Intro, Credits]);
        var nothing = EpisodeSectionView.For(new EpisodeSections(), null, Length);
        Assert.Equal([EpisodeSectionKind.Intro, EpisodeSectionKind.Credits], nothing.Select(view => view.Kind));
        Assert.All(nothing, view => Assert.Null(view.Start));
        Assert.All(nothing, view => Assert.False(view.CanHide));
        Assert.Equal(["intro", "credits"], nothing.Select(view => view.Noun));

        var detected = EpisodeSectionView.For(new EpisodeSections(), proposals, Length);
        Assert.Equal(Intro.Start, detected[0].Start);
        Assert.False(detected[0].Saved);
        Assert.True(detected[0].CanHide);
        Assert.Equal(Credits, detected[1].Detected);

        // Hidden: nothing drawn, still known so it can be shown again.
        var hidden = EpisodeSectionView.For(new EpisodeSections(), proposals.Reject(EpisodeSectionKind.Intro), Length)[0];
        Assert.Null(hidden.Start);
        Assert.True(hidden.Hidden);
        Assert.Equal(Intro, hidden.Detected);

        // Saved boundaries replace the detection; a start alone for the intro shows nothing yet.
        var saved = new EpisodeSections(TimeSpan.FromSeconds(58), TimeSpan.FromSeconds(112), CreditsStart: TimeSpan.FromMinutes(41));
        var mine = EpisodeSectionView.For(saved, proposals, Length);
        Assert.Equal((TimeSpan.FromSeconds(58), TimeSpan.FromSeconds(112), true), (mine[0].Start!.Value, mine[0].End!.Value, mine[0].Saved));
        Assert.False(mine[0].CanHide);
        Assert.Equal(Length, mine[1].End);
        var partial = EpisodeSectionView.For(new EpisodeSections(IntroStart: TimeSpan.FromSeconds(58)), proposals, Length)[0];
        Assert.Null(partial.Start);
        Assert.True(partial.HasSavedBoundary);
        Assert.Throws<ArgumentNullException>(() => EpisodeSectionView.For(null!, proposals, Length));
    }

    [Fact]
    public void CreditsWithAStartAloneRunToTheEnd()
    {
        var credits = new EpisodeSections(CreditsStart: TimeSpan.FromMinutes(42));
        Assert.Equal(new EpisodeSkip(EpisodeSectionKind.Credits, Length), credits.Offer(TimeSpan.FromMinutes(43), Length));
        Assert.Equal((TimeSpan.FromMinutes(42), Length), credits.Span(EpisodeSectionKind.Credits, Length));
        Assert.Null(credits.Span(EpisodeSectionKind.Intro, Length));
        Assert.True(credits.Has(EpisodeSectionKind.Credits));
        Assert.False(credits.Has(EpisodeSectionKind.Intro));
        Assert.Null(new EpisodeSections(CreditsEnd: TimeSpan.FromMinutes(43)).Offer(TimeSpan.FromMinutes(42.5), Length));
        Assert.Null(new EpisodeSections(CreditsStart: Length - TimeSpan.FromSeconds(1)).Span(EpisodeSectionKind.Credits, Length));
        Assert.Throws<ArgumentOutOfRangeException>(() => credits.Span((EpisodeSectionKind)7, Length));
    }

    [Fact]
    public void TheButtonsTooltipSaysWhereItGoesAndItsShortcut()
    {
        var intro = EpisodeSkipPrompt.For(new EpisodeSkip(EpisodeSectionKind.Intro, TimeSpan.FromSeconds(110)), Intro, Length, TimeSpan.FromSeconds(70), true, null);
        Assert.Equal("Go to where the intro ends (I)", intro.Hint("Show S01E03", "I"));
        Assert.Equal("Go to where the intro ends", intro.Hint(null, ""));
        var next = EpisodeSkipPrompt.For(new EpisodeSkip(EpisodeSectionKind.Credits, Length), Credits, Length, TimeSpan.FromMinutes(43), true, null);
        Assert.Equal("Play Show S01E03 (I)", next.Hint("Show S01E03", "I"));
        Assert.False(next.SceneAfter);
        var last = EpisodeSkipPrompt.For(new EpisodeSkip(EpisodeSectionKind.Credits, Length), Credits, Length, TimeSpan.FromMinutes(43), false, null);
        Assert.Equal("Go to the end", last.Hint(null, ""));
        var scene = EpisodeSkipPrompt.For(new EpisodeSkip(EpisodeSectionKind.Credits, Length - TimeSpan.FromSeconds(40)), Credits, Length, TimeSpan.FromMinutes(42.5), true, null);
        Assert.True(scene.SceneAfter);
        Assert.Equal("Go to the scene after the credits", scene.Hint("Show S01E03", ""));
    }

    [Fact]
    public void AnAutomaticSkipCountsDownOnceAndCanBeCancelled()
    {
        var skip = new AutomaticSkip();
        var intro = new EpisodeSkip(EpisodeSectionKind.Intro, TimeSpan.FromSeconds(110));
        Assert.False(skip.Advance(null, true, TimeSpan.Zero));
        Assert.False(skip.Advance(intro, false, TimeSpan.FromSeconds(60)));
        Assert.Null(skip.Pending);
        Assert.False(skip.Advance(intro, true, TimeSpan.FromSeconds(60)));
        Assert.Equal((EpisodeSectionKind.Intro, TimeSpan.FromSeconds(65)), skip.Pending);
        Assert.False(skip.Advance(intro, true, TimeSpan.FromSeconds(64)));
        Assert.True(skip.Advance(intro, true, TimeSpan.FromSeconds(65)));
        Assert.Null(skip.Pending);
        // Once per section: going back into it plays it.
        Assert.False(skip.Advance(intro, true, TimeSpan.FromSeconds(61)));
        Assert.False(skip.Cancel());

        // Never past a second before the section ends.
        var credits = new EpisodeSkip(EpisodeSectionKind.Credits, TimeSpan.FromSeconds(2600));
        Assert.False(skip.Advance(credits, true, TimeSpan.FromSeconds(2597)));
        Assert.Equal(TimeSpan.FromSeconds(2599), skip.Pending!.Value.Due);
        Assert.True(skip.Cancel());
        Assert.False(skip.Advance(credits, true, TimeSpan.FromSeconds(2599.5)));

        // A new episode starts afresh; a section skipped by hand does not count down again.
        skip.Reset();
        Assert.False(skip.Advance(intro, true, TimeSpan.FromSeconds(60)));
        skip.Finish(EpisodeSectionKind.Intro);
        Assert.Null(skip.Pending);
        Assert.False(skip.Advance(intro, true, TimeSpan.FromSeconds(100)));
        Assert.Equal(TimeSpan.FromSeconds(5), AutomaticSkip.Delay);
    }
}
