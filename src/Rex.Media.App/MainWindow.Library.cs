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
/// background as files come and go, and its views (songs, albums, artists, genres, videos, pictures, what
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
        (LibrarySource.Pictures, "Pictures"),
        (LibrarySource.ContinueWatching, "Continue watching"),
        (LibrarySource.RecentlyPlayed, "Recently played"),
        (LibrarySource.RecentlyAdded, "Recently added"),
        (LibrarySource.Playlists, "Playlists"),
        (LibrarySource.Folders, "Folders"),
    ];

    private readonly List<FileSystemWatcher> _watchers = [];
    private readonly HashSet<string> _changedFolders = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _libraryPictureSlots = new(2, 2);
    private CancellationTokenSource _libraryPictures = new();
    private MediaLibrary? _library;
    private LibraryScanner? _scanner;
    private DispatcherQueueTimer? _libraryRefresh;
    private DispatcherQueueTimer? _libraryChanges;
    private DispatcherQueueTimer? _libraryRescan;
    private LibrarySource _librarySource = LibrarySource.Songs;
    private LibraryGroup? _libraryGroup;
    private string _shownLibrary = "";
    private LibraryLayout? _libraryLayout;

    private enum LibrarySource
    {
        Songs,
        Albums,
        Artists,
        Genres,
        Videos,
        Pictures,
        ContinueWatching,
        RecentlyPlayed,
        RecentlyAdded,
        Playlists,
        Folders,
    }

    private enum LibraryLayout
    {
        Songs,
        Browse,
        Videos,
        Pictures,
        Generic,
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
        _libraryRefresh.Tick += (_, _) => RefreshLibraryAfterChanges();
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
        CancelLibraryPictures();
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
        else
        {
            CancelLibraryPictures();
        }
    }

    /// <summary>
    /// A scan reports after every small durable batch. Keep those writes off the window thread and
    /// rebuild the visible view once the scan has settled instead of sorting thousands of rows over
    /// and over while it is still running.
    /// </summary>
    private void RefreshLibraryAfterChanges()
    {
        if (_scanner?.Status.Length > 0)
        {
            Restart(_libraryRefresh!);
            return;
        }

        ShowLibrary();
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
        LibraryLayout layout;
        if (search.Length > 0)
        {
            title = $"Search: {search}";
            rows = EntryRows(LibraryViews.Search(entries, search));
            layout = LibraryLayout.Songs;
        }
        else if (group is not null)
        {
            // The group again, as the library has it now.
            var fresh = Groups(_librarySource, entries).FirstOrDefault(g => g.Name == group.Name && g.Detail == group.Detail);
            _libraryGroup = fresh;
            title = fresh is null ? group.Name : fresh.Detail is null ? fresh.Name : $"{fresh.Name} by {fresh.Detail}";
            rows = fresh is null ? [] : EntryRows(fresh.Entries);
            layout = LibraryLayout.Songs;
        }
        else
        {
            title = LibrarySourceNames.First(pair => pair.Source == _librarySource).Name;
            rows = _librarySource switch
            {
                LibrarySource.Songs => EntryRows(LibraryViews.Songs(entries)),
                LibrarySource.Albums or LibrarySource.Artists or LibrarySource.Genres => [.. Groups(_librarySource, entries).Select(GroupRow)],
                LibrarySource.Videos => EntryRows(LibraryViews.Videos(entries)),
                LibrarySource.Pictures => EntryRows(LibraryViews.Pictures(entries)),
                LibrarySource.ContinueWatching => EntryRows(LibraryViews.ContinueWatching(entries, _player.LeftAt)),
                LibrarySource.RecentlyPlayed => EntryRows(LibraryViews.RecentlyPlayed(entries)),
                LibrarySource.RecentlyAdded => EntryRows(LibraryViews.RecentlyAdded(entries)),
                LibrarySource.Playlists => [.. _player.NamedPlaylists.Select(p => new LibraryRow(p.Name, null, Count(p.Items.Count, "item"), p))],
                _ => [.. _library.Folders.Select(folder => new LibraryRow(folder, Count(entries.Count(entry => MediaLibrary.Holds(folder, entry.Path)), "file"), "", folder))],
            };
            layout = _librarySource switch
            {
                LibrarySource.Albums or LibrarySource.Artists or LibrarySource.Genres => LibraryLayout.Browse,
                LibrarySource.Videos or LibrarySource.ContinueWatching => LibraryLayout.Videos,
                LibrarySource.Pictures => LibraryLayout.Pictures,
                LibrarySource.Playlists or LibrarySource.Folders => LibraryLayout.Generic,
                _ => LibraryLayout.Songs,
            };
        }

        SetLibraryLayout(layout);
        LibraryTitle.Text = title;
        LibraryBack.Visibility = _libraryGroup is not null && search.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        var folders = source == LibrarySource.Folders;
        LibraryFolderActions.Visibility = folders ? Visibility.Visible : Visibility.Collapsed;
        LibraryPlay.Visibility = LibraryEnqueue.Visibility = folders ? Visibility.Collapsed : Visibility.Visible;
        ShowLibraryEmpty(rows.Count == 0, search.Length > 0, source);

        var shown = layout + "\n" + title + "\n" + string.Join("\n", rows.Select(row => row.Name + "|" + row.Detail + "|" + row.Extra));
        if (shown != _shownLibrary)
        {
            CancelLibraryPictures();
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
            : noFolders && source is not LibrarySource.Playlists ? "The library shows the music, videos and pictures in the folders you choose, and keeps up as files come and go."
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
        foreach (var (name, folder) in new[] { ("Music", Environment.SpecialFolder.MyMusic), ("Videos", Environment.SpecialFolder.MyVideos), ("Pictures", Environment.SpecialFolder.MyPictures) })
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

    private static LibraryRow GroupRow(LibraryGroup group) => new(group.Name, group.Detail, Count(group.Entries.Count, "song"), group, hasPicture: true, picturePlaceholder: "♪");

    private List<LibraryRow> EntryRows(IEnumerable<LibraryEntry> entries) => [.. entries.Select(EntryRow)];

    private LibraryRow EntryRow(LibraryEntry entry)
    {
        var episode = entry.Kind == LibraryKind.Video ? LibraryViews.EpisodeOf(entry) : null;
        var name = episode is not null && entry.Title == MediaLibrary.TitleOf(entry.Path) ? episode.DisplayName : entry.Title;
        var detail = entry.Kind == LibraryKind.Video
            ? (_player.LeftAt(entry.Path) is { } at ? "Stopped at " + TimeText.Format(at, entry.Duration ?? TimeSpan.Zero) + " · " : "")
                + (episode is null ? "" : $"Season {episode.Season} · Episode {episode.Episode} · ")
                + Path.GetFileName(Path.GetDirectoryName(entry.Path))
            : entry.Kind == LibraryKind.Picture
                ? string.Join(" · ", new[] { Path.GetFileName(Path.GetDirectoryName(entry.Path)), entry.Modified == default ? null : entry.Modified.ToLocalTime().ToString("d", CultureInfo.CurrentCulture) }.Where(part => part is not null))
                : string.Join(" · ", new[] { entry.Artist, entry.Album }.Where(part => part is not null));
        var extra = entry.Kind != LibraryKind.Picture && entry.Duration is { } duration ? TimeText.Format(duration, duration) : "";
        var badge = entry.Kind switch
        {
            LibraryKind.Music when entry.Track is { } track => entry.Disc is > 1 ? $"{entry.Disc}.{track}" : track.ToString(CultureInfo.CurrentCulture),
            LibraryKind.Video when episode is not null => $"S{episode.Season:00} E{episode.Episode:00}",
            _ => "",
        };
        var placeholder = entry.Kind switch { LibraryKind.Music => "♪", LibraryKind.Video => "▶", _ => "▧" };
        return new LibraryRow(name, detail, extra, entry, hasPicture: true, picturePlaceholder: placeholder, badge: badge);
    }

    private static string Count(int count, string what) => count == 1 ? "1 " + what : count.ToString(CultureInfo.CurrentCulture) + " " + what + "s";

    /// <summary>A visible media line asks for its picture once; recycled, off-screen rows ask for nothing.</summary>
    private void OnLibraryRowShown(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (!args.InRecycleQueue && args.Item is LibraryRow { HasPicture: true, PictureAsked: false } row
            && PictureEntry(row.Item) is { } entry)
        {
            row.PictureAsked = true;
            _ = LoadPictureAsync(row, entry.Path, entry.Kind, _libraryPictures.Token);
        }
    }

    private static LibraryEntry? PictureEntry(object item) => item switch
    {
        LibraryEntry entry => entry,
        LibraryGroup { Entries.Count: > 0 } group => group.Entries[0],
        _ => null,
    };

    /// <summary>Gives each medium its own density and visual hierarchy without giving up ListView virtualisation.</summary>
    private void SetLibraryLayout(LibraryLayout layout)
    {
        if (_libraryLayout == layout)
        {
            return;
        }

        var (template, panel, accessibleName) = layout switch
        {
            LibraryLayout.Songs => ("LibrarySongTemplate", "LibraryListPanel", "Songs, compact list"),
            LibraryLayout.Browse => ("LibraryBrowseTemplate", "LibraryBrowsePanel", "Collections, cover grid"),
            LibraryLayout.Videos => ("LibraryVideoTemplate", "LibraryVideoPanel", "Videos, thumbnail grid"),
            LibraryLayout.Pictures => ("LibraryPhotoTemplate", "LibraryPhotoPanel", "Pictures, gallery grid"),
            _ => ("LibraryGenericTemplate", "LibraryListPanel", "Library items, list"),
        };
        LibraryList.ItemTemplate = (DataTemplate)Root.Resources[template];
        LibraryList.ItemsPanel = (ItemsPanelTemplate)Root.Resources[panel];
        AutomationProperties.SetName(LibraryList, accessibleName);
        _libraryLayout = layout;
    }

    /// <summary>
    /// Reads at most two shell thumbnails at once, away from the window thread. Some shell codecs
    /// take tens of seconds on damaged or unsupported video, so each request also has a short bound
    /// and every request is cancelled when its view goes away.
    /// </summary>
    private async Task LoadPictureAsync(LibraryRow row, string path, LibraryKind kind, CancellationToken viewToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(viewToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(2));
        var token = timeout.Token;
        var entered = false;
        try
        {
            await _libraryPictureSlots.WaitAsync(token);
            entered = true;
            var mode = kind switch
            {
                LibraryKind.Music => ThumbnailMode.MusicView,
                LibraryKind.Picture => ThumbnailMode.PicturesView,
                _ => ThumbnailMode.VideosView,
            };
            var size = kind == LibraryKind.Music ? 192u : 256u;
            using var thumbnail = await Task.Run(async () =>
            {
                var file = await StorageFile.GetFileFromPathAsync(path).AsTask(token);
                return await file.GetThumbnailAsync(mode, size, ThumbnailOptions.UseCurrentScale).AsTask(token);
            }, token);
            if (thumbnail is null || thumbnail.Size == 0)
            {
                return;
            }

            var image = new BitmapImage();
            await image.SetSourceAsync(thumbnail);
            token.ThrowIfCancellationRequested();
            row.Picture = image;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // A slow codec or a row that left the view keeps its placeholder; neither is an error.
        }
        catch (Exception ex) when (ex is COMException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            App.Log.Debug(LogSource, $"No picture for {path}: {ex.Message}");
        }
        finally
        {
            if (entered)
            {
                _libraryPictureSlots.Release();
            }
        }
    }

    private void CancelLibraryPictures()
    {
        _libraryPictures.Cancel();
        _libraryPictures.Dispose();
        _libraryPictures = new CancellationTokenSource();
    }

    /// <summary>The lines chosen, or every line when none is.</summary>
    private List<LibraryRow> ChosenRows()
    {
        var rows = LibraryList.ItemsSource as List<LibraryRow> ?? [];
        var chosen = LibraryList.SelectedItems.OfType<LibraryRow>().ToList();
        return chosen.Count > 0 ? [.. rows.Where(chosen.Contains)] : rows;
    }

    /// <summary>The media the lines stand for, in order: an album's songs, or a folder's media by kind.</summary>
    private List<LibraryEntry> EntriesOf(IEnumerable<LibraryRow> rows) =>
        [.. rows.SelectMany(row => row.Item switch
        {
            LibraryEntry entry => [entry],
            LibraryGroup group => group.Entries,
            string folder => [.. LibraryViews.Songs(_library!.Entries.Where(entry => MediaLibrary.Holds(folder, entry.Path))), .. LibraryViews.Videos(_library.Entries.Where(entry => MediaLibrary.Holds(folder, entry.Path))), .. LibraryViews.Pictures(_library.Entries.Where(entry => MediaLibrary.Holds(folder, entry.Path)))],
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
                var view = entry.Kind == LibraryKind.Video
                    ? LibraryViews.AutoplayVideos(_library!.Entries, entry)
                    : EntriesOf(LibraryList.ItemsSource as List<LibraryRow> ?? []);
                Play(view, view.ToList().FindIndex(item => string.Equals(item.Path, entry.Path, StringComparison.OrdinalIgnoreCase)));
                if (view.Count > 1 && LibraryViews.EpisodeOf(entry) is { } episode)
                {
                    Say($"Playing {episode.Series}, season {episode.Season}, from episode {episode.Episode}");
                }

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

    /// <summary>Plays from the library; visual media closes the library so its picture shows.</summary>
    private void Play(IReadOnlyList<LibraryEntry> entries, int start)
    {
        if (entries.Count == 0)
        {
            Say("There is nothing here to play yet.");
            return;
        }

        _player.PlayFromLibrary(entries, start);
        if (entries[Math.Clamp(start, 0, entries.Count - 1)].Kind is LibraryKind.Video or LibraryKind.Picture)
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

        if (chosen is [{ Item: LibraryEntry { Kind: LibraryKind.Video } video }]
            && LibraryViews.AutoplayVideos(_library!.Entries, video) is { Count: > 1 } season
            && LibraryViews.EpisodeOf(video) is { } episode)
        {
            menu.Items.Add(MenuItem($"Play season {episode.Season} from here ({Count(season.Count, "episode")})", "LibraryMenuPlay", PlayChosen));
            menu.Items.Add(MenuItem("Play only this episode", null, () => Play([video], 0)));
        }
        else
        {
            menu.Items.Add(MenuItem("Play", "LibraryMenuPlay", PlayChosen));
        }

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
