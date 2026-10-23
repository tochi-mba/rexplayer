using System.Windows.Automation;
using Rex.Media.AppCore.Commands;
using Rex.Media.TestKit;

namespace Rex.Media.App.Tests;

/// <summary>Real Windows UI Automation of saved playlist editing and the queue's semantic summary.</summary>
public sealed partial class AppWindowTests
{
    [Fact]
    [Capability("FMT-C19")]
    public void SlideshowPausesAndResumesAndStopsWithoutStrandingFullScreenOrGif()
    {
        var jpg = RepoPaths.Combine("tests", "fixtures", "picture", "still.jpg");
        var gif = RepoPaths.Combine("tests", "fixtures", "picture", "anim.gif");
        var root = Path.Combine(Path.GetTempPath(), "rexplayer-slideshow-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        LibraryScene.WriteSettings(root, settings => settings with { PictureSeconds = 60 });
        try
        {
            using var app = AppProcess.Start([jpg, gif], root);
            Wait.For(() => app.Text("NowPlaying") == "still" && app.IsShown("PictureIntervalButton"),
                "the still picture to be ready");

            app.Run(CommandCatalog.PlayPause);
            Wait.For(() => app.Find("PlayPauseButton").Current.Name == "Play", "the picture to pause");
            Assert.Equal("still", app.Text("NowPlaying"));

            app.Run(CommandCatalog.ToggleFullScreen);
            Wait.For(() => !app.IsShown("Menu"), "fullscreen slideshow");
            app.Run(CommandCatalog.PlayPause);
            Wait.For(() => app.Find("PlayPauseButton").Current.Name == "Pause", "the slideshow to resume");
            app.Run(CommandCatalog.Next);
            Wait.For(() => app.LogText.Contains("Opened anim.gif as", StringComparison.Ordinal)
                && app.IsShown("PictureIntervalButton"), "the animated GIF to start");

            app.Run(CommandCatalog.Stop);
            Wait.For(() => !app.IsShown("PictureIntervalButton"), "picture controls to cancel");
            app.Run(CommandCatalog.LeaveFullScreen);
            Wait.For(() => app.IsShown("Menu"), "normal controls to return");
            Assert.Equal(0, app.Close());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    [Capability("FMT-C19")]
    public void PictureSlideshowHasDiscoverableIntervalAndKeepsCurrentPictureWhenItChanges()
    {
        var picture = RepoPaths.Combine("tests", "fixtures", "picture", "still.png");
        using var app = AppProcess.Start([picture]);
        Wait.For(() => app.IsShown("PictureIntervalButton"), "slideshow interval to appear");
        Assert.Contains("5 seconds", app.Text("PictureIntervalButton"), StringComparison.Ordinal);
        var before = app.Text("NowPlaying");
        app.Press("PictureIntervalButton");
        app.Press("PictureInterval-10");
        Wait.For(() => app.Text("PictureIntervalButton").Contains("10 seconds", StringComparison.Ordinal),
            "new slideshow duration");
        Assert.Equal(before, app.Text("NowPlaying"));
        Wait.For(() => app.SavedSettings.PictureSeconds == 10, "the interval to be kept");
        Assert.Equal(0, app.Close());
    }

    [Fact]
    [Capability("PB-12")]
    public void KeyboardQueueReorderCanBeUndoneWithoutChangingPlayback()
    {
        using var app = AppProcess.Start([Song(60), Named("Terminator", 60), Named("Joha", 60)]);
        Wait.For(() => app.Text("NowPlaying") == "Sungba", "the first song");
        app.Run(CommandCatalog.TogglePlaylist);
        Wait.For(() => app.Text("PlaylistQueueCount") == "3 items", "the three queue entries");
        var list = app.Find("PlaylistView");
        var rows = Wait.Until(() =>
        {
            var found = list.FindAll(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem));
            return found.Count >= 3 ? found.Cast<AutomationElement>().ToArray() : null;
        })!;
        ((SelectionItemPattern)rows[^1].GetCurrentPattern(SelectionItemPattern.Pattern)).Select();
        Input.Focus(app);
        rows[^1].SetFocus();
        Input.Press("Alt+Up");

        Wait.For(() => app.Find("PlaylistUndoReorder").Current.IsEnabled, "undo reorder to be available");
        Wait.For(() => app.Text("PlaylistQueueNext") == "Up next: Joha", "the selected song to move next");
        Assert.Equal("Now playing: Sungba", app.Text("PlaylistQueueNow"));
        app.Press("PlaylistUndoReorder");
        Wait.For(() => app.Text("PlaylistQueueNext") == "Up next: Terminator", "original queue order to return");
        Assert.False(app.Find("PlaylistUndoReorder").Current.IsEnabled);
        Assert.Equal("3 items", app.Text("PlaylistQueueCount"));
        Assert.Equal(0, app.Close());
    }

    [Fact]
    [Capability("LIB-04")]
    public void SavedPlaylistEditorStagesMovesAndRemovalWithoutInterruptingThePlayingQueue()
    {
        using var app = AppProcess.Start([Song(60), Named("Terminator", 60)]);
        Wait.For(() => app.Text("NowPlaying") == "Sungba", "the first song");

        app.Run(CommandCatalog.TogglePlaylist);
        Wait.For(() => app.Text("PlaylistQueueCount") == "2 items", "the queue count");
        Assert.Equal("Now playing: Sungba", app.Text("PlaylistQueueNow"));
        Assert.Equal("Up next: Terminator", app.Text("PlaylistQueueNext"));
        Wait.For(() => app.Find("QueueDuration").Current.Name.Length > 0,
            "the current queue row to show a measured duration");

        ((InvokePattern)app.OpenMenu("NewNamedPlaylist", "MediaMenu", "PlaylistsMenu")
            .GetCurrentPattern(InvokePattern.Pattern)).Invoke();
        var name = Wait.Until(() => app.Window.FindFirst(TreeScope.Descendants,
            new PropertyCondition(AutomationElement.AutomationIdProperty, "PlaylistName")))!;
        ((ValuePattern)name.GetCurrentPattern(ValuePattern.Pattern)).SetValue("Mix");
        app.Press("PrimaryButton");
        Wait.For(() => app.LogText.Contains("Made the playlist Mix (2 items)", StringComparison.Ordinal), "the playlist to be saved");

        void OpenEditor() =>
            ((InvokePattern)app.OpenMenu("EditNamed-Mix", "MediaMenu", "PlaylistsMenu", "NamedPlaylist-Mix")
                .GetCurrentPattern(InvokePattern.Pattern)).Invoke();

        AutomationElement[] Entries() => Wait.Until(() =>
        {
            var view = app.Window.FindFirst(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.AutomationIdProperty, "NamedPlaylistEntries"));
            var found = view?.FindAll(TreeScope.Descendants, new PropertyCondition(
                AutomationElement.ControlTypeProperty, ControlType.ListItem));
            return found is null || found.Count == 0 ? null : found.Cast<AutomationElement>().ToArray();
        })!;

        OpenEditor();
        var initial = Entries();
        Assert.Equal(2, initial.Length);
        ((SelectionItemPattern)initial[1].GetCurrentPattern(SelectionItemPattern.Pattern)).Select();
        app.Press("NamedPlaylistMoveUp");
        app.Press("NamedPlaylistRemoveEntry");
        app.Press("CloseButton"); // Nothing is persisted when the dialog is cancelled.

        OpenEditor();
        var unchanged = Entries();
        Assert.Equal(2, unchanged.Length);
        ((SelectionItemPattern)unchanged[0].GetCurrentPattern(SelectionItemPattern.Pattern)).Select();
        app.Press("NamedPlaylistRemoveEntry");
        app.Press("PrimaryButton");
        Wait.For(() => app.LogText.Contains("Updated Mix (1 item)", StringComparison.Ordinal), "the edited playlist to be saved");

        OpenEditor();
        var saved = Entries();
        Assert.Single(saved);
        Assert.Contains("Terminator", saved[0].Current.Name, StringComparison.Ordinal);
        app.Press("CloseButton");

        // Editing a saved list must neither restart the current song nor change its up-next item.
        Assert.Equal("Now playing: Sungba", app.Text("PlaylistQueueNow"));
        Assert.Equal("Up next: Terminator", app.Text("PlaylistQueueNext"));
        Assert.Equal("2 items", app.Text("PlaylistQueueCount"));
        Assert.Equal(0, app.Close());
    }
}
