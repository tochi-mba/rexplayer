using Rex.Media.AppCore.Player;

namespace Rex.Media.Tests.AppCore;

/// <summary>The stage keeps one order: the title, the visualisation under it, the lyrics at the bottom.</summary>
public sealed class StageLayoutTests
{
    [Fact]
    public void AWideStageCentresItsTitleWithNoGapForAMissingCover()
    {
        var view = StageLayout.Resolve(new StageLayoutInput(1000, 470));

        Assert.False(view.Compact);
        Assert.False(view.Tiny);
        Assert.Equal(32, view.Inset);
        Assert.Equal(32, view.BottomInset);
        Assert.Equal(44, view.TitleSize);
        Assert.Equal(560, view.TextMaxWidth);
        Assert.False(view.TitleAtTop);
        Assert.False(view.CoverShown);
        // A collapsed cover column must not push the words half a gap off centre.
        Assert.Equal(0, view.ColumnSpacing);
        Assert.Equal(0, view.RowSpacing);
        Assert.False(view.TextBesideCover);
        Assert.False(view.VisualizerShown);
        Assert.Equal(default, view.Band);
        Assert.True(view.FooterShown);
        Assert.Equal(48, view.DockSide);
        Assert.Equal(24, view.DockBottom);
    }

    [Fact]
    public void ACoverStandsBesideTheWordsOnAWideStageAndAboveThemOnACompactOne()
    {
        var wide = StageLayout.Resolve(new StageLayoutInput(1000, 470, HasCover: true));
        Assert.True(wide.CoverShown);
        Assert.Equal(240, wide.CoverSize);
        Assert.Equal(StageLayout.CoverGap, wide.ColumnSpacing);
        Assert.Equal(0, wide.RowSpacing);
        Assert.True(wide.TextBesideCover);

        var compact = StageLayout.Resolve(new StageLayoutInput(700, 400, HasCover: true));
        Assert.True(compact.Compact);
        Assert.Equal(20, compact.Inset);
        Assert.Equal(32, compact.TitleSize);
        Assert.Equal(660, compact.TextMaxWidth);
        Assert.True(compact.CoverShown);
        Assert.Equal(220, compact.CoverSize);
        Assert.Equal(0, compact.ColumnSpacing);
        Assert.Equal(StageLayout.StackGap, compact.RowSpacing);
        Assert.False(compact.TextBesideCover);
        Assert.Equal(12, compact.DockSide);
        Assert.Equal(12, compact.DockBottom);

        Assert.False(StageLayout.Resolve(new StageLayoutInput(700, 250, HasCover: true)).CoverShown);
        Assert.False(StageLayout.Resolve(new StageLayoutInput(700, 400, HasCover: true, LyricsShown: true)).CoverShown);
    }

    [Fact]
    public void TheTiniestStageKeepsOnlyTheTitleUnlessLyricsAreShowing()
    {
        var tiny = StageLayout.Resolve(new StageLayoutInput(420, 120, HasCover: true, VisualizerWanted: true, FooterHeight: 40));

        Assert.True(tiny.Tiny);
        Assert.Equal(12, tiny.Inset);
        Assert.Equal(24, tiny.TitleSize);
        Assert.Equal(380, tiny.TextMaxWidth);
        Assert.False(tiny.CoverShown);
        Assert.False(tiny.FooterShown);
        Assert.False(tiny.VisualizerShown);
        Assert.False(tiny.TitleAtTop);
        Assert.Equal(12, tiny.BottomInset);
        Assert.Equal(160, StageLayout.Resolve(new StageLayoutInput(120, 120)).TextMaxWidth);
        Assert.True(StageLayout.Resolve(new StageLayoutInput(420, 120, LyricsShown: true)).FooterShown);
    }

    [Fact]
    public void ACentredTitleLeavesTheLyricsStatusItsRoomBelow()
    {
        var view = StageLayout.Resolve(new StageLayoutInput(1000, 470, FooterHeight: 40));

        Assert.False(view.TitleAtTop);
        Assert.Equal(32, view.Inset);
        Assert.Equal(32 + 40 + 24, view.BottomInset);
    }

