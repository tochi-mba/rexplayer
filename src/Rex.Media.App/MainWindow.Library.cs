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
    private IReadOnlyList<LibraryEntry>? _libraryRemovalUndo;
    private bool _focusLibraryAfterUndoRefresh;
    private readonly HashSet<string> _changedFolders = new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource _libraryPictures = new();
    private CancellationTokenSource _hoverPictures = new();
    private MediaLibrary? _library;
    private LibraryScanner? _scanner;
    private LibrarySectionAnalysis? _backgroundSections;
    private DispatcherQueueTimer? _libraryRefresh;
    private DispatcherQueueTimer? _libraryChanges;
    private DispatcherQueueTimer? _libraryRescan;
    private readonly Windows.UI.ViewManagement.AccessibilitySettings _libraryContrastSettings = new();
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
        Audiobooks,
        Authors,
        Podcasts,
        Recordings,
        Movies,
        TvShows,
        OtherVideos,
        Videos,
        Pictures,
        ContinueWatching,
        Completed,
        RecentlyPlayed,
        RecentlyAdded,
        Playlists,
        Folders,
        Music,
    }

    private bool LibraryOpen => LibraryPane.Visibility == Visibility.Visible;

    private void RebuildLibraryForSystemPalette()
    {
        if (_closed || !LibraryOpen)
        {
            return;
        }

        _shownLibrary = "";
        _shownHero = "";
        ShowLibrary();
    }

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
        _scanner = new LibraryScanner(_library, LibraryScanner.ListFolder, LibraryScanner.ProbeFile, App.Log, inspect: InspectFile);
        _scanner.StatusChanged += (_, _) => OnWindowThread(ShowLibraryStatus);
        _backgroundSections = new LibrarySectionAnalysis(_player.Memory, _player.SectionCache,
            _player.SectionAnalyzer!, App.Log);
        UpdateBackgroundSectionPause();
        _player.Changed += (_, _) => UpdateBackgroundSectionPause();
        _player.Changed += (_, _) => OnPlayingChangedForDetailWrites();

        // A burst of changes (a scan's batches) shows once.
        _libraryRefresh = CreateTimer();
        _libraryRefresh.Interval = TimeSpan.FromMilliseconds(400);
        _libraryRefresh.IsRepeating = false;
        OnTick(_libraryRefresh, RefreshLibraryAfterChanges);
        _library.Changed += (_, _) => OnWindowThread(() => Restart(_libraryRefresh));
        WireDownloadRechecks();

        // Files copied in arrive in many events; the folder is looked at once they settle.
        _libraryChanges = CreateTimer();
        _libraryChanges.Interval = TimeSpan.FromSeconds(3);
        _libraryChanges.IsRepeating = false;
        OnTick(_libraryChanges, LookAtChangedFolders);

        _libraryRescan = CreateTimer();
        _libraryRescan.Interval = LibraryRescanEvery;
        OnTick(_libraryRescan, () => _scanner.Request());
        _libraryRescan.Start();

        // Re-create theme-specific library visuals when Windows theme/contrast
        // changes. A previously rendered movie billboard must not retain white
        // over-photo text after the user switches to a high-contrast theme.
        Root.ActualThemeChanged += (_, _) => RebuildLibraryForSystemPalette();
        try
        {
            _libraryContrastSettings.HighContrastChanged += (_, _) =>
                DispatcherQueue.TryEnqueue(RebuildLibraryForSystemPalette);
        }
        catch (System.Runtime.InteropServices.COMException ex) when (ex.HResult == unchecked((int)0x80070490))
        {
            // This notification is optional on stripped-down Windows desktops.
            // Library views still use the current palette when rebuilt.
            App.Log.Warning(LogSource, "Library contrast notifications are unavailable: " + ex.Message);
        }

        LibrarySources.ItemsSource = LibrarySourceItems.Select(source => source.Item).ToList();
        ApplyLibraryNavigationWidth();
        WireLibraryView();
        WireListening();
        WireSections();
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
        OnTick(firstScan, () =>
        {
            _scanner?.Request();
            QueueLibrarySections();
        });
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
            Forget(Task.Run(sections.Dispose));
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
            Forget(Task.Run(scanner.Dispose));
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
        UpdateBackgroundSectionPause();
        if (_transcriptionStarted)
        {
            // Apply the browsing throttle immediately instead of leaving Whisper competing with
            // scrolling until its next one-second policy tick.
            TranscriptionTick();
        }
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
            LibraryKind.Music => SourceOfAudio(located.Entry),
            LibraryKind.Picture => LibrarySource.Pictures,
            LibraryKind.Video when located.Show is not null => LibrarySource.TvShows,
            LibraryKind.Video => SectionOf(located.Entry),
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

        if (source == LibrarySource.Audiobooks)
        {
            // Return-to-reading opens the containing book, not the generic
            // Audiobooks shelf, whose cards cannot reveal an individual chapter.
            _libraryGroup = Listening().Audiobooks.FirstOrDefault(book => book.Entries.Any(part =>
                LibraryViews.SameLocation(part.Path, located.Entry.Path)));
            _librarySeason = null;
        }
        else
        {
            _libraryGroup = located.Show;
            _librarySeason = located.Season;
        }

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
        if (_focusLibraryAfterUndoRefresh)
        {
            // Restoring a file rebuilds ListView rows after the 400ms refresh timer.
            // Focusing before that rebuild is not enough: WinUI drops the focused
            // container as soon as ItemsSource is replaced.
            _focusLibraryAfterUndoRefresh = false;
            FocusLibraryAfterFileAction();
        }

        ShowMovieVersions();
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
                FileSystemEventHandler structural = (_, _) => FolderChanged(folder);
                watcher.Created += structural;
                watcher.Deleted += structural;
                watcher.Changed += (_, change) => FileContentsChanged(folder, change.FullPath);
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

    /// <summary>
    /// A downloader can raise thousands of LastWrite and Size notifications for one file. Once a
    /// scan knows that file is downloading, the lightweight download recheck owns its progress and
    /// completion; rescanning its entire folder for every write would repeatedly enumerate and
    /// probe unrelated media. Creates, deletes and renames still take the structural path above.
    /// </summary>
    private void FileContentsChanged(string folder, string path)
    {
        var partial = Downloads.WholeNameOf(path);
        var marker = path.EndsWith(Downloads.Aria2Marker, StringComparison.OrdinalIgnoreCase)
            ? path[..^Downloads.Aria2Marker.Length]
            : null;
        var media = partial ?? marker ?? path;
        var known = _library?.Entry(path) ?? _library?.Entry(media);
        if (known is { Downloading: true } or { Suspect: true })
        {
            QueueDownloadRecheck();
            return;
        }

        FolderChanged(folder);
    }

    /// <summary>On a watcher's thread: notes the folder and looks at it once the changes settle.</summary>
    private void FolderChanged(string folder)
    {
        lock (_changedFolders)
        {
            if (!_changedFolders.Add(folder))
            {
                return;
            }
        }

        // Settle from the first change, not the last: a download writes without pause, and
        // restarting the wait on every write would put the look off for as long as it lasts.
        OnWindowThread(() =>
        {
            if (!_libraryChanges!.IsRunning)
            {
                _libraryChanges.Start();
            }
        });
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

    private LibraryRow GroupRow(LibraryGroup group)
    {
        // A group carries media entries but its meaning comes from the route. In particular, an
        // author must not inherit the generic audio treatment and present as a music artist.
        if (_librarySource == LibrarySource.Authors)
        {
            var books = ListeningViews.Books(group.Entries);
            return new LibraryRow(group.Name, "Author", LibraryActions.Count(books.Count, "book"),
                group, hasPicture: false, picturePlaceholder: "✎");
        }

        var videos = group.Entries.All(entry => entry.Kind == LibraryKind.Video);
        return new LibraryRow(group.Name,
            MetadataBlurb(group) is { Length: > 0 } blurb ? (group.Detail is null ? blurb : group.Detail + " · " + blurb) : group.Detail,
            LibraryActions.Count(group.Entries.Count, videos ? "episode" : "song"),
            group, hasPicture: true, picturePlaceholder: videos ? "▶" : "♪");
    }

    private LibraryRow EntryRow(LibraryEntry entry)
    {
        // A tentative special (a movie's extras, perhaps) is labelled only on its show's page.
        var episode = entry.Kind == LibraryKind.Video && LibraryViews.EpisodeOf(entry) is { Tentative: false } found ? found : null;
        var name = _librarySource == LibrarySource.Movies && entry.Kind == LibraryKind.Video
            ? LibraryViews.MovieTitleOf(entry)
            : episode is { Episode: not null } && entry.Title == MediaLibrary.TitleOf(entry.Path)
                ? FetchedEpisodeName(episode) ?? episode.DisplayName : entry.Title;
        var detail = entry.Kind == LibraryKind.Video
            ? (_librarySource is LibrarySource.OtherVideos or LibrarySource.Videos
                && !string.Equals(Path.GetFileName(entry.Path), entry.Title, StringComparison.OrdinalIgnoreCase)
                    ? Path.GetFileName(entry.Path) + " · " : "")
                + (_player.LeftAt(entry.Path) is { } at ? "Stopped at " + TimeText.Format(at, entry.Duration ?? TimeSpan.Zero) + " · " : "")
                + (_librarySource == LibrarySource.Movies && LibraryViews.MovieYearOf(entry) is { } year ? year.ToString(CultureInfo.CurrentCulture) + " · " : "")
                + (_librarySource == LibrarySource.Movies && MovieBlurb(entry) is { Length: > 0 } fromCatalogue ? fromCatalogue + " · " : "")
                + (_librarySource == LibrarySource.Movies && MovieRules.NotMatched(entry, MovieLookupOf(entry)) ? "Not matched · " : "")
                + (episode is null ? "" : episode.Spoken + " · ")
                + (entry.VideoWidth is > 0 && entry.VideoHeight is > 0
                    ? $"{entry.VideoWidth} × {entry.VideoHeight} · " : "")
                + Path.GetFileName(Path.GetDirectoryName(entry.Path))
            : entry.Kind == LibraryKind.Picture
                ? string.Join(" · ", new[]
                {
                    Path.GetFileName(entry.Path),
                    Path.GetFileName(Path.GetDirectoryName(entry.Path)),
                    entry.PictureWidth is > 0 && entry.PictureHeight is > 0
                        ? $"{entry.PictureWidth} × {entry.PictureHeight}" : null,
                    entry.Modified == default ? null : entry.Modified.ToLocalTime().ToString("d", CultureInfo.CurrentCulture),
                }.Where(part => part is not null))
                : string.Join(" · ", new[] { ArtistNames.Display(entry.Artist), entry.Album }.Where(part => part is not null));
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
        if (args.ItemContainer is { } realized)
        {
            // The row's title remains a concise screen-reader name; a secondary
            // filename and parent-folder hint distinguishes otherwise identical
            // titles and is replaced when a virtualized container is recycled.
            AutomationProperties.SetHelpText(realized, args.InRecycleQueue ? ""
                : args.Item is LibraryRow row ? row.AccessibilityContext : "");
        }

        if (args.InRecycleQueue)
        {
            LetPictureGo(args.Item as LibraryRow);
        }
        else
        {
            RequestLibraryPicture(args.Item as LibraryRow, ShowsPosters(sender));
            RequestEpisodeNotes(args.Item as LibraryRow);
        }

        // UI Automation reads the ListViewItem peer, not its template's Grid.
        // Recycled containers must update (and clear) their help text for each row.
        if (args.ItemContainer is { } container)
        {
            AutomationProperties.SetHelpText(container, args.InRecycleQueue
                ? "" : (args.Item as LibraryRow)?.AccessibilityDetails ?? "");
        }

        // Only the persistent main list wires focus: its containers recycle, so the set is bounded.
        if (sender == LibraryList)
        {
            WireRowFocus(args.ItemContainer as Microsoft.UI.Xaml.Controls.Primitives.SelectorItem);
        }
    }

    /// <summary>
    /// Only request artwork when the virtualizing panel actually realizes a tile; a movie on a poster
    /// card (<paramref name="posterCard"/>) asks for a poster's shape.
    /// </summary>
    private void RequestLibraryPicture(LibraryRow? row, bool posterCard = false)
    {
        if (row is { HasPicture: true, PictureAsked: false } && PictureEntry(row.Item) is { } entry)
        {
            row.PictureAsked = true;
            row.Shape = PictureArtOf(row, posterCard || row.Shape == PictureArt.MoviePoster);
            Forget(LoadRowPictureAsync(row, entry, urgent: false, _libraryPictures.Token));
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
            Forget(LoadRowPictureAsync(item, entry, urgent: true, token));
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
        BookChapter chapter => chapter.Part,
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
            BookChapter chapter => [chapter.Part],
            string folder => [.. LibraryViews.Songs(_library!.Entries.Where(entry => MediaLibrary.Holds(folder, entry.Path))), .. LibraryViews.Videos(_library.Entries.Where(entry => MediaLibrary.Holds(folder, entry.Path))), .. LibraryViews.Pictures(_library.Entries.Where(entry => MediaLibrary.Holds(folder, entry.Path)))],
            _ => [],
        }).DistinctBy(entry => entry.Path, StringComparer.OrdinalIgnoreCase)];

    private void OnLibraryPlay(object sender, RoutedEventArgs e)
    {
        if (!PlayDetailsMovie())
        {
            PlayChosen();
        }
    }

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
                Forget(RenamePlaylistAsync(playlist.Id));
                e.Handled = true;
                break;
            case VirtualKey.Delete when SelectedLibraryRows() is { Count: > 0 } lines && lines.All(line => LibraryActions.DeletesFiles(line.Item)) && EntriesOf(lines).Count > 0:
                Forget(ChooseLibraryDeletionAsync());
                e.Handled = true;
                break;
        }
    }

    /// <summary>Reveals each selected file in Explorer, while tolerating removed or inaccessible files.</summary>
    private Task OpenLibraryFoldersAsync(IReadOnlyList<LibraryEntry> entries) =>
        RevealFilesInExplorerAsync(entries.Select(entry => entry.Path));

    /// <summary>Use the same file-selecting Explorer flow from library and queue menus.</summary>
    private async Task RevealFilesInExplorerAsync(IEnumerable<string> paths)
    {
        foreach (var group in LibraryActions.RevealGroups(paths))
        {
            var options = new Windows.System.FolderLauncherOptions();
            foreach (var path in group.Files)
            {
                try
                {
                    options.ItemsToSelect.Add(await Windows.Storage.StorageFile.GetFileFromPathAsync(path));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                    or ArgumentException or NotSupportedException or System.Runtime.InteropServices.COMException)
                {
                    // Files can go missing between a scan and the menu; still open the parent folder.
                    App.Log.Debug(LogSource, $"Could not select {path} in Explorer: {ex.Message}");
                }
            }

            try
            {
                if (!await Launcher.LaunchFolderPathAsync(group.Folder, options))
                {
                    Say("File Explorer could not open " + group.Folder);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                or ArgumentException or NotSupportedException or System.Runtime.InteropServices.COMException)
            {
                Say("File Explorer could not open " + group.Folder + ": " + ex.Message);
            }
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

    /// <summary>Return keyboard focus after modal confirmation and background filesystem operations.</summary>
    private void FocusLibraryAfterFileAction()
    {
        OnWindowThread(DispatcherQueuePriority.Low, () =>
        {
            if (_closed || !LibraryOpen)
            {
                return;
            }

            void RestoreFocus()
            {
                if (_closed || !LibraryOpen)
                {
                    return;
                }

                if (LibraryCollage.Visibility == Visibility.Visible)
                {
                    LibraryCollage.Focus(FocusState.Programmatic);
                }
                else
                {
                    // ListView itself is not consistently a keyboard focus
                    // target once its rows are regenerated. Prefer a realized
                    // row, which is also what keyboard users interact with.
                    if (LibraryList.ContainerFromIndex(0) is not Control row
                        || !row.Focus(FocusState.Keyboard))
                    {
                        LibraryList.Focus(FocusState.Programmatic);
                    }
                }
            }

            RestoreFocus();
            // A library refresh can replace rows after this callback begins.
            // Reassert focus once those queued view changes have completed.
            OnWindowThread(DispatcherQueuePriority.Low, RestoreFocus);
        });
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
            var before = paths.Select(path => _library?.Entry(path)).OfType<LibraryEntry>().ToArray();
            _player.RemoveFromLibrary(paths);
            _libraryRemovalUndo = before.Length > 0 ? before : null;
            LibraryUndoRemove.Visibility = _libraryRemovalUndo is null ? Visibility.Collapsed : Visibility.Visible;
        }

        FocusLibraryAfterFileAction();
    }

    /// <summary>Undo a library-only removal; never re-add paths no longer available on disk.</summary>
    private void OnLibraryUndoRemove(object sender, RoutedEventArgs e) => Forget(UndoLibraryRemoveAsync());

    private async Task UndoLibraryRemoveAsync()
    {
        if (_libraryRemovalUndo is not { } before)
        {
            return;
        }

        _libraryRemovalUndo = null;
        // Move focus off the Undo button before collapsing it. WinUI can
        // otherwise leave keyboard focus on the removed visual indefinitely.
        LibraryList.Focus(FocusState.Keyboard);
        LibraryUndoRemove.Visibility = Visibility.Collapsed;
        var available = await Task.Run(() => before.Where(entry => File.Exists(entry.Path)).ToArray());
        if (!_closed && available.Length > 0)
        {
            _focusLibraryAfterUndoRefresh = true;
            _player.RestoreToLibrary(available);
        }
        else if (!_closed)
        {
            Say("No removed files are available to restore");
        }

        FocusLibraryAfterFileAction();
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
            FocusLibraryAfterFileAction();
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
            _libraryRemovalUndo = null;
            LibraryUndoRemove.Visibility = Visibility.Collapsed;
            _player.ForgetDeleted(moved);
        }

        if (failed.Count > 0)
        {
            Say($"{failed.Count} file(s) could not be moved to the Recycle Bin: " + string.Join("; ", failed.Take(3)));
        }

        FocusLibraryAfterFileAction();
    }

    private void OnLibraryRemoveFolder(object sender, RoutedEventArgs e) => Forget(RemoveChosenFoldersAsync());

    private async Task RemoveChosenFoldersAsync()
    {
        var folders = LibraryList.SelectedItems.OfType<LibraryRow>().Select(row => row.Item).OfType<string>().ToList();
        if (folders.Count == 0)
        {
            Say("Choose the folders to stop watching first.");
            return;
        }

        var prompt = LibraryActions.StopWatchingPrompt(folders);
        if (!await Ask(prompt.Title, new TextBlock { Text = prompt.Body, TextWrapping = TextWrapping.Wrap }, "Stop watching", "Cancel"))
        {
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
