using Rex.Media.AppCore.Commands;
using Rex.Media.TestKit;

namespace Rex.Media.App.Tests;

/// <summary>Help, About rexplayer: the version, where the data lives and the way on.</summary>
public sealed partial class AppWindowTests
{
    [Fact]
    [Capability("UI-02")]
    public void AboutNamesTheVersionAndWhereTheSettingsLive()
    {
        using var app = AppProcess.Start();
        app.Run(CommandCatalog.About);
        Assert.Equal("rexplayer " + AppProcess.Version, app.Text("AboutVersion"));
        Assert.Contains(app.Root, app.Text("AboutDataFolder"), StringComparison.OrdinalIgnoreCase);
        Assert.False(app.Find("AboutWhatsNew").Current.IsOffscreen);
        app.Press("CloseButton");
        Assert.Equal(0, app.Close());
    }
}
