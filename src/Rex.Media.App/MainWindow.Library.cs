using System.Globalization;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Rex.Media.AppCore.Library;
using Rex.Media.AppCore.Player;
using Rex.Media.Library;
using Rex.Media.Settings;
using Windows.ApplicationModel.DataTransfer;
using Microsoft.VisualBasic.FileIO;
using Windows.Storage.Pickers;
using Launcher = Windows.System.Launcher;
using VirtualKey = Windows.System.VirtualKey;

namespace Rex.Media.App;

/// <summary>
/// The media library in the window (LIB-05): the folders it watches, kept up to date in the
/// background as files come and go, and its views (songs, albums, artists, genres, videos, pictures, what
/// was played or added lately, videos left part-way, named playlists and the folders), with a
/// search across it all. It opens over the picture (Ctrl+Shift+L); playback follows the user's pause preference.
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>How often every folder is looked through again, for changes the watching missed.</summary>
    private static readonly TimeSpan LibraryRescanEvery = TimeSpan.FromMinutes(30);

    private readonly List<FileSystemWatcher> _watchers = [];
    private readonly HashSet<string> _changedFolders = new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource _libraryPictures = new();
    private CancellationTokenSource _hoverPictures = new();
    private MediaLibrary? _library;
    private LibraryScanner? _scanner;
    private LibrarySectionAnalysis? _backgroundSections;
    private DispatcherQueueTimer? _libraryRefresh;
    private DispatcherQueueTimer? _libraryChanges;
    private DispatcherQueueTimer? _libraryRescan;
    private LibrarySource _librarySource = LibrarySource.Home;
    private LibraryGroup? _libraryGroup;

    /// <summary>The group opened inside <see cref="_libraryGroup"/>: a show's season, or an artist's album.</summary>
    private LibraryGroup? _librarySeason;
    private string _shownLibrary = "";
    private (bool Searching, LibrarySource? Source, LibraryViewChoice? Choice)? _emptyView;
    private LibraryEmptyState? _shownEmpty;

    private enum LibrarySource
    {
        Home,
        Songs,
        Albums,
        Artists,
        Genres,
        Movies,
        TvShows,
        Videos,
        Pictures,
        ContinueWatching,
        RecentlyPlayed,
        RecentlyAdded,
        Playlists,
        Folders,
    }

    private bool LibraryOpen => LibraryPane.Visibility == Visibility.Visible;

    /// <summary>The library's own store beside the other; when it cannot be read, one in memory.</summary>
    internal static RexStore OpenLibraryStore()
    {
        var path = App.Roots.LibraryStore;
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
        _library = App.Warmup?.TakeLibrary() ?? new MediaLibrary(OpenLibraryStore());
        _player.Library = _library;
        _scanner = new LibraryScanner(_library, LibraryScanner.ListFolder, LibraryScanner.ProbeFile, App.Log);
        _scanner.StatusChanged += (_, _) => OnWindowThread(ShowLibraryStatus);
        _backgroundSections = new LibrarySectionAnalysis(_player.Memory, _player.SectionCache,
            _player.SectionAnalyzer!, App.Log);
        _backgroundSections.SetPaused(_player.IsPlaying);
        _player.Changed += (_, _) => _backgroundSections?.SetPaused(_player.IsPlaying);
        _player.Changed += (_, _) => OnPlayingChangedForDetailWrites();

        // A burst of changes (a scan's batches) shows once.
        _libraryRefresh = CreateTimer();
        _libraryRefresh.Interval = TimeSpan.FromMilliseconds(400);
        _libraryRefresh.IsRepeating = false;
        _libraryRefresh.Tick += (_, _) => RefreshLibraryAfterChanges();
        _library.Changed += (_, _) => OnWindowThread(() => Restart(_libraryRefresh));
        WireDownloadRechecks();

        // Files copied in arrive in many events; the folder is looked at once they settle.
        _libraryChanges = CreateTimer();
        _libraryChanges.Interval = TimeSpan.FromSeconds(3);
        _libraryChanges.IsRepeating = false;
        _libraryChanges.Tick += (_, _) => LookAtChangedFolders();

        _libraryRescan = CreateTimer();
        _libraryRescan.Interval = LibraryRescanEvery;
        _libraryRescan.Tick += (_, _) => _scanner.Request();
        _libraryRescan.Start();

        LibrarySources.ItemsSource = LibrarySourceItems.Select(source => source.Item).ToList();
        WireLibraryView();
        LibrarySources.SelectedIndex = 0;
        var entries = new MenuFlyout();
        entries.Opening += (_, _) => FillLibraryMenu(entries, ChoosePointedLine());
        LibraryList.ContextFlyout = entries;
        LibraryCollage.ContextFlyout = entries;
        LibraryList.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(OnLibraryPointerPressed), handledEventsToo: true);
        LibraryCollage.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(OnLibraryPointerPressed), handledEventsToo: true);
        LibraryList.ContainerContentChanging += OnLibraryRowShown;

        WatchLibraryFolders();
        // Looked through once the launch has settled: a large library's scan reads every folder
        // and competes with the first picture and sound for the disk and the processor.
        var firstScan = CreateTimer();
        firstScan.Interval = Rex.Media.AppCore.LaunchPlan.FirstLibraryScan;
        firstScan.IsRepeating = false;
        firstScan.Tick += (_, _) =>
        {
            _scanner?.Request();
            QueueLibrarySections();
        };
        firstScan.Start();
        App.Log.Info(LogSource, $"The library watches {_library.Folders.Count} folder(s) and knows {_library.Entries.Count} file(s).");
    }

    private void CloseLibrary()
    {
        var sections = _backgroundSections;
        _backgroundSections = null;
        if (sections is not null)
        {
            // A decoder may be cancelling on another thread. Never hold the window open for it.
            _ = Task.Run(sections.Dispose);
        }

        CancelLibraryPictures();
        _libraryRescan?.Stop();
        _libraryChanges?.Stop();
        _libraryRefresh?.Stop();
        foreach (var watcher in _watchers)
        {
            watcher.Dispose();
        }

        _watchers.Clear();
        if (_scanner is { } scanner)
        {
            _scanner = null;
            // Closing a scan can join a slow filesystem worker; never hold up the window or updater.
            _ = Task.Run(scanner.Dispose);
        }
    }

    private static void Restart(DispatcherQueueTimer timer)
    {
        timer.Stop();
        timer.Start();
    }

    private void SetLibraryOpen(bool open)
    {
        var opening = open && !LibraryOpen;
        if (opening && IsFullScreen && _settings.LeaveFullScreenWhenLibraryOpens)
        {
            SetFullScreen(false);
        }

        if (!open && LibraryOpen)
        {
            KeepPlace();
        }

        // Only a transition into the library can pause playback. A paused item stays paused,
        // and closing the library never restarts it or changes the seek position.
        if (opening && LibraryOpening.Decide(_settings.PauseWhenLibraryOpens,
                _settings.KeepAudioPlayingInLibrary, HasVideo, _player.IsPlaying) == LibraryOpenAction.Pause)
        {
            _player.Pause();
        }

        // The first opening of a session starts where the last one ended, before the pane shows.
        if (opening && !_libraryRouteApplied)
        {
            _libraryRouteApplied = true;
            if (_settings.LibraryRoute is { } route)
            {
                ApplyLibraryRoute(route);
            }
        }

        LibraryPane.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        LibraryButton.IsChecked = open;
        if (open)
        {
            if (!opening || !_settings.OpenLibraryAtPlayingItem || !RevealCurrentLibraryItem())
            {
                ShowLibraryAtPlace();
            }

            ResumeLibraryPictures();
            ShowLibraryStatus();
            if (LibraryCollage.Visibility == Visibility.Visible)
            {
                LibraryCollage.Focus(FocusState.Programmatic);
            }
            else
            {
                LibraryList.Focus(FocusState.Programmatic);
            }
        }
        else
        {
            CancelLibraryPictures();
        }
    }

    /// <summary>Opens the narrowest library route containing the current local item.</summary>
    private bool RevealCurrentLibraryItem()
    {
        var located = LibraryViews.LocateCurrent(_library!.Entries, _player.Playlist.Current?.Location);
        if (located is null)
        {
            return false;
        }

        var source = located.Entry.Kind switch
        {
            LibraryKind.Music => LibrarySource.Songs,
            LibraryKind.Picture => LibrarySource.Pictures,
            LibraryKind.Video when located.Show is not null => LibrarySource.TvShows,
            LibraryKind.Video => LibrarySource.Movies,
            _ => LibrarySource.Home,
        };
        var index = Array.FindIndex(LibrarySourceItems, pair => pair.Source == source);
        _places.RememberSearch(source.ToString(), "");
        if (LibrarySources.SelectedIndex != index)
        {
            LibrarySources.SelectedIndex = index;
        }
        else
        {
            _librarySource = source;
        }

        if (LibrarySearch.Text.Length > 0)
        {
            LibrarySearch.Text = "";
        }

        _libraryGroup = located.Show;
        _librarySeason = located.Season;
        _shownLibrary = "";
        ShowLibrary();
        RevealLibraryRow(located.Entry.Path);
        return true;
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

        QueueLibrarySections();
        ShowLibrary();
    }

    /// <summary>Hand a snapshot of library episodes to the low-priority section worker.</summary>
    private void QueueLibrarySections()
    {
        _backgroundSections?.SetQueue(_library!.Entries.Where(entry => entry.Kind == LibraryKind.Video)
            .Select(entry => entry.Path));
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

        OnWindowThread(() => Restart(_libraryChanges!));
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

    private void ShowLibraryStatus()
    {
        LibraryStatus.Text = _scanner?.Status ?? "";

        // A scan that finds nothing new changes no entries, so the view is never rebuilt: an empty
        // one would go on saying it is looking, or never say so.
        if (_emptyView is not null && LibraryOpen)
        {
            ShowLibraryEmptyState();
        }
    }

    /// <summary>
    /// Shows or hides the words of an empty view and the ways out under them. They are kept, so a
    /// scan starting or finishing updates them (its status changes no entries), and the buttons are
    /// rebuilt only when what the view says changes.
    /// </summary>
    private void ShowLibraryEmpty(bool empty, bool searching, LibrarySource? source, LibraryViewChoice? choice = null)
    {
        _emptyView = empty ? (searching, source, choice) : null;
        ShowLibraryEmptyState();
    }

    private void ShowLibraryEmptyState()
    {
        if (_emptyView is not { } empty)
        {
            _shownEmpty = null;
            LibraryEmpty.Visibility = Visibility.Collapsed;
            LibraryFolderButtons.Children.Clear();
            return;
        }

        var (searching, source, choice) = empty;
        var view = source switch
        {
            LibrarySource.ContinueWatching => LibraryEmptyView.ContinueWatching,
            LibrarySource.RecentlyPlayed => LibraryEmptyView.RecentlyPlayed,
            LibrarySource.Playlists => LibraryEmptyView.Playlists,
            LibrarySource.Folders => LibraryEmptyView.Folders,
            _ => LibraryEmptyView.Media,
        };
        var state = LibraryEmptyState.For(view, searching, _library!.Folders.Count > 0,
            choice is { IsFiltered: true } ? LibraryArrangement.FilterLabel(choice) : null, _scanner?.Status.Length > 0);
        LibraryEmpty.Visibility = Visibility.Visible;
        if (state == _shownEmpty)
        {
            return;
        }

        _shownEmpty = state;
        LibraryEmptyText.Text = state.Text;
        LibraryFolderButtons.Children.Clear();
        if (state.OffersClearFilters)
        {
            var clear = new Button { Content = "Clear filters" };
            AutomationProperties.SetAutomationId(clear, "LibraryEmptyClearFilters");
            clear.Click += (_, _) => KeepViewChoice(ViewChoice(_libraryViewKey).Unfiltered());
            LibraryFolderButtons.Children.Add(clear);
        }

        if (!state.OffersFolders)
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
        LibrarySource.TvShows => LibraryViews.TvShows(entries),
        _ => [],
    };

    private LibraryRow GroupRow(LibraryGroup group) => new(group.Name,
        MetadataBlurb(group) is { Length: > 0 } blurb ? (group.Detail is null ? blurb : group.Detail + " · " + blurb) : group.Detail,
        LibraryActions.Count(group.Entries.Count, group.Entries.All(entry => entry.Kind == LibraryKind.Video) ? "episode" : "song"),
        group, hasPicture: true, picturePlaceholder: group.Entries.All(entry => entry.Kind == LibraryKind.Video) ? "▶" : "♪");

    private LibraryRow EntryRow(LibraryEntry entry)
    {
        var episode = entry.Kind == LibraryKind.Video ? LibraryViews.EpisodeOf(entry) : null;
        var name = _librarySource == LibrarySource.Movies && entry.Kind == LibraryKind.Video
            ? LibraryViews.MovieTitleOf(entry)
            : episode is { Episode: not null } && entry.Title == MediaLibrary.TitleOf(entry.Path)
                ? FetchedEpisodeName(episode) ?? episode.DisplayName : entry.Title;
        var detail = entry.Kind == LibraryKind.Video
            ? (_player.LeftAt(entry.Path) is { } at ? "Stopped at " + TimeText.Format(at, entry.Duration ?? TimeSpan.Zero) + " · " : "")
                + (_librarySource == LibrarySource.Movies && LibraryViews.MovieYearOf(entry) is { } year ? year.ToString(CultureInfo.CurrentCulture) + " · " : "")
                + (_librarySource == LibrarySource.Movies && MovieBlurb(entry) is { Length: > 0 } fromCatalogue ? fromCatalogue + " · " : "")
                + (episode is null ? "" : episode.Spoken + " · ")
                + Path.GetFileName(Path.GetDirectoryName(entry.Path))
            : entry.Kind == LibraryKind.Picture
                ? string.Join(" · ", new[] { Path.GetFileName(Path.GetDirectoryName(entry.Path)), entry.Modified == default ? null : entry.Modified.ToLocalTime().ToString("d", CultureInfo.CurrentCulture) }.Where(part => part is not null))
                : string.Join(" · ", new[] { entry.Artist, entry.Album }.Where(part => part is not null));
        if (entry.Problem is { } problem)
        {
            // A file whose details could not be read (damaged, most often) says why instead.
            detail = "Could not be read: " + problem;
        }

        var extra = entry.Kind != LibraryKind.Picture && entry.Duration is { } duration ? TimeText.Format(duration, duration) : "";
        var badge = entry.Kind switch
        {
            LibraryKind.Music when entry.Track is { } track => entry.Disc is > 1 ? $"{entry.Disc}.{track}" : track.ToString(CultureInfo.CurrentCulture),
            LibraryKind.Video when episode is not null => episode.Badge,
            _ => "",
        };
        var placeholder = entry.Kind switch { LibraryKind.Music => "♪", LibraryKind.Video => "▶", _ => "▧" };
        var progress = entry.Kind == LibraryKind.Video && _player.LeftAt(entry.Path) is { } left && entry.Duration is { TotalSeconds: > 0 } length ? Math.Clamp(left / length, 0, 1) : 0;
        var row = new LibraryRow(name, detail, extra, entry, hasPicture: true, picturePlaceholder: placeholder, badge: badge, progress: progress,
            isPlaying: LibraryViews.SameLocation(entry.Path, _player.Playlist.Current?.Location));
        var (glyph, text) = TranscriptBadgeOf(entry);
        row.SetTranscript(glyph, text);
        ShowDownload(row, entry);
        return row;
    }


    /// <summary>A visible media line asks for its picture once; recycled, off-screen rows ask for nothing.</summary>
    private void OnLibraryRowShown(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (!args.InRecycleQueue)
        {
            RequestLibraryPicture(args.Item as LibraryRow);
        }

        // Only the persistent main list wires focus: its containers recycle, so the set is bounded.
        if (sender == LibraryList)
        {
            WireRowFocus(args.ItemContainer as Microsoft.UI.Xaml.Controls.Primitives.SelectorItem);
        }
    }

    /// <summary>Only request artwork when the virtualizing panel actually realizes a tile.</summary>
    private void RequestLibraryPicture(LibraryRow? row)
    {
        if (row is { HasPicture: true, PictureAsked: false } && PictureEntry(row.Item) is { } entry)
        {
            row.PictureAsked = true;
            _ = ShowPictureAsync(entry.Path, entry.Kind, entry.Duration, picture => row.Picture = picture, _libraryPictures.Token);
        }
    }

    /// <summary>
    /// Put the pointed-at card first, then its near neighbours. Old hover work is cancelled
    /// as soon as the pointer moves, while the ordinary visible-card queue keeps progressing.
    /// </summary>
    private void PrioritizeLibraryPicture(LibraryRow row)
    {
        _hoverPictures.Cancel();
        _hoverPictures.Dispose();
        _hoverPictures = CancellationTokenSource.CreateLinkedTokenSource(_libraryPictures.Token);
        var token = _hoverPictures.Token;

        void Request(LibraryRow item)
        {
            if (item.Picture is not null || PictureEntry(item.Item) is not { } entry)
            {
                return;
            }

            item.PictureAsked = true;
            _ = ShowPictureAsync(entry.Path, entry.Kind, entry.Duration,
                picture => item.Picture = picture, token, urgent: true);
        }

        Request(row);
        var rows = _libraryRows.Contains(row) ? _libraryRows : _homeShelfRows;
        var index = rows.IndexOf(row);
        if (index < 0)
        {
            return;
        }

        // A bounded neighbourhood: fast to fill but never preloads the whole library.
        foreach (var neighbour in new[] { index - 1, index + 1, index - 2, index + 2 })
        {
            if (neighbour >= 0 && neighbour < rows.Count)
            {
                Request(rows[neighbour]);
            }
        }
    }

    private static LibraryEntry? PictureEntry(object item) => item switch
    {
        LibraryEntry entry => entry,
        LibraryGroup { Entries.Count: > 0 } group => group.Entries[0],
        _ => null,
    };

    private void CancelLibraryPictures()
    {
        _hoverPictures.Cancel();
        _hoverPictures.Dispose();
        _hoverPictures = new CancellationTokenSource();
        _libraryPictures.Cancel();
        _libraryPictures.Dispose();
        _libraryPictures = new CancellationTokenSource();
    }

    /// <summary>Selection follows the active surface; library commands work in every layout.</summary>
    private List<LibraryRow> SelectedLibraryRows() =>
        LibraryCollage.Visibility == Visibility.Visible
            ? [.. LibraryCollage.SelectedItems.OfType<LibraryRow>()]
            : [.. LibraryList.SelectedItems.OfType<LibraryRow>()];

    /// <summary>The lines chosen, or every line when none is.</summary>
    private List<LibraryRow> ChosenRows()
    {
        var rows = _libraryRows;
        var chosen = SelectedLibraryRows();
        return chosen.Count > 0 ? [.. rows.Where(chosen.Contains)] : rows;
    }

    /// <summary>
    /// The media the lines stand for, in order: an album's songs, or a folder's media by kind. A
    /// file several lines share (an artist page's top song inside one of its albums) plays once,
    /// at its first place.
    /// </summary>
    private List<LibraryEntry> EntriesOf(IEnumerable<LibraryRow> rows) =>
        [.. rows.SelectMany(row => row.Item switch
        {
            LibraryEntry entry => [entry],
            LibraryGroup group => group.Entries,
            string folder => [.. LibraryViews.Songs(_library!.Entries.Where(entry => MediaLibrary.Holds(folder, entry.Path))), .. LibraryViews.Videos(_library.Entries.Where(entry => MediaLibrary.Holds(folder, entry.Path))), .. LibraryViews.Pictures(_library.Entries.Where(entry => MediaLibrary.Holds(folder, entry.Path)))],
            _ => [],
        }).DistinctBy(entry => entry.Path, StringComparer.OrdinalIgnoreCase)];

    private void OnLibraryPlay(object sender, RoutedEventArgs e) => PlayChosen();

    private void OnLibraryEnqueue(object sender, RoutedEventArgs e) => EnqueueChosen();

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
            case VirtualKey.F2 when LibraryList.SelectedItem is LibraryRow { Item: NamedPlaylist playlist }:
                _ = RenamePlaylistAsync(playlist.Id);
                e.Handled = true;
                break;
            case VirtualKey.Delete when SelectedLibraryRows() is { Count: > 0 } lines && lines.All(line => LibraryActions.DeletesFiles(line.Item)) && EntriesOf(lines).Count > 0:
                _ = ChooseLibraryDeletionAsync();
                e.Handled = true;
                break;
        }
    }

    /// <summary>The list's right-click menu, for the lines chosen when it opens.</summary>
    private void FillLibraryMenu(MenuFlyout menu, List<LibraryRow> chosen)
    {
        menu.Items.Clear();
        if (chosen.Count == 0)
        {
            menu.Items.Add(new MenuFlyoutItem { Text = "Choose something first", IsEnabled = false });
            return;
        }

        if (chosen[0].Item is string folder)
        {
            menu.Items.Add(MenuItem("Play everything in it", "LibraryMenuPlay", PlayChosen));
            menu.Items.Add(MenuItem("Stop watching", "LibraryMenuRemoveFolder", RemoveChosenFolders));
            AddTranscriptionMenuItems(menu, EntriesOf([chosen[0]]), folder);
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
            menu.Items.Add(MenuItem($"Play season {episode.Season} from here ({LibraryActions.Count(season.Count, "episode")})", "LibraryMenuPlay", PlayChosen));
            menu.Items.Add(MenuItem("Play only this episode", null, () => Play([video], 0)));
        }
        else
        {
            menu.Items.Add(MenuItem("Play", "LibraryMenuPlay", PlayChosen));
        }

        var selectedEntries = EntriesOf(chosen).DistinctBy(entry => entry.Path, StringComparer.OrdinalIgnoreCase).ToList();
        menu.Items.Add(MenuItem(LibraryActions.MenuText("Play", selectedEntries.Count, tail: "next"), "LibraryMenuPlayNext",
            () => _player.PlayNextFromLibrary(selectedEntries)));
        menu.Items.Add(MenuItem(LibraryActions.MenuText("Add", selectedEntries.Count, tail: "to queue"), "LibraryMenuEnqueue", EnqueueChosen));
        var items = selectedEntries.Select(PlayerController.ItemOf).ToList();
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
        if (selectedEntries.Count > 0)
        {
            menu.Items.Add(new MenuFlyoutSeparator());
            menu.Items.Add(MenuItem(LibraryActions.MenuText("Mark", selectedEntries.Count, tail: "as watched"), "LibraryMenuMarkWatched",
                () => _player.MarkWatched(selectedEntries.Select(entry => entry.Path).ToList())));
            menu.Items.Add(MenuItem(LibraryActions.MenuText("Mark", selectedEntries.Count, tail: "as unwatched"), "LibraryMenuMarkUnwatched",
                () => _player.MarkUnwatched(selectedEntries.Select(entry => entry.Path).ToList())));
            menu.Items.Add(MenuItem(LibraryActions.MenuText("Reset", selectedEntries.Count, tail: "progress"), "LibraryMenuResetProgress",
                () => _player.ResetProgress(selectedEntries.Select(entry => entry.Path).ToList())));
            menu.Items.Add(MenuItem(LibraryActions.MenuText("Copy", selectedEntries.Count, tail: "paths"), "LibraryMenuCopyPath",
                () => CopyLibraryPaths(selectedEntries)));
            menu.Items.Add(MenuItem(selectedEntries.Count == 1 ? "Open containing folder" : "Open containing folders", "LibraryMenuOpenFolder",
                () => _ = OpenLibraryFoldersAsync(selectedEntries)));
            AddEditDetailsMenuItem(menu, selectedEntries);
            AddTranscriptionMenuItems(menu, selectedEntries, null);
            menu.Items.Add(new MenuFlyoutSeparator());
            AddMetadataMenuItems(menu, chosen);

            menu.Items.Add(MenuItem(LibraryActions.MenuText("Remove", selectedEntries.Count, tail: "from library..."), "LibraryMenuRemove",
                () => _ = RemoveLibraryEntriesAsync(selectedEntries)));
            menu.Items.Add(MenuItem(LibraryActions.MenuText("Delete", selectedEntries.Count, tail: "to Recycle Bin..."), "LibraryMenuDelete",
                () => _ = DeleteLibraryEntriesAsync(selectedEntries)));
        }
    }

    private static async Task OpenLibraryFoldersAsync(IReadOnlyList<LibraryEntry> entries)
    {
        foreach (var folder in entries.Select(entry => Path.GetDirectoryName(entry.Path))
            .Where(folder => !string.IsNullOrEmpty(folder)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            await Launcher.LaunchFolderPathAsync(folder!);
        }
    }

    /// <summary>Copies the selected file locations in view order.</summary>
    private void CopyLibraryPaths(IReadOnlyList<LibraryEntry> entries)
    {
        var package = new DataPackage();
        package.SetText(string.Join(Environment.NewLine, entries.Select(entry => entry.Path)));
        Clipboard.SetContent(package);
        Say(entries.Count == 1 ? "Copied the file path" : $"Copied {entries.Count} file paths");
    }

    /// <summary>Asks which deletion the keyboard means, never taking either destructive action silently.</summary>
    private async Task ChooseLibraryDeletionAsync()
    {
        var entries = EntriesOf(SelectedLibraryRows()).DistinctBy(entry => entry.Path, StringComparer.OrdinalIgnoreCase).ToList();
        if (entries.Count == 0)
        {
            return;
        }

        var dialog = new ContentDialog
        {
            Title = "What should happen to the selected files?",
            Content = new TextBlock
            {
                Text = "Remove from the library keeps the files. Move to Recycle Bin moves the files off disk.",
                TextWrapping = TextWrapping.Wrap,
            },
            PrimaryButtonText = "Remove from library",
            SecondaryButtonText = "Move to Recycle Bin",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
        };
        switch (await ShowDialogAsync(dialog))
        {
            case ContentDialogResult.Primary:
                await RemoveLibraryEntriesAsync(entries);
                break;
            case ContentDialogResult.Secondary:
                await DeleteLibraryEntriesAsync(entries);
                break;
        }
    }

    private async Task RemoveLibraryEntriesAsync(IReadOnlyList<LibraryEntry> entries)
    {
        var paths = entries.Select(entry => entry.Path).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (paths.Count == 0)
        {
            return;
        }

        var prompt = LibraryActions.RemovePrompt(paths.Select(path => Path.GetFileName(path) ?? path).ToList());
        if (await Ask(prompt.Title, new TextBlock { Text = prompt.Body, TextWrapping = TextWrapping.Wrap }, "Remove", "Cancel"))
        {
            _player.RemoveFromLibrary(paths);
        }
    }

    /// <summary>Moves files through the OS Recycle Bin only, then forgets successful moves.</summary>
    private async Task DeleteLibraryEntriesAsync(IReadOnlyList<LibraryEntry> entries)
    {
        var paths = entries.Select(entry => entry.Path).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (paths.Count == 0)
        {
            return;
        }

        var prompt = LibraryActions.DeletePrompt(paths.Select(path => Path.GetFileName(path) ?? path).ToList());
        if (!await Ask(prompt.Title, new TextBlock { Text = prompt.Body, TextWrapping = TextWrapping.Wrap }, "Move to Recycle Bin", "Cancel"))
        {
            return;
        }

        var moved = new List<string>();
        var failed = new List<string>();
        await Task.Run(() =>
        {
            foreach (var path in paths)
            {
                try
                {
                    FileSystem.DeleteFile(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin,
                        UICancelOption.ThrowException);
                    moved.Add(path);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
                    or NotSupportedException or OperationCanceledException)
                {
                    failed.Add(Path.GetFileName(path) + ": " + ex.Message);
                }
            }
        });

        if (moved.Count > 0)
        {
            _player.ForgetDeleted(moved);
        }

        if (failed.Count > 0)
        {
            Say($"{failed.Count} file(s) could not be moved to the Recycle Bin: " + string.Join("; ", failed.Take(3)));
        }
    }

    private async void OnLibraryAddFolder(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = Prepared(new FolderPicker { SuggestedStartLocation = PickerLocationId.MusicLibrary });
            picker.FileTypeFilter.Add("*");
            if (await picker.PickSingleFolderAsync() is { } folder)
            {
                AddLibraryFolder(folder.Path);
            }
        }
        catch (Exception ex)
        {
            App.Log.Error(LogSource, "A folder could not be added: " + ex);
            Say("The folder could not be added: " + Rex.Media.AppCore.Player.DamageNotices.Short(ex.Message));
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

    private void OnLibraryBackUp(object sender, RoutedEventArgs e) => _ = BackUpLibraryAsync();

    private void OnLibraryRestore(object sender, RoutedEventArgs e) => _ = RestoreLibraryAsync();

    /// <summary>Keeps the library and the named playlists in a folder the user chooses (LIB-10).</summary>
    private async Task BackUpLibraryAsync()
    {
        var picker = Prepared(new FolderPicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary });
        picker.FileTypeFilter.Add("*");
        if (await picker.PickSingleFolderAsync() is not { } folder)
        {
            return;
        }

        try
        {
            var playlists = _player.NamedPlaylists.ToArray();
            await Task.Run(() =>
            {
                foreach (var (path, bytes) in LibraryBackup.Export(_library!, playlists))
                {
                    var target = Path.Combine(folder.Path, path);
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    Rex.Media.IO.AtomicFile.Write(target, bytes);
                }
            });

            App.Log.Info(LogSource, "Backed the library up to " + folder.Path);
            Say("The library is backed up in " + folder.Path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            App.Log.Warning(LogSource, "The library could not be backed up: " + ex.Message);
            Say("The library could not be backed up there: " + ex.Message);
        }
    }

    /// <summary>Adds a backup's folders, play counts and playlists to what the library has (LIB-10).</summary>
    private async Task RestoreLibraryAsync()
    {
        var picker = Prepared(new FileOpenPicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary });
        picker.FileTypeFilter.Add(".json");
        if (await picker.PickSingleFileAsync() is not { } file)
        {
            return;
        }

        try
        {
            var summary = LibraryBackup.Restore(LibraryBackup.Read(await File.ReadAllBytesAsync(file.Path)), _library!, _player);
            WatchLibraryFolders();
            _scanner!.Request();
            App.Log.Info(LogSource, $"Brought back {file.Path}: {summary}.");
            Say($"Brought back {LibraryActions.Count(summary.Folders, "folder")}, {LibraryActions.Count(summary.Entries, "file")} and {LibraryActions.Count(summary.Playlists, "playlist")}");
        }
        catch (Exception ex) when (ex is FormatException or IOException or UnauthorizedAccessException)
        {
            Say(ex.Message);
        }
    }
}
