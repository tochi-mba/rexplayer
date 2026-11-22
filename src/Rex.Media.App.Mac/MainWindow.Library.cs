using Rex.Media.AppCore.Automation;
using Rex.Media.AppCore.Library;
using Rex.Media.AppCore.Player;
using Rex.Media.Library;
using Rex.Media.Settings;

namespace Rex.Media.App.Mac;

/// <summary>
/// The native Mac library over the same persisted catalogue and scanner used on Windows, laid out
/// as the Windows library pane is: a navigation column of views at the left and, on a rounded page
/// at the right, the view's banner (artwork, kicker, title, what it holds and what to do with it),
/// the search, the view's order, grouping and filter, then its results. It fills the player
/// window's library host over the stage, so playback carries on behind it.
/// </summary>
public sealed partial class MainWindow
{
    private Rex.Media.Library.MediaLibrary? _mediaLibrary;
    private LibraryScanner? _libraryScanner;
    private NSTimer? _libraryPeriodicScan;
    private LibraryPaneView? _libraryRoot;
    private NSSearchField? _librarySearch;
    private NSPopUpButton? _libraryCategory;
    private NSPopUpButton? _librarySort;
    private NSPopUpButton? _libraryGrouping;
    private NSScrollView? _libraryScroll;
    private NSView? _libraryEmpty;
    private NSStackView? _libraryPager;
    private NSView? _libraryPagerHolder;
    private RexLabel? _librarySummary;
    private RexLabel? _libraryPageSummary;
    private RexButton? _libraryPreviousPage;
    private RexButton? _libraryNextPage;
    private NSButton? _libraryFirstPlay;
    private NSButton? _libraryFirstQueue;
    private RexButton? _libraryAddButton;
    private RexButton? _libraryRescanButton;
    private RexButton? _libraryBackButton;
    private LibraryHero? _libraryListHero;
    private RexButton? _libraryPlayAll;
    private RexButton? _libraryShuffleAll;
    private RexButton? _libraryQueueAll;
    private RexButton? _libraryMore;
    private NSStackView? _libraryFolderActions;
    private readonly List<NSView> _libraryFolderActionKeys = [];
    private readonly List<NSView> _libraryResultKeyViews = [];
    private readonly List<(string Path, RexTextButton Title)> _libraryRowTitles = [];
    private LibraryResultRow? _libraryChosenRow;
    private string? _libraryMarkedPlaying;
    private IReadOnlyList<LibraryEntry> _libraryCurrentSelection = [];
    private int _libraryMatchingCount;
    private int _libraryTotalCount;
    private int _libraryFolderCount;
    private int _libraryVisibleResultCount;
    private IReadOnlyList<LibraryEntry> _libraryRenderedEntries = [];
    private int _libraryRenderedPage = -1;
    private string? _libraryRenderedKey;
    private int _libraryPageIndex;
    private const int LibraryPageSize = 80;
    private const double MinimumLibraryPageWidth = 590;
    private const double LibraryHeroArtworkSize = 144;
    private const double LibraryRowHeight = 52;
    private bool _closingLibrary;
    private bool _libraryTemporaryStore;
    private RexStore? _libraryStore;
    private readonly Dictionary<string, NSImage> _libraryThumbnails = new(StringComparer.OrdinalIgnoreCase);
    private readonly Queue<string> _libraryThumbnailOrder = new();
    private const int LibraryThumbnailLimit = 160;

    /// <summary>The orders a view can take, named as Windows names them; the first is the view's own.</summary>
    private static readonly (LibrarySort Sort, string Name)[] LibrarySortNames =
    [
        (LibrarySort.Natural, "Natural order"), (LibrarySort.Title, "Title"), (LibrarySort.Artist, "Artist"), (LibrarySort.Album, "Album"),
        (LibrarySort.Year, "Year"), (LibrarySort.Added, "Date added"), (LibrarySort.Played, "Last played"), (LibrarySort.MostPlayed, "Most played"),
        (LibrarySort.Length, "Duration"), (LibrarySort.Modified, "Date modified"),
        (LibrarySort.Size, "File size"), (LibrarySort.Path, "Folder and path"),
    ];

