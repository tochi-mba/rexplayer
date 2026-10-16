using System.IO;
using System.Windows.Automation;
using Rex.Media.AppCore.Commands;
using Rex.Media.TestKit;

namespace Rex.Media.App.Tests;

[Collection("desktop")]
public sealed class RecentlyPlayedTests : IDisposable
{
    private readonly string _media = Path.Combine(Path.GetTempPath(), "rexplayer-ui-recent-" + Guid.NewGuid().ToString("N"));

    public RecentlyPlayedTests() => Directory.CreateDirectory(_media);

    public void Dispose() => Directory.Delete(_media, recursive: true);

    [Fact]
    [Capability("LIB-05")]
    public void ShowsTheNewestPlayFirstWithoutMusicOnlySortChoices()
    {
        var olderPath = Path.Combine(_media, "Alpha.wav");
        var newerPath = Path.Combine(_media, "Zulu.wav");
        File.WriteAllBytes(olderPath, WavBuilder.Pcm(8000, 1, 16, Pcm.Int16(new float[8000])).Build());
        File.WriteAllBytes(newerPath, WavBuilder.Pcm(8000, 1, 16, Pcm.Int16(new float[8000])).Build());

        var root = Path.Combine(Path.GetTempPath(), "rexplayer-ui-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var library = new Rex.Media.Library.MediaLibrary(Rex.Media.Library.RexStore.Open(Path.Combine(root, "library.log")));
        library.AddFolder(_media);
        var files = new[] { olderPath, newerPath }.Select(path =>
        {
            var file = new FileInfo(path);
            return new Rex.Media.Library.LibraryFile(path, file.Length, file.LastWriteTimeUtc);
        }).ToList();
        Assert.Equal(2, library.Scan(_media, files, _ => Rex.Media.Library.LibraryKind.Music).Added);
        library.Played(olderPath);
        Thread.Sleep(20);
        library.Played(newerPath);

        using var app = AppProcess.Start(root: root);
        app.Run(CommandCatalog.ToggleLibrary);
        Wait.For(() => app.Text("LibraryTitle") == "Home", "the library home");
        var recent = Wait.Until(() => app.Find("LibrarySources").FindFirst(TreeScope.Descendants,
            new PropertyCondition(AutomationElement.NameProperty, "Recently played")));
        ((SelectionItemPattern)recent!.GetCurrentPattern(SelectionItemPattern.Pattern)).Select();
        Wait.For(() => app.Text("LibraryTitle") == "Recently played", "the recently played view");

        var list = app.Find("LibraryList");
        var newer = Wait.Until(() => list.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.NameProperty, "Zulu")));
        var older = Wait.Until(() => list.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.NameProperty, "Alpha")));
        Assert.True(newer.Current.BoundingRectangle.Top < older.Current.BoundingRectangle.Top,
            "Recently played should preserve newest-first playback order.");
        Assert.False(app.IsShown("LibrarySort"), "A mixed-media recent list should not show music-only sort choices.");
        Assert.Equal(0, app.Close());
    }
}
