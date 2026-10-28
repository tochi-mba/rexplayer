using Rex.Media.AppCore.Shell;
using Rex.Media.TestKit;

namespace Rex.Media.Tests.AppCore;

/// <summary>How big dialogs and Preferences are drawn in windows of every size (UI-10, UI-17).</summary>
public sealed class DialogLayoutTests
{
    /// <summary>What a Windows dialog adds around its content, and the tallest one it draws.</summary>
    private static readonly DialogChrome Windows = new(Across: 50, Around: 171, Tallest: 756);

    [Fact]
    [Capability("UI-17")]
    public void OrdinaryDialogsShareOneWidthAndKeepAMarginFromTheWindow()
    {
        Assert.Equal(DialogLayout.OrdinaryWidth, DialogLayout.Width(DialogLayout.OrdinaryWidth, 984, 661, Windows));
        Assert.Equal(DialogLayout.WideWidth, DialogLayout.Width(DialogLayout.WideWidth, 984, 721, Windows));

        // A narrower window takes off what it must to keep the margin: 704 less 2 x 24 less the chrome.
        Assert.Equal(606, DialogLayout.Width(800, 704, 609, Windows));

        // A small window keeps a small margin, and a tiny one still gets a usable dialog.
        Assert.Equal(DialogLayout.SmallMargin, DialogLayout.MarginIn(404, 309));
        Assert.Equal(DialogLayout.SmallMargin, DialogLayout.MarginIn(900, 470));
        Assert.Equal(DialogLayout.Margin, DialogLayout.MarginIn(704, 609));
        Assert.Equal(338, DialogLayout.Width(DialogLayout.OrdinaryWidth, 404, 309, Windows));
        Assert.Equal(DialogLayout.MinimumWidth, DialogLayout.Width(DialogLayout.OrdinaryWidth, 200, 200, Windows));
    }

    [Fact]
    [Capability("UI-17")]
    public void ADialogIsNeverTallerThanTheWindowLessItsMarginOrThePlatformDraws()
    {
        // 721 less 2 x 24 less the title and buttons.
        Assert.Equal(502, DialogLayout.Height(900, 984, 721, Windows));
        Assert.Equal(300, DialogLayout.Height(300, 984, 721, Windows));

        // In a tall window the platform's tallest dialog is the limit.
        Assert.Equal(756 - 171, DialogLayout.Height(2000, 2544, 1400, Windows));
        Assert.Equal(DialogLayout.MinimumHeight, DialogLayout.Height(900, 404, 200, Windows));
    }

    [Fact]
    [Capability("UI-10")]
    public void PreferencesSetsTheRailBesideThePageInAWideWindow()
    {
        var layout = PreferencesLayout.For(984, 721, Windows);

        Assert.True(layout.Wide);
        Assert.False(layout.Stacked);
        Assert.Equal(PreferencesLayout.WideWidth, layout.Width);
        Assert.Equal(502, layout.Height);

        // The page beside the rail is never squeezed below a readable width at the narrowest wide window.
        var narrowest = PreferencesLayout.For(PreferencesLayout.WideFrom, 721, Windows);
        Assert.True(narrowest.Wide);
        Assert.True(narrowest.Width - PreferencesLayout.RailWidth - PreferencesLayout.Gap >= 480);
    }

    [Fact]
    [Capability("UI-10")]
    public void PreferencesPutsThePickerAndSearchAboveThePageInANarrowWindow()
    {
        var layout = PreferencesLayout.For(704, 609, Windows);
        Assert.False(layout.Wide);
        Assert.False(layout.Stacked);
        Assert.Equal(PreferencesLayout.NarrowWidth, layout.Width);
        Assert.Equal(609 - 48 - 171, layout.Height);

        // Too narrow to set them side by side: one above the other.
        var smallest = PreferencesLayout.For(404, 309, Windows);
        Assert.False(smallest.Wide);
        Assert.True(smallest.Stacked);
        Assert.Equal(338, smallest.Width);
        Assert.Equal(309 - 16 - 171, smallest.Height);
        Assert.Equal(DialogLayout.MinimumHeight, PreferencesLayout.For(404, 250, Windows).Height);
    }
}
