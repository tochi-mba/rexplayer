using System.Windows.Automation;
using Rex.Media.TestKit;

namespace Rex.Media.App.Tests;

/// <summary>
/// Non-simulated OS display accessibility lanes. These tests cannot turn a
/// regular 96-DPI or normal-contrast screenshot into high-DPI/contrast proof.
/// Run them on Windows desktops configured for the indicated dedicated lane.
/// </summary>
[Collection("desktop")]
public sealed class LibraryAccessibilityVisualTests
{
    private static void Resize(AppProcess app)
    {
        var transform = (TransformPattern)app.Window.GetCurrentPattern(TransformPattern.Pattern);
        transform.Resize(1000, 760);
        Wait.For(() => Math.Abs(app.Window.Current.BoundingRectangle.Width - 1000) < 2 &&
            Math.Abs(app.Window.Current.BoundingRectangle.Height - 760) < 2,
            "the 1000x760 accessibility viewport");
    }

    [Fact]
    [Capability("A11Y-02")]
    public void RealHighDpiLibraryHasFocusableReadableRows()
    {
        if (Screenshots.Lane != "ci-windows-high-dpi")
        {
            Assert.Skip("Requires the dedicated ci-windows-high-dpi Windows desktop lane.");
            return;
        }

        using var scene = new LibraryScene();
        using var app = scene.WithAsake().Ready().Start();
        Assert.True(WindowPicture.Dpi(app.Handle) >= 144,
            "High-DPI evidence requires actual 150% or greater OS scaling, not a resized 96-DPI image.");
        Resize(app);
        app.Visit("Songs");
        Wait.For(() => app.Lines().Length == 3, "three local song rows on the high-DPI monitor");
        Assert.False(app.Line("Sungba").Current.IsOffscreen);
        Screenshots.Match(app, "library-songs-high-dpi", "LibraryPane");
        Assert.Equal(0, app.Close());
    }

    [Fact]
    [Capability("A11Y-02")]
    public void RealWindowsHighContrastLibraryHasNamedVisibleControls()
    {
        if (Screenshots.Lane != "ci-windows-high-contrast")
        {
            Assert.Skip("Requires the dedicated ci-windows-high-contrast Windows desktop lane.");
            return;
        }

        Assert.True(WindowPicture.HighContrast,
            "High-contrast evidence requires actual Windows accessibility contrast mode.");
        using var scene = new LibraryScene();
        using var app = scene.WithAsake().Ready().Start();
        Resize(app);
        app.Visit("Songs");
        Wait.For(() => app.Lines().Length == 3 &&
            app.IsShown("LibrarySearch"), "accessible library search and local music rows");
        Assert.False(app.Find("LibrarySearch").Current.IsOffscreen);
        Screenshots.Match(app, "library-songs-high-contrast", "LibraryPane");
        Assert.Equal(0, app.Close());
    }
}
