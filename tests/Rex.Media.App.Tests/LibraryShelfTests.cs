using System.IO;
using System.Windows.Automation;
using Rex.Media.AppCore.Commands;
using Rex.Media.TestKit;

namespace Rex.Media.App.Tests;

[Collection("desktop")]
public sealed class LibraryShelfTests : IDisposable
{
    private readonly string _media = Path.Combine(Path.GetTempPath(), "rexplayer-ui-shelf-" + Guid.NewGuid().ToString("N"));

    public LibraryShelfTests() => Directory.CreateDirectory(_media);

    public void Dispose() => Directory.Delete(_media, recursive: true);

    private static Condition ListItem(string name) => new AndCondition(
        new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem),
        new PropertyCondition(AutomationElement.NameProperty, name));

    [Fact]
    [Capability("LIB-05")]
    public void SeeAllOnTheMostPlayedShelfOpensSongsMostPlayedFirstAndNamesTheOnePlaying()
    {
        // Alphabetically first, but played once; Terminator, played three times, heads the shelf.
        var lonely = Path.Combine(_media, "Lonely At The Top.wav");
        var terminator = Path.Combine(_media, "Terminator.wav");
        foreach (var path in new[] { lonely, terminator })
        {
            File.WriteAllBytes(path, WavBuilder.Pcm(8000, 1, 16, Pcm.Int16(new float[8000 * 30])).Build());
        }

        var root = Path.Combine(Path.GetTempPath(), "rexplayer-ui-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var library = new Rex.Media.Library.MediaLibrary(Rex.Media.Library.RexStore.Open(Path.Combine(root, "library.log")));
        library.AddFolder(_media);
        Assert.Equal(2, library.Scan(_media, new[] { lonely, terminator }.Select(path =>
        {
            var file = new FileInfo(path);
            return new Rex.Media.Library.LibraryFile(path, file.Length, file.LastWriteTimeUtc);
        }).ToList(), _ => Rex.Media.Library.LibraryKind.Music).Added);
        library.Played(lonely);
        for (var play = 0; play < 3; play++)
        {
            library.Played(terminator);
        }

        using var app = AppProcess.Start(root: root);
        app.Run(CommandCatalog.ToggleLibrary);
        Wait.For(() => app.Text("LibraryTitle") == "Home", "the library home");
        app.Press("LibraryShelf-Songs");
        Wait.For(() => app.Text("LibraryTitle") == "Songs", "the songs");

        var list = app.Find("LibraryList");
        var most = Wait.Until(() => list.FindFirst(TreeScope.Descendants, ListItem("Terminator")));
        var less = Wait.Until(() => list.FindFirst(TreeScope.Descendants, ListItem("Lonely At The Top")));
        Assert.True(most.Current.BoundingRectangle.Top < less.Current.BoundingRectangle.Top,
            "See all on the most played shelf should open Songs most played first, as the shelf shows them.");

        // The list item a screen reader lands on says what the screen shows: that it is playing.
        ((SelectionItemPattern)less.GetCurrentPattern(SelectionItemPattern.Pattern)).Select();
        app.Press("LibraryPlay");
        Wait.For(() => app.Text("NowPlaying") == "Lonely At The Top", "the song to play");
        Wait.For(() => !app.IsShown("LibraryPane"), "the player to return after the song starts");
        app.Run(CommandCatalog.ToggleLibrary);
        Wait.For(() => app.IsShown("LibraryPane"), "the library to reopen");
        Wait.Until(() => app.Find("LibraryList").FindFirst(TreeScope.Descendants, ListItem("Lonely At The Top, now playing")));
        Assert.Equal(0, app.Close());
    }
}