    /// <summary>The headings a view can be gathered under, named as Windows names them.</summary>
    private static readonly (LibraryGrouping Grouping, string Name)[] LibraryGroupingNames =
    [
        (LibraryGrouping.None, "No groups"), (LibraryGrouping.Letter, "First letter"), (LibraryGrouping.Artist, "Artist"), (LibraryGrouping.Album, "Album"),
        (LibraryGrouping.Genre, "Genre"), (LibraryGrouping.Year, "Year"), (LibraryGrouping.Decade, "Decade"), (LibraryGrouping.Folder, "Folder"),
        (LibraryGrouping.Added, "Date added"), (LibraryGrouping.Length, "Length"),
    ];

    /// <summary>
    /// A page's banner: the aside holding Back over the artwork, the artwork, the kicker, title and
    /// summary, the column of words (an album adds its artists there) and the row of actions.
    /// </summary>
    private sealed record LibraryHero(NSView View, NSStackView Aside, LibraryArtView Art, RexLabel Kicker, RexLabel Title,
        RexLabel Summary, NSStackView Words, NSStackView Actions);

    private void InitializeLibrary()
    {
        RexStore store;
        try
        {
            store = RexStore.Open(_roots.LibraryStore);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.Warning(LogSource, "The library could not be opened: " + ex.Message);
            _libraryTemporaryStore = true;
            store = RexStore.InMemory();
        }

        _libraryStore = store;
        _mediaLibrary = new Rex.Media.Library.MediaLibrary(store);
        _player.Library = _mediaLibrary;
        _libraryScanner = new LibraryScanner(_mediaLibrary, LibraryScanner.ListFolder, LibraryScanner.ProbeFile, _log);
        _mediaLibrary.Changed += (_, _) => OnWindowThread(RefreshLibrary);
        // Status-only changes do not require sorting the entire catalogue on the UI thread.
        _libraryScanner.StatusChanged += (_, _) => OnWindowThread(UpdateLibraryStatus);
        // The playing line wears the signal colour; only a change of item repaints it.
        _player.Changed += (_, _) => OnWindowThread(MarkLibraryPlaying);
        _libraryScanner.Request();
        _libraryPeriodicScan = NSTimer.CreateRepeatingScheduledTimer(TimeSpan.FromMinutes(5),
            _ => _libraryScanner?.Request());
    }

    /// <summary>Shows the library over the stage, building it the first time, and puts the keyboard in its search.</summary>
    private void ShowLibrary()
    {
        if (_libraryHost is null)
        {
            return;
        }

        if (_libraryRoot is null)
        {
            BuildLibraryPane(_libraryHost);
        }

        RefreshLibrary(force: true);
        SetLibraryShown(true);
        _libraryHost.LayoutSubtreeIfNeeded();
        WireLibraryKeyViews();
        _window.MakeFirstResponder(_librarySearch);
        ShowChecks();
    }

    /// <summary>Hides the library and returns the keyboard to playback.</summary>
    private void HideLibrary()
    {
        SetLibraryShown(false);
        if (_libraryHost is not null && _window.FirstResponder is NSView focused && focused.IsDescendantOf(_libraryHost))
        {
            _window.MakeFirstResponder(_playPause);
        }

        ShowChecks();
    }

    /// <summary>The library's toggle: shows it while hidden, hides it while shown.</summary>
    private void ToggleLibrary()
    {
        if (LibraryShown)
        {
            HideLibrary();
        }
        else
        {
            ShowLibrary();
        }
    }

    /// <summary>Start from the first page when the search text, order, grouping or media category changes.</summary>
    private void ChangeLibraryView()
    {
        _libraryPageIndex = 0;
        RefreshLibrary();
        ScrollLibraryToStart();
    }

    /// <summary>Keep the first row visible after paging or changing a search/category.</summary>
    private void ScrollLibraryToStart()
    {
        if (_libraryScroll is { } scroll)
        {
            var clip = scroll.ContentView;
            clip.ScrollToPoint(CGPoint.Empty);
            scroll.ReflectScrolledClipView(clip);
        }
    }

