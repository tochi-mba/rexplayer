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
        Assert.Contains("No account is required", app.Text("AboutPrivacy"), StringComparison.Ordinal);
        Assert.Contains("Optional metadata lookups and update checks use the network when enabled",
            app.Text("AboutPrivacy"), StringComparison.Ordinal);
        Assert.DoesNotContain("sends nothing about you", app.Text("AboutPrivacy"), StringComparison.Ordinal);
        Assert.False(app.Find("AboutWhatsNew").Current.IsOffscreen);
        app.Press("CloseButton");
        Assert.Equal(0, app.Close());
    }
}
