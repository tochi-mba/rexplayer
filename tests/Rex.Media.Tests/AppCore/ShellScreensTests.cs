using System.Text.Json;
using System.Text.RegularExpressions;
using Rex.Media.AppCore.Design;
using Rex.Media.TestKit;

namespace Rex.Media.Tests.AppCore;

public sealed class ShellScreensTests
{
    [Theory]
    [InlineData(ShellControlKind.Icon, "icon")]
    [InlineData(ShellControlKind.IconToggle, "icon-toggle")]
    [InlineData(ShellControlKind.AccentIcon, "accent-icon")]
    [InlineData(ShellControlKind.Text, "text")]
    [InlineData(ShellControlKind.AccentText, "accent-text")]
    [InlineData(ShellControlKind.OverlayText, "overlay-text")]
    [InlineData(ShellControlKind.Label, "label")]
    [InlineData(ShellControlKind.Slider, "slider")]
    public void EveryKindHasItsFixedSpelling(ShellControlKind kind, string spelling)
    {
        Assert.Equal(spelling, ShellScreens.Spell(kind));
    }

    [Fact]
    public void AKindThatDoesNotExistIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ShellScreens.Spell((ShellControlKind)99));
    }

    // Written out in full so that changing what a screen shows is a visible, deliberate edit here.
    [Fact]
    public void TheWelcomeScreenIsDescribedExactly()
    {
        Assert.Equal(
            "IdleKicker:label;IdleTitle:label;IdleHint:label;WelcomeOpen:accent-text;WelcomeLibrary:overlay-text;WelcomeTagline:label",
            ShellScreens.Describe(ShellScreens.Welcome));
    }

    [Fact]
    public void TheTransportBarIsDescribedExactly()
    {
        Assert.Equal(
            "StartOverButton:text;Elapsed:label;SeekBar:slider;Remaining:label;"
            + "PreviousButton:icon;PlayPauseButton:accent-icon;StopButton:icon;NextButton:icon;"
            + "ShuffleButton:icon-toggle;RepeatButton:icon-toggle;PlaylistButton:icon-toggle;LibraryButton:icon-toggle;"
            + "FullScreenButton:icon;NowPlaying:label;SpeedButton:text;MuteButton:icon;VolumeBar:slider",
            ShellScreens.Describe(ShellScreens.Transport));
    }

    [Fact]
    public void EveryScreenIsListedInTheOrderOfItsLookRow()
    {
        Assert.Equal([ShellScreens.Welcome, ShellScreens.Transport], ShellScreens.All);
        Assert.Equal(["LOOK-01", "LOOK-02"], ShellScreens.All.Select(screen => screen.LookRow));
        Assert.Equal(["Welcome", "Transport"], ShellScreens.All.Select(screen => screen.Name));
    }

    [Fact]
    public void AScreenIsFoundByItsLookRowAndNoneForAnUnknownRow()
    {
        Assert.Same(ShellScreens.Welcome, ShellScreens.ForLookRow("LOOK-01"));
        Assert.Same(ShellScreens.Transport, ShellScreens.ForLookRow("LOOK-02"));
        Assert.Null(ShellScreens.ForLookRow("LOOK-99"));
        Assert.Null(ShellScreens.ForLookRow("look-01"));
    }

    [Fact]
    public void NoScreenNamesAControlTwice()
    {
        foreach (var screen in ShellScreens.All)
        {
            var ids = screen.Controls.Select(control => control.Id).ToList();
            Assert.True(ids.Count == ids.Distinct(StringComparer.Ordinal).Count(), screen.Name);
        }
    }

    [Fact]
    public void EveryScreenProvesALookRowOfTheMatrix()
    {
        var areas = MatrixRows().ToDictionary(row => row.Id, row => row.Area, StringComparer.Ordinal);
        foreach (var screen in ShellScreens.All)
        {
            Assert.True(areas.TryGetValue(screen.LookRow, out var area), screen.LookRow);
            Assert.Equal("Look", area);
        }
    }

    [Fact]
    public void EveryLookRowIsNumberedAsALookRow()
    {
        var look = MatrixRows().Where(row => row.Area == "Look").ToList();
        Assert.NotEmpty(look);
        Assert.All(look, row => Assert.Matches(new Regex("^LOOK-[0-9]{2}$"), row.Id));
    }

    private static List<(string Id, string Area)> MatrixRows()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(RepoPaths.Combine("docs", "capability-matrix.json")));
        return [.. document.RootElement.GetProperty("capabilities").EnumerateArray()
            .Select(row => (row.GetProperty("id").GetString()!, row.GetProperty("area").GetString()!))];
    }
}
