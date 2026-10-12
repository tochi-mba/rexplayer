using Rex.Media.AppCore.Player;
using Rex.Media.Settings;

namespace Rex.Media.Tests.AppCore;

public sealed class SubjectPanelTests
{
    [Theory]
    [InlineData(false, false, "Select a region in the picture.", SubjectState.Waiting)]
    [InlineData(false, false, null, SubjectState.Waiting)]
    [InlineData(true, false, "Acquiring the selected region...", SubjectState.Acquiring)]
    [InlineData(true, true, "Selected region locked. Tracking uses its visible texture, not a person recognizer.", SubjectState.Tracking)]
    [InlineData(true, false, "Tracking uncertain: looking for the selected object to return.", SubjectState.Searching)]
    [InlineData(true, false, null, SubjectState.Searching)]
    [InlineData(true, false, "Similar-looking regions: watching for a clear match. Select again if necessary.", SubjectState.Ambiguous)]
    [InlineData(false, false, "Similar-looking regions: watching for a clear match. Select again if necessary.", SubjectState.Ambiguous)]
    public void TheTrackersStatusBecomesAState(bool locked, bool tracking, string? status, SubjectState expected) =>
        Assert.Equal(expected, SubjectPanel.State(locked, tracking, status));

    [Theory]
    [InlineData(SubjectState.Waiting, 0.0, "Waiting for selection")]
    [InlineData(SubjectState.Acquiring, 0.0, "Acquiring")]
    [InlineData(SubjectState.Tracking, 0.874, "Tracking, 87% match")]
    [InlineData(SubjectState.Tracking, 1.7, "Tracking, 100% match")]
    [InlineData(SubjectState.Tracking, double.NaN, "Tracking, 0% match")]
    [InlineData(SubjectState.Searching, 0.5, "Searching")]
    [InlineData(SubjectState.Ambiguous, 0.5, "Ambiguous")]
    public void ThePillNamesTheStateAndTheConfidenceWhileTracking(SubjectState state, double confidence, string expected) =>
        Assert.Equal(expected, SubjectPanel.Pill(state, confidence));

    [Fact]
    public void ThePillIsTealWhileTrackingAmberWhileUnsureAndGreyWhileWaiting()
    {
        Assert.Equal(0xFF45ECD2u, SubjectPanel.PillColour(SubjectState.Tracking));
        Assert.Equal(0xFFFFC857u, SubjectPanel.PillColour(SubjectState.Searching));
        Assert.Equal(0xFFFFC857u, SubjectPanel.PillColour(SubjectState.Ambiguous));
        Assert.Equal(0xFFB8E0F5u, SubjectPanel.PillColour(SubjectState.Acquiring));
        Assert.Equal(0xFFC2C9D4u, SubjectPanel.PillColour(SubjectState.Waiting));
    }

    [Fact]
    public void TheSliderReadoutsAreShortAndInvariant()
    {
        Assert.Equal("Off", SubjectPanel.FeatherText(0));
        Assert.Equal("3 px", SubjectPanel.FeatherText(3));
        Assert.Equal("50%", SubjectPanel.ToleranceText(50));
        Assert.Equal("100%", SubjectPanel.ToleranceText(140));
        Assert.Equal(3, SubjectPanel.DefaultFeather);
        Assert.Equal(50, SubjectPanel.DefaultTolerance);
    }

    [Fact]
    public void TheButtonsSayWhatTheyWillDo()
    {
        Assert.Equal("Select", SubjectPanel.SelectText(hasSelection: false));
        Assert.Equal("Select again", SubjectPanel.SelectText(hasSelection: true));
        Assert.Equal("Preview removal", SubjectPanel.EraseText(erasing: false));
        Assert.Equal("Show original", SubjectPanel.EraseText(erasing: true));
        Assert.Equal("Move left", SubjectPanel.MoveText(PanelSide.Right));
        Assert.Equal("Move right", SubjectPanel.MoveText(PanelSide.Left));
        Assert.Equal("Click to lock", SubjectPanel.ModeText(SubjectSelectionMode.Click));
        Assert.Equal("Draw a box", SubjectPanel.ModeText(SubjectSelectionMode.Draw));
    }

