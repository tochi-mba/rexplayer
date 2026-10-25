using Rex.Media.AppCore.Shell;
using Rex.Media.Settings;

namespace Rex.Media.Tests.AppCore.Shell;

/// <summary>The window opens where it can be seen, even when the screen it was left on is gone.</summary>
public sealed class WindowPlacementFitTests
{
    private static readonly ScreenArea Laptop = new(0, 0, 1920, 1032);
    private static readonly ScreenArea Monitor = new(1920, -200, 2560, 1400);

    [Fact]
    public void AWindowOnAScreenThatIsThereStaysWhereItWasLeft()
    {
        var onMonitor = new WindowPlacement(2200, 100, 1280, 800, false);

        Assert.Equal(onMonitor, WindowPlacementFit.Fit(onMonitor, [Laptop, Monitor]));
        Assert.Equal(new WindowPlacement(100, 50, 1280, 800, true), WindowPlacementFit.Fit(new WindowPlacement(100, 50, 1280, 800, true), [Laptop]));
    }

    [Fact]
    public void AWindowLeftOnAScreenSinceUnpluggedComesOntoTheMainScreenCentred()
    {
        var fitted = WindowPlacementFit.Fit(new WindowPlacement(2200, 100, 1280, 800, false), [Laptop]);

        Assert.Equal(new WindowPlacement(320, 116, 1280, 800, false), fitted);
    }

    [Fact]
    public void AWindowHangingOffAnEdgeStaysWhileEnoughOfItsTitleBarShows()
    {
        var hanging = new WindowPlacement(-1100, 900, 1280, 800, false);
        Assert.Equal(hanging, WindowPlacementFit.Fit(hanging, [Laptop]));

        var titleBarAbove = new WindowPlacement(100, -20, 1280, 800, false);
        Assert.Equal(116, WindowPlacementFit.Fit(titleBarAbove, [Laptop])!.Y);

        var tooFarLeft = new WindowPlacement(-1200, 100, 1280, 800, false);
        Assert.Equal(320, WindowPlacementFit.Fit(tooFarLeft, [Laptop])!.X);

        var belowTheScreen = new WindowPlacement(100, 1020, 1280, 800, false);
        Assert.Equal(116, WindowPlacementFit.Fit(belowTheScreen, [Laptop])!.Y);
    }

    [Fact]
    public void AWindowIsNeverLargerThanItsScreenNorSmallerThanItsControlsNeed()
    {
        Assert.Equal(new WindowPlacement(0, 0, 1920, 1032, false), WindowPlacementFit.Fit(new WindowPlacement(0, 0, 4000, 3000, false), [Laptop]));
        Assert.Equal(new WindowPlacement(10, 10, 360, 150, false), WindowPlacementFit.Fit(new WindowPlacement(10, 10, 0, -5, false), [Laptop]));
        Assert.Equal(new WindowPlacement(0, 0, 300, 120, false), WindowPlacementFit.Fit(new WindowPlacement(0, 0, 800, 600, false), [new ScreenArea(0, 0, 300, 120)]));
    }

    [Fact]
    public void NothingSavedOrNoScreenKnownChangesNothing()
    {
        Assert.Null(WindowPlacementFit.Fit(null, [Laptop]));
        var saved = new WindowPlacement(-90000, -90000, 1, 1, false);
        Assert.Same(saved, WindowPlacementFit.Fit(saved, []));
        Assert.Throws<ArgumentNullException>(() => WindowPlacementFit.Fit(saved, null!));
    }
}
