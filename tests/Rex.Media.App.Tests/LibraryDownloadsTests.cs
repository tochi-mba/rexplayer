using System.Buffers.Binary;
using System.IO;
using System.Windows.Automation;
using Rex.Media.TestKit;

namespace Rex.Media.App.Tests;

/// <summary>
/// Files still downloading into a watched folder (LIB-05): a video written in two parts during the
/// test wears the download badge and says how far it is, the Download filter keeps or hides it,
/// Home's Downloading shelf shows it until it is whole, and Play on a file too short to open says
/// it is still downloading instead of failing.
/// </summary>
[Collection("desktop")]
public sealed class LibraryDownloadsTests : IDisposable
{
    private const int Part = 6000;

    private readonly LibraryScene _scene = new();

    public void Dispose() => _scene.Dispose();

    private static byte[] Fixture => File.ReadAllBytes(RepoPaths.Combine("tests", "fixtures", "mp4", "h264-aac.mp4"));

    /// <summary>The first part of the fixture: its movie header whole, its media cut short, as a download leaves it.</summary>
    private string Growing()
    {
        var path = Path.Combine(_scene.Media, "Growing.mp4");
        File.WriteAllBytes(path, Fixture[..Part]);
        return path;
    }

    private static bool Shows(AppProcess app, string list, string name) =>
        app.IsShown(list) && app.Find(list).FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.NameProperty, name)) is not null;

    private static bool HasBadge(AppProcess app, string list) =>
        app.IsShown(list) && app.Find(list).FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, "LibraryDownloadBadge")) is not null;

    [Fact]
    [Capability("LIB-05")]
    public void AGrowingVideoWearsTheBadgeUntilWholeAndTheFilterAndShelfFollowIt()
    {
        var growing = Growing();
        _scene.Video("Finished.mp4");
        using var app = _scene.Watched().Start();

        // Home: the Downloading shelf, the badge and how far it is.
        var detail = "Downloading · 6 KB of 11 KB";
        Wait.For(() => Shows(app, "LibraryShelfCards-Downloading", detail), "the Downloading shelf with the partial video");
        Assert.True(HasBadge(app, "LibraryShelfCards-Downloading"), "a partial file's card should wear the download badge");
        Screenshots.Match(app, "library-card-downloading", "LibraryShelfCards-Downloading");

        // Videos: the Download filter keeps only what is still arriving, or only what is whole.
        app.Visit("All videos");
        app.Pick("LibraryLook", "List");
        Wait.For(() => app.Lines().Length == 2, "both videos", () => string.Join(", ", app.Lines()));
        Wait.For(() => HasBadge(app, "LibraryList"), "the badge on the partial video's line");
        app.Filter("LibraryFilterDownload", "Downloading");
        Wait.For(() => app.Lines().Length == 1 && Shows(app, "LibraryList", detail), "only the partial video", () => string.Join(", ", app.Lines()));
        Assert.Equal("Filter: Still downloading", app.Text("LibraryFilter"));
        Screenshots.Match(app, "library-video-partial-downloading", "LibraryPane");
        app.Filter("LibraryFilterDownload", "Complete");
        Wait.For(() => app.Lines().Length == 1 && !HasBadge(app, "LibraryList"), "only the whole video", () => string.Join(", ", app.Lines()));

        // The rest arrives: the badge goes, the filter now hides nothing whole, and the shelf leaves Home.
        using (var stream = new FileStream(growing, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
        {
            stream.Write(Fixture.AsSpan(Part));
        }

        Wait.For(() => app.Lines().Length == 2, "the finished download to count as whole", () => string.Join(", ", app.Lines()));
        Assert.False(HasBadge(app, "LibraryList"), "a whole file shows nothing extra");
        app.Filter("LibraryFilterDownload", "Downloading");
        Wait.For(() => app.IsShown("LibraryEmptyClearFilters"), "the empty view to offer Clear filters");
        Assert.Contains("Still downloading", app.Text("LibraryEmptyText"), StringComparison.Ordinal);
        app.Press("LibraryEmptyClearFilters");
        Wait.For(() => app.Lines().Length == 2, "Clear filters to bring both back");
        app.Visit("Home");
        Wait.For(() => !app.IsShown("LibraryShelfCards-Downloading"), "the Downloading shelf to go once nothing is partial");
        Assert.Equal(0, app.Close());
    }

    [Fact]
    [Capability("LIB-05")]
    public void SeeAllOnTheDownloadingShelfOpensAllVideosFilteredToIt()
    {
        Growing();
        _scene.Video("Finished.mp4");
        using var app = _scene.Watched().Start();
        Wait.For(() => app.IsShown("LibraryShelf-Downloading"), "the Downloading shelf");

        app.Press("LibraryShelf-Downloading");
        Wait.For(() => app.Text("LibraryTitle") == "All videos", "the All videos view");
        Wait.For(() => app.Text("LibraryFilter") == "Filter: Still downloading", "the view filtered to Still downloading");
        Wait.For(() => app.Lines().Length == 1, "only the partial video", () => string.Join(", ", app.Lines()));
        Assert.Equal(0, app.Close());
    }

    [Fact]
    [Capability("LIB-05")]
    public void PlayOnAFileTooShortToOpenSaysHowFarItsDownloadIs()
    {
        // aria2 says the file will be 100 bytes; twenty have arrived, not even its first box.
        var clip = Path.Combine(_scene.Media, "Clip.mp4");
        File.WriteAllBytes(clip, Fixture[..20]);
        var control = new byte[10 + 20 + 4 + 8 + 8 + 4];
        BinaryPrimitives.WriteUInt16BigEndian(control, 1);
        BinaryPrimitives.WriteUInt32BigEndian(control.AsSpan(6), 20);
        BinaryPrimitives.WriteUInt64BigEndian(control.AsSpan(34), 100);
        File.WriteAllBytes(clip + ".aria2", control);
        using var app = _scene.Watched().Start();

        app.Visit("All videos");
        app.Pick("LibraryLook", "List");
        app.Choose("Clip");
        app.Press("LibraryPlay");
        Wait.For(() => app.IsShown("LibraryNotice") && app.Find("LibraryNotice").FindAll(TreeScope.Subtree, Condition.TrueCondition).Cast<AutomationElement>()
            .Any(element => element.Current.Name.Contains("Still downloading: 20% so far", StringComparison.Ordinal)), "the still-downloading notice in the library");
        Assert.True(app.IsShown("LibraryPane"), "the library should stay open rather than show a player that failed");
        Screenshots.Match(app, "library-video-partial-play-notice", "LibraryPane");
        Assert.Equal(0, app.Close());
    }
}
