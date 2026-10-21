using System.Collections.Specialized;
using System.IO;
using System.Windows.Automation;
using Rex.Media.AppCore.Commands;
using Rex.Media.Settings;
using Rex.Media.TestKit;

namespace Rex.Media.App.Tests;

/// <summary>
/// Every way media comes in besides the command line: a typed location, the
/// clipboard (text and copied files), and a second launch that joins the queue.
/// </summary>
public sealed partial class AppWindowTests
{
    /// <summary>Puts something on the clipboard; the clipboard wants a single-threaded apartment.</summary>
    private static void OnClipboard(Action<System.Windows.DataObject> fill)
    {
        var thread = new Thread(() =>
        {
            var data = new System.Windows.DataObject();
            fill(data);
            System.Windows.Clipboard.SetDataObject(data, copy: true);
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
    }

    [Fact]
    [Capability("PB-01")]
    public void TheClipboardPlaysCopiedFilesAndPathsAndSaysWhenItHoldsNoMedia()
    {
        var copied = Named("Lonely At The Top", 30);
        var typed = Named("Terminator", 30);
        using var app = AppProcess.Start();

        OnClipboard(data => data.SetFileDropList(new StringCollection { copied }));
        app.Run(CommandCatalog.PasteLocation);
        Wait.For(() => app.Text("NowPlaying") == "Lonely At The Top", "the copied file to play");

        OnClipboard(data => data.SetText("\"" + typed + "\""));
        app.Run(CommandCatalog.PasteLocation);
        Wait.For(() => app.Text("NowPlaying") == "Terminator", "the copied path to play");

        OnClipboard(data => data.SetText("Asake - Lonely At The Top, the lyrics"));
        app.Run(CommandCatalog.PasteLocation);
        Wait.For(() => app.LogText.Contains("The clipboard has no media rexplayer can open.", StringComparison.Ordinal), "the clipboard to be refused");
        Assert.Equal("Terminator", app.Text("NowPlaying"));
        Assert.Equal(0, app.Close());
    }

    [Fact]
    [Capability("PB-01")]
    public void ATypedLocationOpensFromItsDialog()
    {
        var song = Named("Sungba", 30);
        using var app = AppProcess.Start();

        app.Run(CommandCatalog.OpenLocation);
        ((ValuePattern)app.Find("LocationBox").GetCurrentPattern(ValuePattern.Pattern)).SetValue("\"" + song + "\"");
        app.Press("PrimaryButton");
        Wait.For(() => app.Text("NowPlaying") == "Sungba", "the typed location to play");

        Assert.Equal(0, app.Close());
    }

    [Fact]
    [Capability("TOOL-06")]
    public void ASecondLaunchJoinsTheQueueWhenPreferencesSaySo()
    {
        var root = Path.Combine(_media, "enqueue-data");
        Directory.CreateDirectory(root);
        SettingsStore.Save(Path.Combine(root, "settings.json"), new PlayerSettings
        {
            FirstRunDone = true,
            LastSeenVersion = AppProcess.Version,
            UpdateChecks = UpdateCadence.Off,
            EnqueueFromSecondLaunch = true,
        });
        using var app = AppProcess.Start([Named("Lonely At The Top", 30)], root);
        Wait.For(() => app.Text("NowPlaying") == "Lonely At The Top", "the first song");

        Assert.Equal(0, AppProcess.Launch([Named("Terminator", 30)], root));

        app.Toggle("PlaylistButton");
        Wait.For(() => All(app.Find("PlaylistView"), ControlType.ListItem).Length == 2, "the handed-over song to join the queue");
        Assert.Equal("Lonely At The Top", app.Text("NowPlaying"));
        app.Press("NextButton");
        Wait.For(() => app.Text("NowPlaying") == "Terminator", "the queued song to play next");
        Assert.Equal(0, app.Close());
    }
}