    /// <summary>
    /// Keep background scanning independent of playback: a hidden library does not render 80
    /// AppKit rows on every catalogue change. Showing it forces a fresh snapshot.
    /// </summary>
    private void RefreshLibrary() => RefreshLibrary(force: false);

    private void RefreshLibrary(bool force)
    {
        if (_closingLibrary || _libraryRoot is null || _libraryScroll is null ||
            _librarySummary is null || _mediaLibrary is null ||
            (!force && !LibraryShown))
        {
            return;
        }

        var all = _mediaLibrary.Entries;
        var query = _librarySearch?.StringValue.Trim() ?? "";
        // Search the immutable snapshot before sorting a category. For a large
        // catalogue this avoids sorting every unrelated item on each keystroke,
        // while retaining the shared search rules and category-specific ordering.
        IReadOnlyList<LibraryEntry> candidates = query.Length == 0
            ? all
            : LibraryViews.Search(all, query, entry => [Path.GetFileName(Path.GetDirectoryName(entry.Path))]);
        var category = (int)(_libraryCategory?.IndexOfSelectedItem ?? 0);
        var folders = category == 7;
        var watchedFolders = folders
            ? _mediaLibrary.Folders.OrderBy(folder => folder, StringComparer.CurrentCultureIgnoreCase).ToArray()
            : Array.Empty<string>();
        IReadOnlyList<LibraryEntry> natural = category switch
        {
            1 => LibraryViews.Songs(candidates),
            2 => LibraryViews.Videos(candidates),
            3 => LibraryViews.Pictures(candidates),
            4 => LibraryViews.RecentlyAdded(candidates),
            5 => LibraryViews.RecentlyPlayed(candidates),
            6 => LibraryViews.ContinueWatching(candidates, _player.Memory.ResumePoint),
            7 => Array.Empty<LibraryEntry>(),
            _ => candidates.OrderBy(entry => entry.Title, StringComparer.CurrentCultureIgnoreCase).ToList(),
        };

        // The view's order and headings come from the same shared rules Windows arranges with.
        var sort = LibrarySortNames[Math.Clamp((int)(_librarySort?.IndexOfSelectedItem ?? 0), 0, LibrarySortNames.Length - 1)].Sort;
        var grouping = LibraryGroupingNames[Math.Clamp((int)(_libraryGrouping?.IndexOfSelectedItem ?? 0), 0, LibraryGroupingNames.Length - 1)].Grouping;
        var sorted = sort == LibrarySort.Natural ? natural : LibraryArrangement.Sort(natural, sort, descending: false);
        var sections = LibraryArrangement.Group(sorted, grouping, DateTime.UtcNow);
        IReadOnlyList<LibraryEntry> entries = grouping == LibraryGrouping.None ? sorted : [.. sections.SelectMany(section => section.Entries)];

        var lastPage = Math.Max(0, (entries.Count - 1) / LibraryPageSize);
        _libraryPageIndex = Math.Clamp(_libraryPageIndex, 0, lastPage);
        var shown = entries.Skip(_libraryPageIndex * LibraryPageSize).Take(LibraryPageSize).ToList();
        if (_libraryPreviousPage is not null && _libraryNextPage is not null && _libraryPageSummary is not null && _libraryPager is not null)
        {
            _libraryPreviousPage.Enabled = _libraryPageIndex > 0;
            _libraryNextPage.Enabled = _libraryPageIndex < lastPage;
            _libraryPageSummary.StringValue = $"Page {_libraryPageIndex + 1} of {lastPage + 1}";
            _libraryPageSummary.AccessibilityLabel = "Library results " + _libraryPageSummary.StringValue;
            // One page needs no pager; the controls keep answering scripts while it is hidden.
            if (_libraryPagerHolder is not null)
            {
                _libraryPagerHolder.Hidden = lastPage == 0;
            }
        }

        _libraryCurrentSelection = entries;
        var empty = (folders ? watchedFolders.Length == 0 : shown.Count == 0)
            ? LibraryEmptyFor(category, query.Length > 0)
            : null;
        var showPlays = category == 1 && ViewFacts.SongsShowPlays(entries.Select(entry => entry.Plays));
        var key = string.Join("|", category, sort, grouping, showPlays, empty?.Text, empty?.Heading,
            string.Join("\n", watchedFolders));
        // Scanner progress and unrelated catalogue changes should not destroy focused
        // native buttons or reset scrolling when this page still contains the same rows.
        if (_libraryRenderedPage != _libraryPageIndex ||
            _libraryRenderedKey != key ||
            !_libraryRenderedEntries.SequenceEqual(shown) || category == 6)
        {
            _libraryRenderedPage = _libraryPageIndex;
            _libraryRenderedKey = key;
            _libraryRenderedEntries = shown;
            RenderLibraryResults(shown, watchedFolders, category, grouping, sections, showPlays, empty);
        }

        _libraryMatchingCount = folders ? watchedFolders.Length : entries.Count;
        _libraryTotalCount = all.Count;
        _libraryFolderCount = _mediaLibrary.Folders.Count;
        _libraryVisibleResultCount = shown.Count;
        UpdateLibraryHero(all, entries, category, query, watchedFolders.Length);
        UpdateLibraryStatus();
        RefreshLibrarySidebar();
    }

