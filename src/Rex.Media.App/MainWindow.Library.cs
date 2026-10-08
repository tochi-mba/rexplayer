using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using Rex.Media.AppCore.Library;
using Rex.Media.AppCore.Player;
using Rex.Media.Library;
using Windows.Storage;
using Windows.Storage.FileProperties;
using Windows.Storage.Pickers;
using Launcher = Windows.System.Launcher;
using VirtualKey = Windows.System.VirtualKey;

namespace Rex.Media.App;

/// <summary>
/// The media library in the window (LIB-05): the folders it watches, kept up to date in the
/// background as files come and go, and its views (songs, albums, artists, genres, videos, what
/// was played or added lately, videos left part-way, named playlists and the folders), with a
/// search across it all. It opens over the picture (Ctrl+Shift+L) and what plays carries on.
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>How often every folder is looked through again, for changes the watching missed.</summary>
    private static readonly TimeSpan LibraryRescanEvery = TimeSpan.FromMinutes(30);

    private static readonly (LibrarySource Source, string Name)[] LibrarySourceNames =
    [
        (LibrarySource.Songs, "Songs"),
        (LibrarySource.Albums, "Albums"),
        (LibrarySource.Artists, "Artists"),
        (LibrarySource.Genres, "Genres"),
        (LibrarySource.Videos, "Videos"),
        (LibrarySource.ContinueWatching, "Continue watching"),
        (LibrarySource.RecentlyPlayed, "Recently played"),
        (LibrarySource.RecentlyAdded, "Recently added"),
        (LibrarySource.Playlists, "Playlists"),
        (LibrarySource.Folders, "Folders"),
    ];

    private readonly List<FileSystemWatcher> _watchers = [];
    private readonly HashSet<string> _changedFolders = new(StringComparer.OrdinalIgnoreCase);
    private MediaLibrary? _library;
    private LibraryScanner? _scanner;
    private DispatcherQueueTimer? _libraryRefresh;
    private DispatcherQueueTimer? _libraryChanges;
    private DispatcherQueueTimer? _libraryRescan;
    private LibrarySource _librarySource = LibrarySource.Songs;
    private LibraryGroup? _libraryGroup;
    private string _shownLibrary = "";

    private enum LibrarySource
    {
        Songs,
        Albums,
        Artists,
        Genres,
        Videos,
        ContinueWatching,
        RecentlyPlayed,
        RecentlyAdded,
        Playlists,
        Folders,
    }

    private bool LibraryOpen => LibraryPane.Visibility == Visibility.Visible;

    /// <summary>The library's own store beside the other; when it cannot be read, one in memory.</summary>
    private static RexStore OpenLibraryStore()
    {
        var path = Path.Combine(App.DataRoot, "library.log");
        try
        {
            return RexStore.Open(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            App.Log.Error(LogSource, "The library could not be read, so it starts empty this time: " + ex.Message);
            return RexStore.InMemory();
        }
    }

    private void WireLibrary()
    {
        _library = new MediaLibrary(OpenLibraryStore());
        _player.Library = _library;
        _scanner = new LibraryScanner(_library, LibraryScanner.ListFolder, LibraryScanner.ProbeFile, App.Log);
        _scanner.StatusChanged += (_, _) => DispatcherQueue.TryEnqueue(ShowLibraryStatus);

        // A burst of changes (a scan's batches) shows once.
        _libraryRefresh = DispatcherQueue.CreateTimer();
        _libraryRefresh.Interval = TimeSpan.FromMilliseconds(400);
        _libraryRefresh.IsRepeating = false;
        _libraryRefresh.Tick += (_, _) => ShowLibrary();
        _library.Changed += (_, _) => DispatcherQueue.TryEnqueue(() => Restart(_libraryRefresh));

        // Files copied in arrive in many events; the folder is looked at once they settle.
        _libraryChanges = DispatcherQueue.CreateTimer();
        _libraryChanges.Interval = TimeSpan.FromSeconds(3);
        _libraryChanges.IsRepeating = false;
        _libraryChanges.Tick += (_, _) => LookAtChangedFolders();

        _libraryRescan = DispatcherQueue.CreateTimer();
        _libraryRescan.Interval = LibraryRescanEvery;
        _libraryRescan.Tick += (_, _) => _scanner.Request();
        _libraryRescan.Start();

        LibrarySources.ItemsSource = LibrarySourceNames.Select(source => source.Name).ToList();
        LibrarySources.SelectedIndex = 0;
        var entries = new MenuFlyout();
        entries.Opening += (_, _) => FillLibraryMenu(entries);
        LibraryList.ContextFlyout = entries;
        LibraryList.ContainerContentChanging += OnLibraryRowShown;

        WatchLibraryFolders();
        _scanner.Request();
        App.Log.Info(LogSource, $"The library watches {_library.Folders.Count} folder(s) and knows {_library.Entries.Count} file(s).");
    }

    private void CloseLibrary()
    {
        _libraryRescan?.Stop();
        _libraryChanges?.Stop();
        _libraryRefresh?.Stop();
        foreach (var watcher in _watchers)
        {
            watcher.Dispose();
        }

        _watchers.Clear();
        _scanner?.Dispose();
    }

    private static void Restart(DispatcherQueueTimer timer)
    {
        timer.Stop();
        timer.Start();
    }

    private void SetLibraryOpen(bool open)
    {
        LibraryPane.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        LibraryButton.IsChecked = open;
        if (open)
        {
            _shownLibrary = "";
            ShowLibrary();
            ShowLibraryStatus();
            LibraryList.Focus(FocusState.Programmatic);
        }
    }

    /// <summary>Watches every folder of the library for files coming, going and changing.</summary>
    private void WatchLibraryFolders()
    {
        foreach (var watcher in _watchers)
        {
            watcher.Dispose();
        }

        _watchers.Clear();
        foreach (var folder in _library!.Folders.Where(Directory.Exists))
        {
            try
            {
                var watcher = new FileSystemWatcher(folder)
                {
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
                };
                FileSystemEventHandler changed = (_, _) => FolderChanged(folder);
                watcher.Created += changed;
                watcher.Deleted += changed;
                watcher.Changed += changed;
                watcher.Renamed += (_, _) => FolderChanged(folder);

                // Too many changes at once to list (a whole album copied in): look at all of it.
                watcher.Error += (_, _) => FolderChanged(folder);
                watcher.EnableRaisingEvents = true;
                _watchers.Add(watcher);
            }
            catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
            {
                App.Log.Warning(LogSource, $"{folder} cannot be watched, so it is looked through every {LibraryRescanEvery.TotalMinutes} minutes instead: {ex.Message}");
            }
        }
    }

    /// <summary>On a watcher's thread: notes the folder and looks at it once the changes settle.</summary>
    private void FolderChanged(string folder)
    {
        lock (_changedFolders)
        {
            _changedFolders.Add(folder);
        }

        DispatcherQueue.TryEnqueue(() => Restart(_libraryChanges!));
    }

    private void LookAtChangedFolders()
    {
        List<string> folders;
        lock (_changedFolders)
        {
            folders = [.. _changedFolders];
            _changedFolders.Clear();
        }

        foreach (var folder in folders)
        {
            _scanner!.Request(folder);
        }
    }

    private void ShowLibraryStatus() => LibraryStatus.Text = _scanner?.Status ?? "";

    private void OnLibrarySourceChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LibrarySources.SelectedIndex >= 0)
        {
            _librarySource = LibrarySourceNames[LibrarySources.SelectedIndex].Source;
            _libraryGroup = null;
            LibrarySearch.Text = "";
            ShowLibrary();
        }
    }

    private void OnLibrarySearchChanged(object sender, TextChangedEventArgs e) => ShowLibrary();

    private void OnLibraryBack(object sender, RoutedEventArgs e)
    {
        _libraryGroup = null;
        ShowLibrary();
    }

    /// <summary>Brings the open view in line with the library; the list is replaced only when what it shows has changed.</summary>
    private void ShowLibrary()
    {
        if (_library is null || !LibraryOpen)
        {
            return;
        }

        var entries = _library.Entries;
        var search = LibrarySearch.Text.Trim();
        var source = search.Length > 0 ? (LibrarySource?)null : _librarySource;
        var group = search.Length > 0 ? null : _libraryGroup;
        string title;
        List<LibraryRow> rows;
        if (search.Length > 0)
        {
            title = $"Search: {search}";
            rows = EntryRows(LibraryViews.Search(entries, search));
        }
        else if (group is not null)
        {
            // The group again, as the library has it now.
            var fresh = Groups(_librarySource, entries).FirstOrDefault(g => g.Name == group.Name && g.Detail == group.Detail);
            _libraryGroup = fresh;
            title = fresh is null ? group.Name : fresh.Detail is null ? fresh.Name : $"{fresh.Name} by {fresh.Detail}";
            rows = fresh is null ? [] : EntryRows(fresh.Entries);
        }
        else
        {
            title = LibrarySourceNames.First(pair => pair.Source == _librarySource).Name;
            rows = _librarySource switch
            {
                LibrarySource.Songs => EntryRows(LibraryViews.Songs(entries)),
                LibrarySource.Albums or LibrarySource.Artists or LibrarySource.Genres => [.. Groups(_librarySource, entries).Select(GroupRow)],
                LibrarySource.Videos => EntryRows(LibraryViews.Videos(entries)),
                LibrarySource.ContinueWatching => EntryRows(LibraryViews.ContinueWatching(entries, _player.LeftAt)),
                LibrarySource.RecentlyPlayed => EntryRows(LibraryViews.RecentlyPlayed(entries)),
                LibrarySource.RecentlyAdded => EntryRows(LibraryViews.RecentlyAdded(entries)),
                LibrarySource.Playlists => [.. _player.NamedPlaylists.Select(p => new LibraryRow(p.Name, null, Count(p.Items.Count, "item"), p))],
                _ => [.. _library.Folders.Select(folder => new LibraryRow(folder, Count(entries.Count(entry => MediaLibrary.Holds(folder, entry.Path)), "file"), "", folder))],
            };
        }

        LibraryTitle.Text = title;
        LibraryBack.Visibility = _libraryGroup is not null && search.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        var folders = source == LibrarySource.Folders;
        LibraryFolderActions.Visibility = folders ? Visibility.Visible : Visibility.Collapsed;
        LibraryPlay.Visibility = LibraryEnqueue.Visibility = folders ? Visibility.Collapsed : Visibility.Visible;
        ShowLibraryEmpty(rows.Count == 0, search.Length > 0, source);

        var shown = title + "\n" + string.Join("\n", rows.Select(row => row.Name + "|" + row.Detail + "|" + row.Extra));
        if (shown != _shownLibrary)
        {
            _shownLibrary = shown;
            LibraryList.ItemsSource = rows;
        }
    }

    private void ShowLibraryEmpty(bool empty, bool searching, LibrarySource? source)
    {
        LibraryEmpty.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        LibraryFolderButtons.Children.Clear();
        if (!empty)
        {
            return;
        }

        var noFolders = _library!.Folders.Count == 0;
        LibraryEmptyText.Text = searching ? "Nothing in the library matches that."
            : noFolders && source is not LibrarySource.Playlists ? "The library shows the music and videos in the folders you choose, and keeps up as files come and go."
            : source switch
            {
                LibrarySource.ContinueWatching => "Videos you stop part-way through wait here.",
                LibrarySource.RecentlyPlayed => "What you play from the library shows here.",
                LibrarySource.Playlists => "Playlists you keep (Media, Playlists) show here.",
                _ => _scanner!.Status.Length > 0 ? "Looking through your folders..." : "There is nothing of this kind in the library's folders.",
            };
        if (searching || !(noFolders || source == LibrarySource.Folders))
        {
            return;
        }

        var add = new Button { Content = "Add a folder..." };
        AutomationProperties.SetAutomationId(add, "LibraryEmptyAddFolder");
        add.Click += OnLibraryAddFolder;
        LibraryFolderButtons.Children.Add(add);

        // The usual places, offered, never added unasked.
        foreach (var (name, folder) in new[] { ("Music", Environment.SpecialFolder.MyMusic), ("Videos", Environment.SpecialFolder.MyVideos) })
        {
            var path = Environment.GetFolderPath(folder);
            if (path.Length > 0 && Directory.Exists(path) && !_library.Folders.Any(known => MediaLibrary.Holds(known, path)))
            {
                var suggestion = new Button { Content = "Add your " + name + " folder" };
                AutomationProperties.SetAutomationId(suggestion, "LibraryAdd" + name);
                ToolTipService.SetToolTip(suggestion, path);
                suggestion.Click += (_, _) => AddLibraryFolder(path);
                LibraryFolderButtons.Children.Add(suggestion);
            }
        }
    }

    private static IReadOnlyList<LibraryGroup> Groups(LibrarySource source, IReadOnlyList<LibraryEntry> entries) => source switch
    {
        LibrarySource.Albums => LibraryViews.Albums(entries),
        LibrarySource.Artists => LibraryViews.Artists(entries),
        LibrarySource.Genres => LibraryViews.Genres(entries),
        _ => [],
    };

    private static LibraryRow GroupRow(LibraryGroup group) => new(group.Name, group.Detail, Count(group.Entries.Count, "song"), group);

    private List<LibraryRow> EntryRows(IEnumerable<LibraryEntry> entries) => [.. entries.Select(EntryRow)];

    private LibraryRow EntryRow(LibraryEntry entry)
    {
        var detail = entry.Kind == LibraryKind.Video
            ? (_player.LeftAt(entry.Path) is { } at ? "Stopped at " + TimeText.Format(at, entry.Duration ?? TimeSpan.Zero) + " · " : "") + Path.GetFileName(Path.GetDirectoryName(entry.Path))
            : string.Join(" · ", new[] { entry.Artist, entry.Album }.Where(part => part is not null));
        var extra = entry.Duration is { } duration ? TimeText.Format(duration, duration) : "";
        return new LibraryRow(entry.Title, detail, extra, entry, hasPicture: entry.Kind == LibraryKind.Video);
    }

    private static string Count(int count, string what) => count == 1 ? "1 " + what : count.ToString(CultureInfo.CurrentCulture) + " " + what + "s";

    /// <summary>A video's line, as it scrolls into view: its picture from Windows, once.</summary>
    private void OnLibraryRowShown(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (args.Item is LibraryRow { HasPicture: true, PictureAsked: false, Item: LibraryEntry entry } row)
        {
            row.PictureAsked = true;
            _ = LoadPictureAsync(row, entry.Path);
        }
    }

    private static async Task LoadPictureAsync(LibraryRow row, string path)
    {
        try
        {
            var file = await StorageFile.GetFileFromPathAsync(path);
            using var thumbnail = await file.GetThumbnailAsync(ThumbnailMode.VideosView, 192, ThumbnailOptions.UseCurrentScale);
            if (thumbnail is null || thumbnail.Size == 0)
            {
                return;
            }

            var image = new BitmapImage();
            await image.SetSourceAsync(thumbnail);
            row.Picture = image;
        }
        catch (Exception ex) when (ex is COMException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            App.Log.Debug(LogSource, $"No picture for {path}: {ex.Message}");
        }
    }

    /// <summary>The lines chosen, or every line when none is.</summary>
    private List<LibraryRow> ChosenRows()
    {
        var rows = LibraryList.ItemsSource as List<LibraryRow> ?? [];
        var chosen = LibraryList.SelectedItems.OfType<LibraryRow>().ToList();
        return chosen.Count > 0 ? [.. rows.Where(chosen.Contains)] : rows;
    }

    /// <summary>The songs and videos the lines stand for, in order: an album's songs, a folder's files.</summary>
    private List<LibraryEntry> EntriesOf(IEnumerable<LibraryRow> rows) =>
        [.. rows.SelectMany(row => row.Item switch
        {
            LibraryEntry entry => [entry],
            LibraryGroup group => group.Entries,
            string folder => [.. LibraryViews.Songs(_library!.Entries.Where(entry => MediaLibrary.Holds(folder, entry.Path))), .. LibraryViews.Videos(_library.Entries.Where(entry => MediaLibrary.Holds(folder, entry.Path)))],
            _ => [],
        })];

    private void OnLibraryPlay(object sender, RoutedEventArgs e) => PlayChosen();

    private void OnLibraryEnqueue(object sender, RoutedEventArgs e) => EnqueueChosen();

    private void OnLibraryDoubleTapped(object sender, DoubleTappedRoutedEventArgs e) => Activate(LibraryList.SelectedItem as LibraryRow);

    private void OnLibraryKeyDown(object sender, KeyRoutedEventArgs e)
    {
        switch (e.Key)
        {
            case VirtualKey.Enter when LibraryList.SelectedItem is LibraryRow row:
                Activate(row);
                e.Handled = true;
                break;
            case VirtualKey.Back when _libraryGroup is not null:
                OnLibraryBack(sender, e);
                e.Handled = true;
                break;
        }
    }

    /// <summary>A line opened: a group shows its songs, a playlist plays, a song or video plays with the rest of the view after it.</summary>
    private void Activate(LibraryRow? row)
    {
        switch (row?.Item)
        {
            case LibraryGroup group:
                _libraryGroup = group;
                LibrarySearch.Text = "";
                ShowLibrary();
                break;
            case NamedPlaylist playlist:
                _player.PlayPlaylist(playlist.Id);
                break;
            case LibraryEntry entry:
                var view = EntriesOf(LibraryList.ItemsSource as List<LibraryRow> ?? []);
                Play(view, view.IndexOf(entry));
                break;
        }
    }

    private void PlayChosen()
    {
        var chosen = LibraryList.SelectedItems.OfType<LibraryRow>().ToList();
        if (chosen is [{ Item: LibraryEntry }])
        {
            Activate(chosen[0]);
            return;
        }

        if (ChosenRows() is [{ Item: NamedPlaylist playlist }, ..])
        {
            _player.PlayPlaylist(playlist.Id);
            return;
        }

        Play(EntriesOf(ChosenRows()), 0);
    }

    private void EnqueueChosen()
    {
        var rows = ChosenRows();
        foreach (var playlist in rows.Select(row => row.Item).OfType<NamedPlaylist>())
        {
            _player.PlayPlaylist(playlist.Id, enqueue: true);
        }

        var entries = EntriesOf(rows);
        if (entries.Count > 0)
        {
            _player.EnqueueFromLibrary(entries);
            Say($"Added {Count(entries.Count, "item")} to the playlist");
        }
    }

    /// <summary>Plays from the library; a video closes the library so its picture shows.</summary>
    private void Play(IReadOnlyList<LibraryEntry> entries, int start)
    {
        if (entries.Count == 0)
        {
            Say("There is nothing here to play yet.");
            return;
        }

        _player.PlayFromLibrary(entries, start);
        if (entries[Math.Clamp(start, 0, entries.Count - 1)].Kind == LibraryKind.Video)
        {
            SetLibraryOpen(false);
        }
    }

    /// <summary>The list's right-click menu, for the lines chosen when it opens.</summary>
    private void FillLibraryMenu(MenuFlyout menu)
    {
        menu.Items.Clear();
        var chosen = LibraryList.SelectedItems.OfType<LibraryRow>().ToList();
        if (chosen.Count == 0)
        {
            menu.Items.Add(new MenuFlyoutItem { Text = "Choose something first", IsEnabled = false });
            return;
        }

        if (chosen[0].Item is string)
        {
            menu.Items.Add(MenuItem("Play everything in it", "LibraryMenuPlay", PlayChosen));
            menu.Items.Add(MenuItem("Stop watching", "LibraryMenuRemoveFolder", RemoveChosenFolders));
            return;
        }

        if (chosen[0].Item is NamedPlaylist playlist)
        {
            menu.Items.Add(MenuItem("Play", "LibraryMenuPlay", () => _player.PlayPlaylist(playlist.Id)));
            menu.Items.Add(MenuItem("Add to the end of this playlist", null, () => _player.PlayPlaylist(playlist.Id, enqueue: true)));
            menu.Items.Add(new MenuFlyoutSeparator());
            menu.Items.Add(MenuItem("Rename...", null, () => _ = RenamePlaylistAsync(playlist.Id)));
            menu.Items.Add(MenuItem("Duplicate", null, () => _player.DuplicatePlaylist(playlist.Id)));
            menu.Items.Add(MenuItem("Delete...", null, () => _ = DeletePlaylistAsync(playlist.Id)));
            return;
        }

        menu.Items.Add(MenuItem("Play", "LibraryMenuPlay", PlayChosen));
        menu.Items.Add(MenuItem("Add to the end of the playlist", "LibraryMenuEnqueue", EnqueueChosen));
        var items = EntriesOf(chosen).Select(PlayerController.ItemOf).ToList();
        var addTo = new MenuFlyoutSubItem { Text = "Add to a playlist" };
        AutomationProperties.SetAutomationId(addTo, "LibraryMenuAddTo");
        foreach (var named in _player.NamedPlaylists)
        {
            var id = named.Id;
            addTo.Items.Add(MenuItem(named.Name, null, () => _player.AddToPlaylist(id, items)));
        }

        if (addTo.Items.Count > 0)
        {
            addTo.Items.Add(new MenuFlyoutSeparator());
        }

        addTo.Items.Add(MenuItem("New playlist...", "LibraryMenuAddToNew", () => _ = NewPlaylistAsync(items)));
        menu.Items.Add(addTo);
        if (chosen is [{ Item: LibraryEntry entry }])
        {
            menu.Items.Add(new MenuFlyoutSeparator());
            menu.Items.Add(MenuItem("Show in its folder", null, () => _ = Launcher.LaunchFolderPathAsync(Path.GetDirectoryName(entry.Path)!)));
        }
    }

    private async void OnLibraryAddFolder(object sender, RoutedEventArgs e)
    {
        var picker = Prepared(new FolderPicker { SuggestedStartLocation = PickerLocationId.MusicLibrary });
        picker.FileTypeFilter.Add("*");
        if (await picker.PickSingleFolderAsync() is { } folder)
        {
            AddLibraryFolder(folder.Path);
        }
    }

    private void AddLibraryFolder(string path)
    {
        if (!_library!.AddFolder(path))
        {
            Say($"The library already watches {path}.");
            return;
        }

        App.Log.Info(LogSource, "The library now watches " + path);
        Say("The library now watches " + path);
        WatchLibraryFolders();
        _scanner!.Request();
    }

    private void OnLibraryRemoveFolder(object sender, RoutedEventArgs e) => RemoveChosenFolders();

    private void RemoveChosenFolders()
    {
        var folders = LibraryList.SelectedItems.OfType<LibraryRow>().Select(row => row.Item).OfType<string>().ToList();
        if (folders.Count == 0)
        {
            Say("Choose the folders to stop watching first.");
            return;
        }

        foreach (var folder in folders)
        {
            _library!.RemoveFolder(folder);
            App.Log.Info(LogSource, "The library no longer watches " + folder);
        }

        WatchLibraryFolders();
    }

    private void OnLibraryRescan(object sender, RoutedEventArgs e) => _scanner!.Request();
}
