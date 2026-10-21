using System.IO;
using System.Windows.Automation;
using Rex.Media.AppCore.Commands;
using Rex.Media.Library;
using Rex.Media.Primitives;
using Rex.Media.Settings;
using Rex.Media.TestKit;

namespace Rex.Media.App.Tests;

/// <summary>
/// A library made before the window starts: Asake's songs with their tags, synthetic movie and show
/// folders, and the settings a flow needs. The window tests drive what the library shows, not a
/// background scan's timing, so the files are scanned and their details written here, once.
/// </summary>
internal sealed class LibraryScene : IDisposable
{
    private readonly List<(string Path, LibraryKind Kind, MediaInfo? Info)> _files = [];

    public LibraryScene()
    {
        Directory.CreateDirectory(Media);
        Directory.CreateDirectory(Root);
    }

    /// <summary>The watched folder.</summary>
    public string Media { get; } = Path.Combine(Path.GetTempPath(), "rexplayer-ui-library-" + Guid.NewGuid().ToString("N"));

    /// <summary>The app's data folder.</summary>
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "rexplayer-ui-" + Guid.NewGuid().ToString("N"));

    /// <summary>The three Asake songs every music flow uses, on three albums, with Sungba under its own genre.</summary>
    public LibraryScene WithAsake()
    {
        Song("Lonely At The Top", "Lungu Boy", 2024, "Afrobeats");
        Song("Terminator", "Work of Art", 2023, "Afrobeats");
        Song("Sungba", "Mr. Money with the Vibe", 2022, "Amapiano");
        return this;
    }

    /// <summary>A song tagged with <paramref name="artist"/> (Asake unless said otherwise).</summary>
    public string Song(string title, string album, int year, string genre, string artist = "Asake")
    {
        var path = Path.Combine(Media, title + ".wav");
        File.WriteAllBytes(path, WavBuilder.Pcm(8000, 1, 16, Pcm.Int16(new float[8000 * 20])).Build());
        _files.Add((path, LibraryKind.Music, new MediaInfo
        {
            FormatName = "WAVE",
            Tracks = [new TrackInfo { Id = 1, Codec = CodecId.Pcm }],
            Duration = MediaTime.FromSeconds(20),
            Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [MetadataKeys.Title] = title,
                [MetadataKeys.Artist] = artist,
                [MetadataKeys.Album] = album,
                [MetadataKeys.Date] = year.ToString(System.Globalization.CultureInfo.InvariantCulture),
                [MetadataKeys.Genre] = genre,
            },
        }));
        return path;
    }

    /// <summary>
    /// A short real video at <paramref name="relative"/> under the watched folder, which names what it is,
    /// copied from <paramref name="fixture"/> in tests/fixtures/mp4. With a <paramref name="length"/>, the
    /// library records it as read with that running time, so the rules that weigh length (a film lasts
    /// 40 minutes or more) see it; without one, the window reads the fixture's own few seconds.
    /// </summary>
    public string Video(string relative, string fixture = "h264-aac.mp4", TimeSpan? length = null)
    {
        var path = Path.Combine(Media, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.Copy(RepoPaths.Combine("tests", "fixtures", "mp4", fixture), path);
        _files.Add((path, LibraryKind.Video, length is { } running ? VideoInfo(running) : null));
        return path;
    }

    /// <summary>A film for Movies: a release-style name at <paramref name="relative"/>, recorded at a feature's length.</summary>
    public string Film(string relative, string fixture = "h264-aac.mp4") => Video(relative, fixture, TimeSpan.FromHours(2));

    /// <summary>What reading an H.264 and AAC video of <paramref name="length"/> finds.</summary>
    public static MediaInfo VideoInfo(TimeSpan length) => new()
    {
        FormatName = "MPEG-4",
        Tracks = [new TrackInfo { Id = 1, Codec = CodecId.H264 }, new TrackInfo { Id = 2, Codec = CodecId.Aac }],
        Duration = MediaTime.FromSeconds(length.TotalSeconds),
    };

    public string Picture(string name)
    {
        var path = Path.Combine(Media, name);
        File.Copy(RepoPaths.Combine("tests", "fixtures", "picture", "still.jpg"), path);
        _files.Add((path, LibraryKind.Picture, null));
        return path;
    }

    /// <summary>
    /// Writes the library: the folder watched, every file scanned and read, then
    /// <paramref name="played"/> counted as plays. The settings start from <paramref name="settings"/>.
    /// </summary>
    public LibraryScene Ready(Func<PlayerSettings, PlayerSettings>? settings = null, params string[] played)
    {
        var library = new MediaLibrary(RexStore.Open(Path.Combine(Root, "library.log")));
        library.AddFolder(Media);
        var kinds = _files.ToDictionary(file => file.Path, file => file.Kind, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(_files.Count, library.Scan(Media, _files.Select(file =>
        {
            var info = new FileInfo(file.Path);
            return new LibraryFile(file.Path, info.Length, info.LastWriteTimeUtc);
        }), path => kinds[path]).Added);
        library.SetDetails(_files.Where(file => file.Info is not null).Select(file => (file.Path, file.Info)));
        foreach (var path in played)
        {
            library.Played(path);
        }

        WriteSettings(Root, settings);
        return this;
    }

    /// <summary>Watches the folder without looking through it, so the window's own scan finds the files.</summary>
    public LibraryScene Watched()
    {
        new MediaLibrary(RexStore.Open(Path.Combine(Root, "library.log"))).AddFolder(Media);
        WriteSettings(Root);
        return this;
    }

    /// <summary>Settings for someone who has used rexplayer before, changed by <paramref name="settings"/>.</summary>
    public static void WriteSettings(string root, Func<PlayerSettings, PlayerSettings>? settings = null)
    {
        Directory.CreateDirectory(root);
        var start = new PlayerSettings { FirstRunDone = true, LastSeenVersion = AppProcess.Version, UpdateChecks = UpdateCadence.Off };
        SettingsStore.Save(Path.Combine(root, "settings.json"), settings is null ? start : settings(start));
    }

    public AppProcess Start()
    {
        var app = AppProcess.Start(root: Root);
        app.Run(CommandCatalog.ToggleLibrary);
        Wait.For(() => app.IsShown("LibraryPane") && app.Text("LibraryTitle") == "Home", "the library's home");
        return app;
    }

    public void Dispose()
    {
        foreach (var folder in new[] { Media, Root })
        {
            try
            {
                Directory.Delete(folder, recursive: true);
            }
            catch (IOException)
            {
                // A file the closing player still holds is left for the temporary folder's cleanup.
            }
        }
    }
}

/// <summary>Moving about the library the way a person does, through UI Automation.</summary>
internal static class LibraryNavigation
{
    /// <summary>Chooses a view in the sidebar and waits for its title.</summary>
    public static void Visit(this AppProcess app, string view, string? title = null)
    {
        var source = Wait.Until(() => app.Find("LibrarySources").FindFirst(TreeScope.Descendants,
            new PropertyCondition(AutomationElement.NameProperty, view)));
        ((SelectionItemPattern)source.GetCurrentPattern(SelectionItemPattern.Pattern)).Select();
        // The streaming browse of Movies and TV shows has a billboard in place of the title.
        Wait.For(() => (app.IsShown("LibraryTitle") && app.Text("LibraryTitle") == (title ?? view))
            || (view is "Movies" or "TV Shows" && app.IsShown("LibraryBillboard")), view + " to open");
    }

    /// <summary>The line named <paramref name="name"/> in the list, once it is there.</summary>
    public static AutomationElement Line(this AppProcess app, string name, string list = "LibraryList") =>
        Wait.Until(() => app.Find(list).FindFirst(TreeScope.Descendants, new AndCondition(
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem),
            new PropertyCondition(AutomationElement.NameProperty, name))));

    /// <summary>The names of the list's lines from top to bottom, as far as they are realised.</summary>
    public static string[] Lines(this AppProcess app, string list = "LibraryList")
    {
        try
        {
            return [.. app.Find(list).FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem))
                .Cast<AutomationElement>().OrderBy(line => line.Current.BoundingRectangle.Top).ThenBy(line => line.Current.BoundingRectangle.Left)
                .Select(line => line.Current.Name)];
        }
        catch (ElementNotAvailableException)
        {
            // The list was rebuilt while it was read; a polling wait reads the new one.
            return [];
        }
    }

    public static void Choose(this AppProcess app, string name, bool alone = true)
    {
        var line = (SelectionItemPattern)app.Line(name).GetCurrentPattern(SelectionItemPattern.Pattern);
        if (alone)
        {
            line.Select();
        }
        else
        {
            line.AddToSelection();
        }
    }

    public static bool IsChosen(this AppProcess app, string name) =>
        ((SelectionItemPattern)app.Line(name).GetCurrentPattern(SelectionItemPattern.Pattern)).Current.IsSelected;

    /// <summary>Opens a line with a double click: a group shows what it holds, media plays.</summary>
    public static void Open(this AppProcess app, string name) => Pointer.DoubleClick(app, app.Line(name));

    /// <summary>Opens one of the view's option menus (layout, order, groups) and picks a choice in it.</summary>
    public static void Pick(this AppProcess app, string menu, string choice)
    {
        app.Press(menu);
        app.Press(menu + "-" + choice);
    }

    /// <summary>Opens the filter menu, one of its submenus, and picks a choice in it.</summary>
    public static void Filter(this AppProcess app, string group, string choice)
    {
        app.Press("LibraryFilter");
        ((ExpandCollapsePattern)app.Find(group).GetCurrentPattern(ExpandCollapsePattern.Pattern)).Expand();
        app.Press(group + "-" + choice);
    }

    /// <summary>The playlist pane's items from top to bottom.</summary>
    public static string[] Queue(this AppProcess app)
    {
        if (!app.IsShown("PlaylistView"))
        {
            app.Toggle("PlaylistButton");
            Wait.For(() => app.IsShown("PlaylistView"), "the playlist pane");
        }

        return [.. app.Find("PlaylistView").FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem))
            .Cast<AutomationElement>().Select(item => item.Current.Name)];
    }
}