    /// <summary>The list page's banner and actions for the view shown: its glyph, kicker, title and what it holds.</summary>
    private void UpdateLibraryHero(IReadOnlyList<LibraryEntry> all, IReadOnlyList<LibraryEntry> entries, int category, string query, int folders)
    {
        if (_libraryListHero is not { } hero)
        {
            return;
        }

        var item = LibrarySidebarCatalog.FirstOrDefault(candidate => candidate.Category == category);
        string kicker, title, summary;
        if (query.Length > 0)
        {
            (kicker, title, summary) = ("SEARCH", query, LibraryActions.Count(entries.Count, "result"));
        }
        else if (category == 0)
        {
            var (music, videos, pictures) = LibraryMemo.Counts(all);
            (kicker, title, summary) = ("YOUR LIBRARY", item.Title, ViewFacts.Home(music, videos, pictures, 0, 0, 0));
        }
        else if (category == 7)
        {
            (kicker, title, summary) = ("LIBRARY", item.Title, LibraryActions.Count(folders, "folder") + " watched");
        }
        else
        {
            var noun = category switch { 3 => "picture", 2 or 6 => "video", _ => "item" };
            (kicker, title, summary) = ("LIBRARY", item.Title, ViewFacts.Join(LibraryActions.Count(entries.Count, noun),
                category == 3 ? null : ViewFacts.Length(entries.Select(entry => entry.Duration))));
        }

        hero.Kicker.Text = kicker;
        hero.Title.StringValue = title;
        hero.Summary.StringValue = summary;
        hero.Summary.Hidden = summary.Length == 0;
        hero.Art.Glyph = query.Length > 0 ? "magnifyingglass" : item.Symbol;
        var folderView = category == 7;
        if (_libraryPlayAll is not null && _libraryShuffleAll is not null && _libraryQueueAll is not null && _libraryFolderActions is not null)
        {
            _libraryPlayAll.Hidden = _libraryShuffleAll.Hidden = _libraryQueueAll.Hidden = folderView;
            _libraryPlayAll.Enabled = _libraryShuffleAll.Enabled = _libraryQueueAll.Enabled = entries.Count > 0;
            _libraryFolderActions.Hidden = !folderView;
        }

        if (_librarySort is not null && _libraryGrouping is not null)
        {
            _librarySort.Hidden = _libraryGrouping.Hidden = folderView;
        }
    }

    /// <summary>Plays the view from its first item, or from a random one with shuffle turned on.</summary>
    private void PlayLibrarySelection(bool shuffled)
    {
        var selection = _libraryCurrentSelection;
        if (selection.Count == 0)
        {
            Say("There is nothing here to play yet.");
            return;
        }

        if (shuffled)
        {
            _player.Playlist.Shuffle = true;
        }

        PlayLibraryEntry(selection[shuffled ? Random.Shared.Next(selection.Count) : 0], selection);
        if (shuffled)
        {
            Say("Shuffle on");
        }
    }