    [Fact]
    public void AVisualisationDrawsInItsOwnBandBetweenTheTitleAndTheLyricsStatus()
    {
        var shapes = StageLayout.Resolve(new StageLayoutInput(1000, 470, VisualizerWanted: true, TitleHeight: 120, FooterHeight: 40));

        Assert.True(shapes.VisualizerShown);
        Assert.True(shapes.TitleAtTop);
        Assert.Equal(32, shapes.BottomInset);
        Assert.Equal(new StageBand(48, 32 + 120 + StageLayout.Gap, 48, 40 + 24 + StageLayout.Gap), shapes.Band);

        // A software stage spans the width; with nothing under it, it reaches the stage's edge.
        var software = StageLayout.Resolve(new StageLayoutInput(1000, 470, VisualizerWanted: true, FullBleed: true, TitleHeight: 120));
        Assert.Equal(new StageBand(0, 168, 0, 0), software.Band);

        Assert.Equal(new StageBand(48, 168, 48, 48), StageLayout.Resolve(new StageLayoutInput(1000, 470, VisualizerWanted: true, TitleHeight: 120)).Band);
        Assert.Equal(new StageBand(20, 20 + 90 + 16, 20, 12), StageLayout.Resolve(new StageLayoutInput(720, 290, VisualizerWanted: true, TitleHeight: 90)).Band);
        Assert.Equal(new StageBand(8, 12 + 60 + 16, 8, 8), StageLayout.Resolve(new StageLayoutInput(420, 260, VisualizerWanted: true, TitleHeight: 60)).Band);
    }

    [Fact]
    public void ASqueezedBandReachesBehindTheStatusThenBehindTheTitleButNeverDisappears()
    {
        var behindStatus = StageLayout.Resolve(new StageLayoutInput(1000, 470, VisualizerWanted: true, TitleHeight: 300, FooterHeight: 60)).Band;
        Assert.Equal(32 + 300 + 16, behindStatus.Top);
        Assert.Equal(470 - 348 - StageLayout.ShortestBand, behindStatus.Bottom);

        var behindTitle = StageLayout.Resolve(new StageLayoutInput(700, 260, VisualizerWanted: true, TitleHeight: 200)).Band;
        Assert.Equal(0, behindTitle.Bottom);
        Assert.Equal(260 - StageLayout.ShortestBand, behindTitle.Top);
    }

    [Fact]
    public void AStageTooSmallForAVisualisationLeavesItOut()
    {
        Assert.False(StageLayout.Resolve(new StageLayoutInput(1000, 150, VisualizerWanted: true)).VisualizerShown);
        Assert.False(StageLayout.Resolve(new StageLayoutInput(300, 400, VisualizerWanted: true)).VisualizerShown);
        Assert.True(StageLayout.Resolve(new StageLayoutInput(320, 200, VisualizerWanted: true)).VisualizerShown);
        Assert.False(StageLayout.Resolve(new StageLayoutInput(1000, 470, FullBleed: true)).VisualizerShown);
    }

    [Fact]
    public void LyricsTakeTheRoomUnderTheTitleUpToTheirTallestAndKeepTwoLines()
    {
        var roomy = StageLayout.Resolve(new StageLayoutInput(900, 470, LyricsShown: true, TitleHeight: 108, FooterHeight: 74));
        Assert.True(roomy.TitleAtTop);
        Assert.Equal(470 * 0.45, roomy.LyricsHeight);

        var tight = StageLayout.Resolve(new StageLayoutInput(900, 470, LyricsShown: true, TitleHeight: 180, FooterHeight: 74));
        Assert.Equal(470 - (32 + 180) - StageLayout.Gap - (74 + 24), tight.LyricsHeight);

        var cramped = StageLayout.Resolve(new StageLayoutInput(900, 470, LyricsShown: true, TitleHeight: 300, FooterHeight: 74));
        Assert.Equal(StageLayout.ShortestLyrics, cramped.LyricsHeight);

        Assert.Equal(StageLayout.TallestLyrics, StageLayout.Resolve(new StageLayoutInput(1600, 1000, LyricsShown: true)).LyricsHeight);
        Assert.Equal(100 * 0.35, StageLayout.Resolve(new StageLayoutInput(420, 100, LyricsShown: true)).LyricsHeight);
        Assert.Equal(0, StageLayout.Resolve(new StageLayoutInput(-5, -5)).LyricsHeight);
    }

    [Fact]
    public void BesideLyricsTheCoverShrinksToLeaveThemTheirShortestBox()
    {
        var view = StageLayout.Resolve(new StageLayoutInput(1000, 470, HasCover: true, LyricsShown: true, FooterHeight: 74));

        Assert.True(view.CoverShown);
        Assert.Equal(470 - 32 - StageLayout.Gap - StageLayout.ShortestLyrics - (74 + 24), view.CoverSize);
        Assert.False(StageLayout.Resolve(new StageLayoutInput(1000, 450, HasCover: true, LyricsShown: true, FooterHeight: 220)).CoverShown);
    }
}
