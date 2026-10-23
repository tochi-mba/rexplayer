using System.Windows.Automation;
using Rex.Media.AppCore.Commands;
using Rex.Media.TestKit;

namespace Rex.Media.App.Tests;

/// <summary>Real Windows UI Automation of saved playlist editing and the queue's semantic summary.</summary>
public sealed partial class AppWindowTests
{
    [Fact]
    [Capability("FMT-C19")]
    public void PictureSlideshowHasDiscoverableIntervalAndKeepsCurrentPictureWhenItChanges()
    {
        var picture = RepoPaths.Combine("tests", "fixtures", "picture", "still.png");
        using var app = AppProcess.Start([picture]);
        Wait.For(() => app.IsShown("PictureIntervalButton"), "slideshow interval to appear");
        Assert.Contains("5 s", app.Text("PictureIntervalButton"), StringComparison.Ordinal);
        var before = app.Text("NowPlaying");
        app.Press("PictureIntervalButton");
        app.Press("PictureInterval-10");
        Wait.For(() => app.Text("PictureIntervalButton").Contains("10 s", StringComparison.Ordinal),
            "new slideshow duration");
        Assert.Equal(before, app.Text("NowPlaying"));
        Assert.Equal(10, app.SavedSettings.PictureSeconds);
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
