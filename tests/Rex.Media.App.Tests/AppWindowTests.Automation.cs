using System.IO;
using Rex.Media.AppCore.Automation;
using Rex.Media.AppCore.Commands;
using Rex.Media.TestKit;

namespace Rex.Media.App.Tests;

/// <summary>
/// The real window answering the pipe's window-driving commands (protocol 2): what a script sees
/// is what the window built, and its actions reach the same controls a person uses. The macOS
/// window must answer these the same way, which is how the two are compared.
/// </summary>
public sealed partial class AppWindowTests
{
    [Fact]
    [Capability("TOOL-03")]
    public void TheWindowReportsTheMenusItBuiltFromTheSharedLayout()
    {
        using var app = AppProcess.Start();

        var reply = app.Ui(new AutomationRequest { Command = "ui.menu" });

        Assert.True(reply.Ok, reply.Error);
        var menus = reply.Menus!;
        Assert.Equal(["Media", "Playback", "Audio", "Subtitles", "Video", "View", "Help"], menus.Select(menu => menu.Title));
        var media = menus[0];
        var open = Assert.Single(media.Items, item => item.Id == "Command-" + CommandCatalog.OpenFile);
        Assert.Equal(("command", "Ctrl+O"), (open.Kind, open.Shortcut));
        var playback = menus.Single(menu => menu.Title == "Playback");
        Assert.Contains(playback.Items, item => item.Kind == "submenu" && item.Items.Any(inner => inner.Id == "Command-" + CommandCatalog.JumpForwardLong));
        Assert.Contains(playback.Items, item => item is { Kind: "toggle", Id: "Command-" + CommandCatalog.ToggleShuffle });
        Assert.Equal(0, app.Close());
    }

    [Fact]
    public void AScriptSeesTheControlsAndWorksThemAsAPersonWould()
    {
        using var app = AppProcess.Start();

        var tree = app.Ui(new AutomationRequest { Command = "ui.tree" });
        Assert.True(tree.Ok, tree.Error);
        var shuffle = tree.Controls!.Single(control => control.Id == "ShuffleButton");
        Assert.Equal(false, shuffle.Checked);
        Assert.Contains(tree.Controls!, control => control.Id == "PlayPauseButton");

        Assert.True(app.Ui(new AutomationRequest { Command = "ui.toggle", Target = "ShuffleButton" }).Ok);
        Wait.For(() => app.SavedSettings.Shuffle, "shuffle to be switched on from the pipe");
        Assert.True(app.Ui(new AutomationRequest { Command = "ui.key", Chord = "R" }).Ok);
        Wait.For(() => !app.SavedSettings.Shuffle, "R to switch shuffle off again");

        var missing = app.Ui(new AutomationRequest { Command = "ui.press", Target = "NoSuchButton" });
        Assert.False(missing.Ok);
        Assert.Contains("NoSuchButton", missing.Error, StringComparison.Ordinal);

        var picture = Path.Combine(app.Root, "window.png");
        Assert.True(app.Ui(new AutomationRequest { Command = "ui.snapshot", Path = picture }).Ok);
        Assert.Equal([0x89, (byte)'P', (byte)'N', (byte)'G'], File.ReadAllBytes(picture)[..4]);
        Assert.Equal(0, app.Close());
    }
}