    private string LibraryEntryDescription(LibraryEntry entry)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(entry.Artist)) parts.Add(entry.Artist);
        if (!string.IsNullOrWhiteSpace(entry.Album) && entry.Album != entry.Artist) parts.Add(entry.Album);
        if (entry.Kind == LibraryKind.Music && entry.Track is { } track) parts.Add("Track " + track);
        var width = entry.VideoWidth ?? entry.PictureWidth;
        var height = entry.VideoHeight ?? entry.PictureHeight;
        if (entry.Kind is LibraryKind.Video or LibraryKind.Picture && width is not null && height is not null)
        {
            parts.Add($"{width}×{height}");
        }
        if (entry.Plays > 0) parts.Add(entry.Plays == 1 ? "Played once" : $"Played {entry.Plays} times");
        if (_player.Memory.ResumePoint(entry.Path) is { } resume) parts.Add("Continue at " + TimeText.Format(resume));
        if (parts.Count == 0) parts.Add(Path.GetFileName(Path.GetDirectoryName(entry.Path)) ?? "Local media");
        return string.Join(" • ", parts);
    }

    private NSImage LibraryThumbnail(LibraryEntry entry)
    {
        if (_libraryThumbnails.TryGetValue(entry.Path, out var cached)) return cached;
        // AppKit supplies a decoded preview or embedded artwork where the system has one,
        // and a file-type image otherwise. Only visible pages enter this bounded cache.
        var image = NSWorkspace.SharedWorkspace.IconForFile(entry.Path);
        image.Size = new CGSize(48, 48);
        _libraryThumbnails[entry.Path] = image;
        _libraryThumbnailOrder.Enqueue(entry.Path);
        while (_libraryThumbnailOrder.Count > LibraryThumbnailLimit)
        {
            var old = _libraryThumbnailOrder.Dequeue();
            if (_libraryThumbnails.Remove(old, out var removed)) removed.Dispose();
        }
        return image;
    }

    /// <summary>
    /// Scanner progress changes frequently. Update only the short text and its accessibility
    /// announcement; the catalogue Changed event separately refreshes the visible rows.
    /// </summary>
    private void UpdateLibraryStatus()
    {
        if (_closingLibrary || _librarySummary is null)
        {
            return;
        }

        var status = _libraryScanner?.Status;
        var writeProblem = _libraryStore?.WriteProblem;
        // Show counts at narrow widths even if a scan names a long folder path.
        var persistenceWarning = _libraryTemporaryStore ? "Changes will not be saved • "
            : writeProblem is not null ? "Changes could not be saved • " : "";
        _librarySummary.StringValue = persistenceWarning
            + $"{_libraryMatchingCount} matching • {_libraryTotalCount} total • {_libraryFolderCount} folders"
            + (_libraryMatchingCount > LibraryPageSize
                ? $" • results {_libraryPageIndex * LibraryPageSize + 1}-{_libraryPageIndex * LibraryPageSize + _libraryVisibleResultCount}"
                : "")
            + (string.IsNullOrEmpty(status) ? "" : " • " + status)
            + (writeProblem is null ? "" : " • " + writeProblem);
        _librarySummary.ToolTip = _librarySummary.StringValue;
        _librarySummary.AccessibilityLabel = "Library status: " + _librarySummary.StringValue;
    }

    /// <summary>
    /// The one native focus loop both the wiring and the keyboard probe follow, in the order the
    /// pane reads: the navigation column, the banner's Back and actions, Back to player, the
    /// search, the view options, the current page of results, then the pager. While an album's
    /// or artist's page shows, its own controls follow Back instead. Null while any of the fixed
    /// controls is missing.
    /// </summary>
    private List<NSView>? LibraryKeyViewOrder()
    {
        if (_libraryNavToggle is null || _libraryNavigationBack is null ||
            _librarySearch is null || _libraryCategory is null || _librarySort is null || _libraryGrouping is null ||
            _libraryAddButton is null || _libraryRescanButton is null || _libraryBackButton is null ||
            _libraryPlayAll is null || _libraryShuffleAll is null || _libraryQueueAll is null || _libraryMore is null ||
            _libraryPreviousPage is null || _libraryNextPage is null)
        {
            return null;
        }

        var order = new List<NSView> { _libraryNavToggle };
        order.AddRange(_libraryNavItems.Select(item => (NSView)item.Item));
        order.Add(_libraryAddButton);
        order.Add(_libraryNavigationBack);
        if (_libraryCurrentPage is LibraryDetailPage detail)
        {
            order.Add(_libraryBackButton);
            order.AddRange(detail.KeyViews);
            return order;
        }

        order.AddRange([_libraryPlayAll, _libraryShuffleAll, _libraryQueueAll]);
        order.AddRange(_libraryFolderActionKeys);
        order.AddRange([_libraryMore, _libraryBackButton, _librarySearch, _librarySort, _libraryGrouping, _libraryCategory]);
        order.AddRange(_libraryResultKeyViews);
        order.AddRange([_libraryPreviousPage, _libraryNextPage]);
        return order;
    }

    /// <summary>
    /// Links the pane's controls in one closed, reversible loop: its last control leads back to
    /// its first. The result rows are replaced after scans or filters, so AppKit's automatic
    /// geometric loop must not retain stale buttons.
    /// </summary>
    private void WireLibraryKeyViews()
    {
        if (_libraryRoot is null || LibraryKeyViewOrder() is not { } order)
        {
            return;
        }

        for (var i = 0; i < order.Count; i++)
        {
            order[i].NextKeyView = order[(i + 1) % order.Count];
        }
    }

    private void PlayLibraryEntry(LibraryEntry chosen, IReadOnlyList<LibraryEntry> selection)
    {
        // A background rescan can remove a file after its native row was constructed.
        // Never silently start an unrelated first item when that selected row is stale.
        var at = -1;
        for (var index = 0; index < selection.Count; index++)
        {
            if (string.Equals(selection[index].Path, chosen.Path, StringComparison.OrdinalIgnoreCase))
            {
                at = index;
                break;
            }
        }

        if (at < 0)
        {
            Say("This library result is no longer available. Refresh the library and try again.");
            RefreshLibrary();
            return;
        }

        if (!File.Exists(chosen.Path))
        {
            _libraryScanner?.Request();
            Say("This file is missing. Rescanning your library.");
            return;
        }

        _player.PlayFromLibrary(selection, at);
        HideLibrary();
    }

    /// <summary>
    /// Verify that the first result's Play and Queue controls fit in the actual
    /// AppKit scroll viewport, including with a long title at minimum window width.
    /// </summary>
    private AutomationReply VerifyLibraryResultLayout()
    {
        if (!LibraryShown || _libraryScroll is null ||
            _libraryFirstPlay is null || _libraryFirstQueue is null)
        {
            return AutomationReply.Failed("Open the library with a matching media result.");
        }

        _libraryRoot?.LayoutSubtreeIfNeeded();
        var play = _libraryFirstPlay.ConvertRectToView(_libraryFirstPlay.Bounds, _libraryScroll);
        var queue = _libraryFirstQueue.ConvertRectToView(_libraryFirstQueue.Bounds, _libraryScroll);
        var visibleWidth = _libraryScroll.ContentView.Bounds.Width;
        if (visibleWidth <= 0 || play.Left < -1 || queue.Left < play.Right - 1 ||
            queue.Right > visibleWidth + 1)
        {
            return AutomationReply.Failed(
                $"Native result controls clip or overlap: play {play}, queue {queue}, viewport {visibleWidth}.");
        }

        return new AutomationReply { Ok = true, Text = "Play and Queue fit inside the native results viewport." };
    }

    /// <summary>Report real AppKit control labels and enabled states for automation checks.</summary>
    private AutomationReply LibraryControlTree()
    {
        if (!LibraryShown || _librarySearch is null || _libraryCategory is null ||
            _libraryScroll is null || _libraryPreviousPage is null || _libraryNextPage is null ||
            _libraryPageSummary is null || _librarySummary is null)
        {
            return AutomationReply.Failed("Open the library to inspect its controls.");
        }

        return new AutomationReply
        {
            Ok = true,
            Controls =
            [
                new UiControl { Id = "LibrarySidebar", Kind = "list", Name = _librarySidebarScroll?.AccessibilityLabel ?? "" },
                new UiControl { Id = "LibraryNavigationBack", Kind = "button", Name = ((NSView?)_libraryNavigationBack)?.AccessibilityLabel ?? "", Enabled = _libraryNavigationBack?.Enabled == true },
                new UiControl { Id = "LibrarySearch", Kind = "search", Name = ((NSView)_librarySearch).AccessibilityLabel ?? "", Text = _librarySearch.StringValue },
                new UiControl { Id = "LibraryCategory", Kind = "choice", Name = ((NSView)_libraryCategory).AccessibilityLabel ?? "", Text = _libraryCategory.TitleOfSelectedItem },
                new UiControl { Id = "LibraryResults", Kind = "list", Name = _libraryScroll.AccessibilityLabel ?? "" },
                new UiControl { Id = "LibrarySummary", Kind = "text", Name = _librarySummary.AccessibilityLabel ?? "", Text = _librarySummary.StringValue },
                new UiControl { Id = "LibraryPage", Kind = "text", Name = _libraryPageSummary.AccessibilityLabel ?? "", Text = _libraryPageSummary.StringValue },
                new UiControl { Id = "LibraryPrevious", Kind = "button", Name = ((NSView)_libraryPreviousPage).AccessibilityLabel ?? "", Enabled = _libraryPreviousPage.Enabled },
                new UiControl { Id = "LibraryNext", Kind = "button", Name = ((NSView)_libraryNextPage).AccessibilityLabel ?? "", Enabled = _libraryNextPage.Enabled },
                new UiControl { Id = "LibraryAddFolder", Kind = "button", Name = ((NSView?)_libraryAddButton)?.AccessibilityLabel ?? "", Enabled = _libraryAddButton?.Enabled == true },
                new UiControl { Id = "LibraryRescan", Kind = "button", Name = ((NSView?)_libraryRescanButton)?.AccessibilityLabel ?? "", Enabled = _libraryRescanButton?.Enabled == true },
                new UiControl { Id = "LibraryBack", Kind = "button", Name = ((NSView?)_libraryBackButton)?.AccessibilityLabel ?? "", Enabled = _libraryBackButton?.Enabled == true },
                new UiControl { Id = "LibraryFirstPlay", Kind = "button", Name = ((NSView?)_libraryFirstPlay)?.AccessibilityLabel ?? "", Enabled = _libraryFirstPlay is not null },
                new UiControl { Id = "LibraryFirstQueue", Kind = "button", Name = ((NSView?)_libraryFirstQueue)?.AccessibilityLabel ?? "", Enabled = _libraryFirstQueue is not null },
            ],
        };
    }

    /// <summary>
    /// Verify the native key-view link, then traverse it if macOS permits controls as Tab stops.
    /// AppKit excludes buttons and pop-ups from the Tab loop unless full keyboard navigation is on.
    /// RecalculateKeyViewLoop here would discard the explicitly connected search/category link.
    /// </summary>
    private AutomationReply ProbeLibraryKeyboard()
    {
        if (!LibraryShown || _librarySearch is null || _libraryCategory is null)
        {
            return AutomationReply.Failed("Open the library before checking keyboard navigation.");
        }

        if (LibraryKeyViewOrder() is not { } order)
        {
            return AutomationReply.Failed("The library action or page controls are missing.");
        }

        for (var i = 0; i < order.Count; i++)
        {
            var following = order[(i + 1) % order.Count];
            if (order[i].NextKeyView?.Handle != following.Handle ||
                following.PreviousKeyView?.Handle != order[i].Handle)
            {
                return AutomationReply.Failed($"Native library key-view link {i + 1} of {order.Count} is broken.");
            }
        }

        _window.MakeFirstResponder(_librarySearch);
        var initialResponder = _window.FirstResponder?.Handle;
        var next = _librarySearch.NextValidKeyView;
        if (next is null || next.Handle == _librarySearch.Handle)
        {
            // On a default macOS account only text-editing views are valid Tab stops.
            // Do not confuse that OS preference with a broken key-view chain, or claim
            // that this automation has exercised a physical Tab/Shift-Tab walkthrough.
            return new AutomationReply
            {
                Ok = true,
                Text = "Native search/category key-view link verified; full keyboard navigation requires macOS settings.",
            };
        }

        _window.SelectKeyViewFollowingView(_librarySearch);
        if (_window.FirstResponder?.Handle != next.Handle)
        {
            _window.MakeFirstResponder(_librarySearch);
            return AutomationReply.Failed("AppKit did not advance to the next eligible key view.");
        }

        _window.SelectKeyViewPrecedingView(next);
        var reversed = _window.FirstResponder?.Handle == initialResponder;
        _window.MakeFirstResponder(_librarySearch);
        return reversed
            ? new AutomationReply { Ok = true, Text = "Native key-view link verified; AppKit advanced and reversed focus." }
            : AutomationReply.Failed("AppKit did not return focus to the search field.");
    }

    private AutomationReply PressLibraryControl(string? target)
    {
        NSButton? button = target switch
        {
            "LibraryPrevious" => _libraryPreviousPage,
            "LibraryNext" => _libraryNextPage,
            "LibraryFirstPlay" => _libraryFirstPlay,
            "LibraryFirstQueue" => _libraryFirstQueue,
            "LibraryRescan" => _libraryRescanButton,
            "LibraryBack" => _libraryBackButton,
            "LibraryNavigationBack" => _libraryNavigationBack,
            _ => null,
        };
        if (!LibraryShown || button?.Enabled != true)
        {
            return AutomationReply.Failed("The library control is not available: " + target);
        }

        button.PerformClick(button);
        return new AutomationReply { Ok = true, Text = _libraryPageSummary?.StringValue };
    }

    private AutomationReply ChangeLibraryCategory(double? value)
    {
        if (!LibraryShown || _libraryCategory is null || value is not { } index ||
            !double.IsFinite(index) || index != Math.Truncate(index) || index < 0 || index >= 8)
        {
            return AutomationReply.Failed("Choose a valid library category index.");
        }

        _libraryCategory.SelectItem((nint)index);
        ChangeLibraryView();
        return new AutomationReply { Ok = true, Text = _librarySummary?.StringValue };
    }

    /// <summary>
    /// Sizes the player window so the library's page, beside its navigation column, is
    /// <paramref name="width"/> wide: the width scripts name is the page's. The narrowest page
    /// leaves the navigation as its icon rail, as Windows does below its breakpoint.
    /// </summary>
    private AutomationReply ResizeLibraryWindow(double? width)
    {
        if (!LibraryShown || _libraryHost is null || width is not { } requested ||
            !double.IsFinite(requested) || requested < MinimumLibraryPageWidth)
        {
            return AutomationReply.Failed("Open the library and choose a supported width.");
        }

        var pane = requested + (_libraryNavCollapsed || requested + LibraryNavigationCompactWidth < LibraryNavigationBreakpoint
            ? LibraryNavigationCompactWidth
            : LibraryNavigationExpandedWidth);
        var frame = _window.Frame;
        var grow = pane - (double)_libraryHost.Frame.Width;
        _window.SetFrame(new CGRect(frame.X, frame.Y, (double)frame.Width + grow, frame.Height), true);
        RefreshLibrary();
        return new AutomationReply { Ok = true };
    }

    /// <summary>Use top-left coordinates for scrolling native result rows.</summary>
    private sealed class TopAlignedLibraryDocument : NSView
    {
        public override bool IsFlipped => true;
    }

    private void CloseLibrary()
    {
        _closingLibrary = true;
        _libraryPeriodicScan?.Invalidate();
        _libraryPeriodicScan?.Dispose();
        _libraryPeriodicScan = null;
        _libraryScanner?.Dispose();
        _libraryScanner = null;
        CloseLibrarySidebar();
        CloseLibraryNavigation();
        _libraryRoot?.RemoveFromSuperview();
        _libraryRoot?.Dispose();
        _libraryRoot = null;
        foreach (var image in _libraryThumbnails.Values) image.Dispose();
        _libraryThumbnails.Clear();
        _libraryThumbnailOrder.Clear();
    }
}