    [Fact]
    public void ThePanelDocksToASideAndNeverCoversTheMiddle()
    {
        Assert.Equal(PanelSide.Left, SubjectPanel.Opposite(PanelSide.Right));
        Assert.Equal(PanelSide.Right, SubjectPanel.Opposite(PanelSide.Left));

        // A wide stage gets the full width; a narrower one at most two fifths; a tiny one the minimum.
        Assert.Equal(320, SubjectPanel.Width(1600));
        Assert.Equal(320, SubjectPanel.Width(800));
        Assert.Equal(240, SubjectPanel.Width(600));
        Assert.Equal(140, SubjectPanel.Width(300));
        Assert.Equal(140, SubjectPanel.Width(0));
        Assert.Equal(576, SubjectPanel.MaxHeight(600));
        Assert.Equal(48, SubjectPanel.MaxHeight(10));
    }

    [Fact]
    public void TheInstructionAndHelpNameTheSelectionMode()
    {
        var click = SubjectPanel.Instruction(SubjectSelectionMode.Click, follow: true);
        Assert.StartsWith("Drag a box", click, StringComparison.Ordinal);
        Assert.Contains("auto-follow widens", click, StringComparison.Ordinal);
        var draw = SubjectPanel.Instruction(SubjectSelectionMode.Draw, follow: false);
        Assert.StartsWith("Drag a box", draw, StringComparison.Ordinal);
        Assert.Contains("auto-follow is off", draw, StringComparison.Ordinal);

        var clickHelp = SubjectPanel.Help(SubjectSelectionMode.Click);
        var drawHelp = SubjectPanel.Help(SubjectSelectionMode.Draw);
        Assert.Equal(3, clickHelp.Count);
        Assert.Equal(3, drawHelp.Count);
        Assert.StartsWith("Click to lock", clickHelp[0], StringComparison.Ordinal);
        Assert.StartsWith("Draw a box", drawHelp[0], StringComparison.Ordinal);
        Assert.Equal(clickHelp[1], drawHelp[1]);
        Assert.Contains("plain wall", clickHelp[1], StringComparison.Ordinal);
        Assert.Contains("leaves the picture", clickHelp[2], StringComparison.Ordinal);
        foreach (var line in clickHelp.Concat(drawHelp).Append(click).Append(draw))
        {
            Assert.DoesNotContain('\u2014', line);
            Assert.DoesNotContain('\u00B7', line);
        }
    }

    [Fact]
    public void AClickIsASmallMovementInClickModeOnly()
    {
        Assert.True(SubjectPanel.IsClick(SubjectSelectionMode.Click, 3, -4));
        Assert.False(SubjectPanel.IsClick(SubjectSelectionMode.Click, 8, 0));
        Assert.False(SubjectPanel.IsClick(SubjectSelectionMode.Click, 0, -9));
        Assert.False(SubjectPanel.IsClick(SubjectSelectionMode.Draw, 0, 0));
    }

    [Fact]
    public void ThePanelSettingsHaveDefaultsAndAreMended()
    {
        var defaults = new PlayerSettings();
        Assert.Equal(PanelSide.Right, defaults.SubjectPanelSide);
        Assert.Equal(SubjectSelectionMode.Click, defaults.SubjectSelection);
        Assert.True(defaults.SubjectFollowingOpen);
        Assert.True(defaults.SubjectRemovalOpen);
        Assert.False(defaults.SubjectHelpOpen);

        var mended = new PlayerSettings { SubjectPanelSide = (PanelSide)9, SubjectSelection = (SubjectSelectionMode)9 }.Normalize();
        Assert.Equal((PanelSide.Right, SubjectSelectionMode.Click), (mended.SubjectPanelSide, mended.SubjectSelection));

        var kept = new PlayerSettings { SubjectPanelSide = PanelSide.Left, SubjectSelection = SubjectSelectionMode.Draw, SubjectHelpOpen = true }.Normalize();
        Assert.Equal((PanelSide.Left, SubjectSelectionMode.Draw, true), (kept.SubjectPanelSide, kept.SubjectSelection, kept.SubjectHelpOpen));
    }
}
