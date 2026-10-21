using System.Windows;
using System.Windows.Automation;

namespace Rex.Media.App.Tests;

public sealed partial class AppWindowTests
{
    /// <summary>A launch given a blank argument, as a shortcut or script may pass, opens its window instead of quitting before it shows.</summary>
    [Fact]
    public void ALaunchWithABlankArgumentStillOpensItsWindow()
    {
        using var app = AppProcess.Start(["", "  "]);

        Assert.Equal("Idle", app.Status().State);
        Assert.Equal(0, app.Close());
    }

    /// <summary>A window left on a screen since unplugged opens on the screen there is, where it can be seen and reached.</summary>
    [Fact]
    public void AWindowLeftOnAScreenSinceUnpluggedOpensWhereItCanBeSeen()
    {
        using var app = AppProcess.Start(extraSettings: "\"window\": { \"x\": -40000, \"y\": -40000, \"width\": 1000, \"height\": 700, \"maximized\": false }");

        var bounds = (Rect)app.Window.GetCurrentPropertyValue(AutomationElement.BoundingRectangleProperty);
        Assert.True(bounds.IntersectsWith(SystemParameters.WorkArea), $"The window opened at {bounds}, off every screen.");
        Assert.Equal(0, app.Close());
    }
}
