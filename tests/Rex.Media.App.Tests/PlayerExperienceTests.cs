using System.Windows.Automation;

namespace Rex.Media.App.Tests;

/// <summary>Everyday entry points and recovery remain usable at compact window sizes.</summary>
[Collection("desktop")]
public sealed class PlayerExperienceTests
{
    [Fact]
    public void WelcomeOpensTheLibraryWithoutOpeningAMenu()
    {
        using var app = AppProcess.Start();
        Wait.For(() => app.IsShown("WelcomeOpen") && app.IsShown("WelcomeLibrary"), "the welcome actions");
        Screenshots.Match(app, "experience-welcome");
        app.Press("WelcomeLibrary");
        Wait.For(() => app.IsShown("LibraryPane") && app.IsShown("LibraryQuickAddFolder"), "the library and its add-folder action");
        Assert.Equal(0, app.Close());
    }

    [Theory]
    [InlineData("Dark")]
    [InlineData("Light")]
    public void CompactSearchStaysInsideTheWindowAndCanRecoverFromNoResults(string theme)
    {
        using var scene = new LibraryScene();
        scene.WithAsake().Ready(settings => settings with { Theme = theme == "Dark" ? Rex.Media.Settings.ThemeChoice.Dark : Rex.Media.Settings.ThemeChoice.Light });
        using var app = scene.Start();
        app.Visit("Songs");
        var transform = (TransformPattern)app.Window.GetCurrentPattern(TransformPattern.Pattern);
        // GitHub's virtual desktop cannot reliably accommodate a 1200 px-wide window.
        // Exercise the expanded rail above the 760 px breakpoint at an attainable size,
        // and require the search field to occupy at least 60% of that window.
        transform.Resize(1000, 600);
        Wait.For(() => Math.Abs(app.Window.Current.BoundingRectangle.Width - 1000) < 2
            && app.Find("LibrarySearch").Current.BoundingRectangle.Width > 600,
            "the wide search field",
            () => $"Window: {app.Window.Current.BoundingRectangle}; search: {app.Find("LibrarySearch").Current.BoundingRectangle}");
        Screenshots.Match(app, "experience-library-wide-" + theme.ToLowerInvariant());
        transform.Resize(720, 720);
        Wait.For(() => Math.Abs(app.Window.Current.BoundingRectangle.Width - 720) < 2 && app.Find("LibrarySearch").Current.BoundingRectangle.Width > 120, "the compact search field");
        var bounds = app.Window.Current.BoundingRectangle;
        var searchBounds = app.Find("LibrarySearch").Current.BoundingRectangle;
        Assert.True(searchBounds.Left >= bounds.Left && searchBounds.Right <= bounds.Right, "Search must fit within the window.");
        var search = (ValuePattern)app.Find("LibrarySearch").GetCurrentPattern(ValuePattern.Pattern);
        search.SetValue("a title that does not exist");
        Wait.For(() => app.IsShown("LibraryEmptyClearSearch"), "the empty search recovery action");
        Screenshots.Match(app, "experience-search-" + theme.ToLowerInvariant());
        app.Press("LibraryEmptyClearSearch");
        Wait.For(() => app.Lines().Length == 3, "the songs to return after clearing search");
        Assert.Equal("", search.Current.Value);
        Screenshots.Match(app, "experience-library-" + theme.ToLowerInvariant());
        Assert.Equal(0, app.Close());
    }
}
