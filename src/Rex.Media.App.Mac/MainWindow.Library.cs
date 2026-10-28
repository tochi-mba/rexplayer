using Rex.Media.AppCore.Automation;
using Rex.Media.AppCore.Library;
using Rex.Media.AppCore.Player;
using Rex.Media.Library;

namespace Rex.Media.App.Mac;

/// <summary>
/// The native Mac library browser over the same persisted catalogue and scanner used on Windows.
/// It deliberately uses AppKit views rather than embedding a web view or duplicating catalogue rules.
/// </summary>
public sealed partial class MainWindow
{
    private Rex.Media.Library.MediaLibrary? _mediaLibrary;
    private LibraryScanner? _libraryScanner;
    private NSTimer? _libraryPeriodicScan;
    private NSWindow? _libraryWindow;
    private NSSearchField? _librarySearch;
    private NSPopUpButton? _libraryCategory;
    private NSScrollView? _libraryScroll;
    private NSTextField? _librarySummary;
    private NSTextField? _libraryPageSummary;
    private NSButton? _libraryPreviousPage;
    private NSButton? _libraryNextPage;
    private NSButton? _libraryFirstPlay;
    private NSButton? _libraryFirstQueue;
    private NSButton? _libraryAddButton;
    private NSButton? _libraryRescanButton;
    private NSButton? _libraryBackButton;
    private readonly List<NSView> _libraryResultKeyViews = [];
    private IReadOnlyList<LibraryEntry> _libraryCurrentSelection = [];
    private int _libraryMatchingCount;
    private int _libraryTotalCount;
    private int _libraryFolderCount;
    private int _libraryVisibleResultCount;
    private IReadOnlyList<LibraryEntry> _libraryRenderedEntries = [];
    private int _libraryRenderedPage = -1;
    private string? _libraryRenderedEmptyLabel;
    private int _libraryPageIndex;
    private const int LibraryPageSize = 80;
    private bool _closingLibrary;
    private bool _libraryTemporaryStore;
    private RexStore? _libraryStore;
    private readonly Dictionary<string, NSImage> _libraryThumbnails = new(StringComparer.OrdinalIgnoreCase);
    private readonly Queue<string> _libraryThumbnailOrder = new();
    private const int LibraryThumbnailLimit = 160;

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
        _libraryScanner.Request();
        _libraryPeriodicScan = NSTimer.CreateRepeatingScheduledTimer(TimeSpan.FromMinutes(5),
            _ => _libraryScanner?.Request());
    }

    private void ShowLibrary()
    {
        if (_libraryWindow is null)
        {
            _libraryWindow = new NSWindow(new CGRect(0, 0, 820, 620),
                NSWindowStyle.Titled | NSWindowStyle.Closable | NSWindowStyle.Miniaturizable | NSWindowStyle.Resizable,
                NSBackingStore.Buffered, false)
            {
                Title = "Your library - rexplayer",
                MinSize = new CGSize(590, 460),
            };
            _libraryWindow.ReleaseWhenClosed(false);
            // The key-view order is explicit: AppKit's automatic geometric ordering
            // can discard links after we replace paginated result rows.
            _libraryWindow.AutorecalculatesKeyViewLoop = false;

            var headline = NSTextField.CreateLabel("Your library");
            headline.Font = NSFont.SystemFontOfSize(26, NSFontWeight.Semibold)!;
            var description = NSTextField.CreateLabel("Your files stay on this Mac. Choose folders to keep them organised.");
            description.TextColor = NSColor.SecondaryLabel;
            var add = NSButton.CreateButton("Add folder…", AddLibraryFolder);
            var rescan = NSButton.CreateButton("Rescan", () => _libraryScanner?.Request());
            var close = NSButton.CreateButton("Back to player", HideLibrary);
            _libraryAddButton = add;
            _libraryRescanButton = rescan;
            _libraryBackButton = close;
            ((NSView)add).AccessibilityLabel = "Add a folder to the library";
            ((NSView)rescan).AccessibilityLabel = "Rescan watched library folders";
            ((NSView)close).AccessibilityLabel = "Back to player";
            var actions = NSStackView.FromViews([add, rescan, close]);
            actions.Orientation = NSUserInterfaceLayoutOrientation.Horizontal;
            actions.Spacing = 10;
            var suggestions = NSStackView.FromViews(
            [
                SuggestedLibraryFolder("Add Music", Environment.SpecialFolder.MyMusic),
                SuggestedLibraryFolder("Add Videos", Environment.SpecialFolder.MyVideos),
                SuggestedLibraryFolder("Add Pictures", Environment.SpecialFolder.MyPictures),
            ]);
            suggestions.Orientation = NSUserInterfaceLayoutOrientation.Horizontal;
            suggestions.Spacing = 8;

            _librarySearch = new NSSearchField(new CGRect(0, 0, 400, 30))
            {
                PlaceholderString = "Search titles, artists, albums and folders",
            };
            _librarySearch.Changed += (_, _) => ChangeLibraryView();
            _librarySearch.Activated += (_, _) => ChangeLibraryView();
            _librarySearch.ToolTip = "Type to filter your local library";
            _librarySearch.AccessibilityLabel = "Search library";

            _libraryCategory = new NSPopUpButton(new CGRect(0, 0, 220, 30), false);
            foreach (var name in new[] { "All media", "Music", "Videos", "Pictures", "Recently added", "Recently played", "Continue watching", "Folders" })
            {
                _libraryCategory.AddItem(name);
            }

            _libraryCategory.Activated += (_, _) => ChangeLibraryView();
            ((NSView)_libraryCategory).AccessibilityLabel = "Media category";
            var filters = NSStackView.FromViews([_librarySearch, _libraryCategory]);
            filters.Orientation = NSUserInterfaceLayoutOrientation.Horizontal;
            filters.Spacing = 12;
            _librarySearch.WidthAnchor.ConstraintGreaterThanOrEqualTo(220).Active = true;
            _libraryCategory.WidthAnchor.ConstraintEqualTo(190).Active = true;

            _librarySummary = NSTextField.CreateLabel("");
            _librarySummary.TextColor = NSColor.SecondaryLabel;
            _librarySummary.Font = NSFont.SystemFontOfSize(12, NSFontWeight.Regular)!;
            _librarySummary.LineBreakMode = NSLineBreakMode.TruncatingTail;

            _libraryPreviousPage = NSButton.CreateButton("Previous", () =>
            {
                if (_libraryPageIndex > 0)
                {
                    _libraryPageIndex--;
                    RefreshLibrary();
                    ScrollLibraryToStart();
                }
            });
            _libraryPreviousPage.ToolTip = "Show the previous library results";
            ((NSView)_libraryPreviousPage).AccessibilityLabel = "Previous results page";
            _libraryNextPage = NSButton.CreateButton("Next", () =>
            {
                _libraryPageIndex++;
                RefreshLibrary();
                ScrollLibraryToStart();
            });
            _libraryNextPage.ToolTip = "Show the next library results";
            ((NSView)_libraryNextPage).AccessibilityLabel = "Next results page";
            _libraryPageSummary = NSTextField.CreateLabel("");
            _libraryPageSummary.TextColor = NSColor.SecondaryLabel;

            // Search/Category are linked once the controls belong to the window.
            // AppKit must first calculate the dynamic row controls in the key loop.
            var pages = NSStackView.FromViews([_libraryPreviousPage, _libraryPageSummary, _libraryNextPage]);
            pages.Orientation = NSUserInterfaceLayoutOrientation.Horizontal;
            pages.Spacing = 12;

            _libraryScroll = new NSScrollView(new CGRect(0, 0, 740, 390))
            {
                HasVerticalScroller = true,
            };
            _libraryScroll.HeightAnchor.ConstraintGreaterThanOrEqualTo(170).Active = true;
            _libraryScroll.AccessibilityLabel = "Library results";

            var stack = NSStackView.FromViews([headline, description, actions, suggestions, filters, _librarySummary, pages, _libraryScroll]);
            stack.Orientation = NSUserInterfaceLayoutOrientation.Vertical;
            stack.Alignment = NSLayoutAttribute.Leading;
            stack.Spacing = 14;
            stack.EdgeInsets = new NSEdgeInsets(22, 24, 22, 24);
            stack.TranslatesAutoresizingMaskIntoConstraints = false;
            _libraryWindow.ContentView = stack;
            _libraryScroll.WidthAnchor.ConstraintEqualTo(stack.WidthAnchor, 1, -48).Active = true;
            _librarySummary.WidthAnchor.ConstraintEqualTo(stack.WidthAnchor, 1, -48).Active = true;
            _libraryWindow.Center();
        }

        RefreshLibrary(force: true);
        _libraryWindow.MakeKeyAndOrderFront(null);
        WireLibraryKeyViews();
        _libraryWindow.MakeFirstResponder(_librarySearch);
        ShowChecks();
    }

    private NSButton SuggestedLibraryFolder(string title, Environment.SpecialFolder specialFolder)
    {
        var path = Environment.GetFolderPath(specialFolder);
        var button = NSButton.CreateButton(title, () =>
        {
            if (!string.IsNullOrWhiteSpace(path))
            {
                var answer = AddLibraryFolders([path]);
                Say(answer.Ok ? title[4..] + " is now watched." : answer.Error ?? "That folder could not be added.");
            }
        });
        button.Enabled = !string.IsNullOrWhiteSpace(path) && Directory.Exists(path);
        button.ToolTip = button.Enabled ? "Watch " + path : "This standard folder is not available";
        ((NSView)button).AccessibilityLabel = title + " folder suggestion";
        return button;
    }

    private void AddLibraryFolder()
    {
        if (_mediaLibrary is null)
        {
            return;
        }

        var panel = NSOpenPanel.OpenPanel;
        panel.AllowsMultipleSelection = true;
        panel.CanChooseFiles = false;
        panel.CanChooseDirectories = true;
        panel.Begin(response =>
        {
            if (response != 1) return;
            var reply = AddLibraryFolders(panel.Urls.Select(url => url.Path!).ToList());
            if (!reply.Ok)
            {
                Say(reply.Error ?? "Could not add a library folder.");
            }
        });
    }

    /// <summary>
    /// The native folder picker and Mac automation both use the same registration path.
    /// Scan only after a folder has been saved in the persistent library store.
    /// </summary>
    private AutomationReply AddLibraryFolders(IReadOnlyCollection<string> folders)
    {
        if (_mediaLibrary is null || folders.Count == 0 || folders.Any(folder => !Directory.Exists(folder)))
        {
            return AutomationReply.Failed("Select one or more existing folders.");
        }

        var added = 0;
        var errors = new List<string>();
        foreach (var folder in folders)
        {
            try
            {
                if (_mediaLibrary.AddFolder(folder))
                {
                    added++;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                _log.Warning(LogSource, "Cannot add library folder: " + ex.Message);
                errors.Add(ex.Message);
            }
        }

        if (added > 0)
        {
            _libraryScanner?.Request();
        }

        RefreshLibrary();
        if (_libraryTemporaryStore)
        {
            return AutomationReply.Failed(
                "The library store could not be opened. Watched folders will be forgotten when the app quits.");
        }

        if (_libraryStore?.WriteProblem is { } writeProblem)
        {
            return AutomationReply.Failed("Library folder changes are only in memory: " + writeProblem);
        }

        return errors.Count == 0
            ? new AutomationReply { Ok = true, Text = $"Added {added} watched folder(s)." }
            : AutomationReply.Failed("Could not add one or more folders: " + string.Join("; ", errors));
    }

    /// <summary>Start from the first page when the search text or media category changes.</summary>
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
    /// Keep background scanning independent of playback: a hidden library window does not
    /// render 80 AppKit rows on every catalogue change. Opening it forces a fresh snapshot.
    /// </summary>
    private void RefreshLibrary() => RefreshLibrary(force: false);

    private void RefreshLibrary(bool force)
    {
        if (_closingLibrary || _libraryWindow is null || _libraryScroll is null ||
            _librarySummary is null || _mediaLibrary is null ||
            (!force && !_libraryWindow.IsVisible))
        {
            return;
        }

        var all = _mediaLibrary.Entries;
        var query = _librarySearch?.StringValue.Trim();
        // Search the immutable snapshot before sorting a category. For a large
        // catalogue this avoids sorting every unrelated item on each keystroke,
        // while retaining the shared search rules and category-specific ordering.
        IReadOnlyList<LibraryEntry> candidates = string.IsNullOrEmpty(query)
            ? all
            : LibraryViews.Search(all, query, entry => [Path.GetFileName(Path.GetDirectoryName(entry.Path))]);
        var category = (int)(_libraryCategory?.IndexOfSelectedItem ?? 0);
        var watchedFolders = category == 7
            ? _mediaLibrary.Folders.OrderBy(folder => folder, StringComparer.CurrentCultureIgnoreCase).ToArray()
            : Array.Empty<string>();
        IReadOnlyList<LibraryEntry> entries = category switch
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

        var lastPage = Math.Max(0, (entries.Count - 1) / LibraryPageSize);
        _libraryPageIndex = Math.Clamp(_libraryPageIndex, 0, lastPage);
        var shown = entries.Skip(_libraryPageIndex * LibraryPageSize).Take(LibraryPageSize).ToList();
        if (_libraryPreviousPage is not null && _libraryNextPage is not null && _libraryPageSummary is not null)
        {
            _libraryPreviousPage.Enabled = _libraryPageIndex > 0;
            _libraryNextPage.Enabled = _libraryPageIndex < lastPage;
            _libraryPageSummary.StringValue = $"Page {_libraryPageIndex + 1} of {lastPage + 1}";
            _libraryPageSummary.AccessibilityLabel = "Library results " + _libraryPageSummary.StringValue;
        }
        _libraryCurrentSelection = entries;
        var emptyLabel = category == 7
            ? watchedFolders.Length == 0 ? "No watched folders. Add a folder to get started." : null
            : shown.Count == 0
                ? all.Count == 0 ? "No media yet. Add a music, video or picture folder to get started."
                    : "Nothing matches this view. Try another category or search."
                : null;
        // Scanner progress and unrelated catalogue changes should not destroy focused
        // native buttons or reset scrolling when this page still contains the same rows.
        if (_libraryRenderedPage != _libraryPageIndex ||
            _libraryRenderedEmptyLabel != emptyLabel ||
            !_libraryRenderedEntries.SequenceEqual(shown) || category == 6)
        {
            _libraryRenderedPage = _libraryPageIndex;
            _libraryRenderedEmptyLabel = emptyLabel;
            _libraryRenderedEntries = shown;
            var views = new List<NSView>();
            _libraryFirstPlay = null;
            _libraryFirstQueue = null;
            _libraryResultKeyViews.Clear();
            if (shown.Count == 0 && watchedFolders.Length == 0)
            {
                var hint = NSTextField.CreateLabel(emptyLabel!);
                hint.TextColor = NSColor.SecondaryLabel;
                views.Add(hint);
            }
    
            foreach (var folder in watchedFolders)
            {
                var name = NSTextField.CreateLabel(folder);
                name.LineBreakMode = NSLineBreakMode.TruncatingMiddle;
                name.ToolTip = folder;
                name.WidthAnchor.ConstraintLessThanOrEqualTo(370).Active = true;
                var stop = NSButton.CreateButton("Stop watching: " + Path.GetFileName(folder), () =>
                {
                    if (_mediaLibrary.RemoveFolder(folder))
                    {
                        _libraryScanner?.Request();
                        RefreshLibrary(force: true);
                    }
                });
                ((NSView)stop).AccessibilityLabel = "Stop watching folder: " + folder;
                stop.ToolTip = "Stop watching this folder without deleting its files";
                _libraryResultKeyViews.Add(stop);
                var row = NSStackView.FromViews([name, stop]);
                row.Orientation = NSUserInterfaceLayoutOrientation.Horizontal;
                row.Spacing = 12;
                views.Add(row);
            }

            foreach (var entry in shown)
            {
                var title = entry.Title;
                var secondary = LibraryEntryDescription(entry);
                var play = NSButton.CreateButton(title, () => PlayLibraryEntry(entry, _libraryCurrentSelection));
                _libraryFirstPlay ??= play;
                _libraryResultKeyViews.Add(play);
                play.ToolTip = entry.Path;
                ((NSView)play).AccessibilityLabel = "Play: " + entry.Title;
                play.Alignment = NSTextAlignment.Left;
                play.LineBreakMode = NSLineBreakMode.TruncatingTail;
                play.WidthAnchor.ConstraintGreaterThanOrEqualTo(250).Active = true;
                // Very long titles must not push Queue out of the minimum-width viewport.
                play.WidthAnchor.ConstraintLessThanOrEqualTo(370).Active = true;
                var detail = NSTextField.CreateLabel(secondary);
                detail.TextColor = NSColor.SecondaryLabel;
                detail.Font = NSFont.SystemFontOfSize(11, NSFontWeight.Regular)!;
                detail.LineBreakMode = NSLineBreakMode.TruncatingTail;
                // A very long artist/album/folder name must not widen the metadata
                // column beyond Play, or the separate Queue action is pushed offscreen.
                detail.WidthAnchor.ConstraintLessThanOrEqualTo(370).Active = true;
                var words = NSStackView.FromViews([play, detail]);
                words.Orientation = NSUserInterfaceLayoutOrientation.Vertical;
                words.Alignment = NSLayoutAttribute.Leading;
                words.Spacing = 2;
                var thumbnail = new NSImageView(new CGRect(0, 0, 48, 48))
                {
                    Image = LibraryThumbnail(entry),
                    ImageScaling = NSImageScale.ProportionallyUpOrDown,
                };
                ((NSView)thumbnail).AccessibilityLabel = entry.Kind switch
                {
                    LibraryKind.Music => "Artwork for " + entry.Title,
                    LibraryKind.Picture => "Thumbnail of " + entry.Title,
                    _ => "Preview of " + entry.Title,
                };
                thumbnail.WidthAnchor.ConstraintEqualTo(48).Active = true;
                thumbnail.HeightAnchor.ConstraintEqualTo(48).Active = true;
                var queue = NSButton.CreateButton("Queue", () =>
                {
                    if (!File.Exists(entry.Path))
                    {
                        _libraryScanner?.Request();
                        Say("This file is missing. Rescanning your library.");
                        return;
                    }

                    _player.EnqueueFromLibrary([entry]);
                    Say("Added to Up Next: " + entry.Title);
                });
                _libraryFirstQueue ??= queue;
                _libraryResultKeyViews.Add(queue);
                queue.ToolTip = "Add to Up Next without replacing what is playing";
                ((NSView)queue).AccessibilityLabel = "Add " + entry.Title + " to Up Next";
                var row = NSStackView.FromViews([thumbnail, words, queue]);
                row.Orientation = NSUserInterfaceLayoutOrientation.Horizontal;
                row.Spacing = 14;
                row.WidthAnchor.ConstraintGreaterThanOrEqualTo(490).Active = true;
                views.Add(row);
            }
    
            // AppKit views use bottom-left coordinates by default. A flipped document and
            // top-anchored stack keep the first result and empty state at the top of the list.
            var document = new TopAlignedLibraryDocument
            {
                Frame = new CGRect(0, 0, Math.Max(510, _libraryScroll.ContentSize.Width), Math.Max(340, (shown.Count + watchedFolders.Length) * 59 + 35)),
            };
            var content = NSStackView.FromViews([.. views]);
            content.Orientation = NSUserInterfaceLayoutOrientation.Vertical;
            content.Alignment = NSLayoutAttribute.Leading;
            content.Spacing = 9;
            content.TranslatesAutoresizingMaskIntoConstraints = false;
            document.AddSubview(content);
            content.TopAnchor.ConstraintEqualTo(document.TopAnchor, 12).Active = true;
            content.LeadingAnchor.ConstraintEqualTo(document.LeadingAnchor, 14).Active = true;
            content.TrailingAnchor.ConstraintLessThanOrEqualTo(document.TrailingAnchor, -12).Active = true;
            _libraryScroll.DocumentView = document;
            WireLibraryKeyViews();
        }
        _libraryMatchingCount = category == 7 ? watchedFolders.Length : entries.Count;
        _libraryTotalCount = all.Count;
        _libraryFolderCount = _mediaLibrary.Folders.Count;
        _libraryVisibleResultCount = shown.Count;
        UpdateLibraryStatus();
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
    /// Include the action toolbar, filters, paging and the current page of row buttons
    /// in a stable, reversible native focus loop. The row views are replaced after scans
    /// or filters, so AppKit's automatic geometric loop must not retain stale buttons.
    /// </summary>
    private void WireLibraryKeyViews()
    {
        if (_libraryWindow is null || _librarySearch is null || _libraryCategory is null ||
            _libraryAddButton is null || _libraryRescanButton is null || _libraryBackButton is null ||
            _libraryPreviousPage is null || _libraryNextPage is null)
        {
            return;
        }

        var order = new List<NSView>
        {
            _librarySearch, _libraryCategory, _libraryAddButton, _libraryRescanButton,
            _libraryBackButton, _libraryPreviousPage, _libraryNextPage,
        };
        order.AddRange(_libraryResultKeyViews);
        for (var i = 0; i < order.Count; i++)
        {
            order[i].NextKeyView = order[(i + 1) % order.Count];
        }
    }

    /// <summary>Return focus to playback when the library is dismissed.</summary>
    private void HideLibrary()
    {
        _libraryWindow?.OrderOut(null);
        _window.MakeKeyAndOrderFront(null);
        ShowChecks();
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
        if (_libraryWindow?.IsVisible != true || _libraryScroll is null ||
            _libraryFirstPlay is null || _libraryFirstQueue is null)
        {
            return AutomationReply.Failed("Open the library with a matching media result.");
        }

        _libraryWindow.ContentView?.LayoutSubtreeIfNeeded();
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
        if (_libraryWindow?.IsVisible != true || _librarySearch is null || _libraryCategory is null ||
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
        if (_libraryWindow?.IsVisible != true || _librarySearch is null || _libraryCategory is null)
        {
            return AutomationReply.Failed("Open the library before checking keyboard navigation.");
        }

        if (_libraryAddButton is null || _libraryRescanButton is null || _libraryBackButton is null ||
            _libraryPreviousPage is null || _libraryNextPage is null)
        {
            return AutomationReply.Failed("The library action or page controls are missing.");
        }

        var order = new List<NSView>
        {
            _librarySearch, _libraryCategory, _libraryAddButton, _libraryRescanButton,
            _libraryBackButton, _libraryPreviousPage, _libraryNextPage,
        };
        order.AddRange(_libraryResultKeyViews);
        for (var i = 0; i < order.Count; i++)
        {
            var following = order[(i + 1) % order.Count];
            if (order[i].NextKeyView?.Handle != following.Handle ||
                following.PreviousKeyView?.Handle != order[i].Handle)
            {
                return AutomationReply.Failed($"Native library key-view link {i + 1} of {order.Count} is broken.");
            }
        }

        _libraryWindow.MakeFirstResponder(_librarySearch);
        var initialResponder = _libraryWindow.FirstResponder?.Handle;
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

        _libraryWindow.SelectKeyViewFollowingView(_librarySearch);
        if (_libraryWindow.FirstResponder?.Handle != next.Handle)
        {
            _libraryWindow.MakeFirstResponder(_librarySearch);
            return AutomationReply.Failed("AppKit did not advance to the next eligible key view.");
        }

        _libraryWindow.SelectKeyViewPrecedingView(next);
        var reversed = _libraryWindow.FirstResponder?.Handle == initialResponder;
        _libraryWindow.MakeFirstResponder(_librarySearch);
        return reversed
            ? new AutomationReply { Ok = true, Text = "Native key-view link verified; AppKit advanced and reversed focus." }
            : AutomationReply.Failed("AppKit did not return focus to the search field.");
    }

    private AutomationReply PressLibraryControl(string? target)
    {
        var button = target switch
        {
            "LibraryPrevious" => _libraryPreviousPage,
            "LibraryNext" => _libraryNextPage,
            "LibraryFirstPlay" => _libraryFirstPlay,
            "LibraryFirstQueue" => _libraryFirstQueue,
            "LibraryRescan" => _libraryRescanButton,
            "LibraryBack" => _libraryBackButton,
            _ => null,
        };
        if (_libraryWindow?.IsVisible != true || button?.Enabled != true)
        {
            return AutomationReply.Failed("The library control is not available: " + target);
        }

        button.PerformClick(button);
        return new AutomationReply { Ok = true, Text = _libraryPageSummary?.StringValue };
    }

    private AutomationReply ChangeLibraryCategory(double? value)
    {
        if (_libraryWindow?.IsVisible != true || _libraryCategory is null || value is not { } index ||
            !double.IsFinite(index) || index != Math.Truncate(index) || index < 0 || index >= 8)
        {
            return AutomationReply.Failed("Choose a valid library category index.");
        }

        _libraryCategory.SelectItem((nint)index);
        ChangeLibraryView();
        return new AutomationReply { Ok = true, Text = _librarySummary?.StringValue };
    }

    private AutomationReply ResizeLibraryWindow(double? width)
    {
        if (_libraryWindow?.IsVisible != true || width is not { } requested ||
            !double.IsFinite(requested) || requested < _libraryWindow.MinSize.Width)
        {
            return AutomationReply.Failed("Open the library and choose a supported width.");
        }

        var frame = _libraryWindow.Frame;
        _libraryWindow.SetFrame(new CGRect(frame.X, frame.Y, requested, _libraryWindow.MinSize.Height), true);
        RefreshLibrary();
        return new AutomationReply { Ok = true };
    }

    /// <summary>Use top-left coordinates for scrolling native result rows.</summary>
    private sealed class TopAlignedLibraryDocument : NSView
    {
        public override bool IsFlipped => true;
    }

    /// <summary>Capture the native library surface for scripted macOS UI checks.</summary>
    private void CaptureLibraryScreenshot(string path)
    {
        if (_libraryWindow?.IsVisible != true || _libraryWindow.ContentView is not { } content)
        {
            throw new InvalidOperationException("Open the library before capturing its screenshot.");
        }

        content.LayoutSubtreeIfNeeded();
        using var bitmap = content.BitmapImageRepForCachingDisplayInRect(content.Bounds)
            ?? throw new InvalidOperationException("AppKit could not capture the library.");
        content.CacheDisplay(content.Bounds, bitmap);
        using var png = bitmap.RepresentationUsingTypeProperties(NSBitmapImageFileType.Png)
            ?? throw new InvalidOperationException("AppKit could not encode the library screenshot.");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        if (!png.Save(NSUrl.FromFilename(Path.GetFullPath(path)), true))
        {
            throw new IOException("Could not save the library screenshot.");
        }
    }

    /// <summary>Compatibility actions for native AppKit fixture tests, never enabled in release sessions.</summary>
    private bool LibrarySmokeAction(string? action, IReadOnlyList<string> paths)
    {
        if (Environment.GetEnvironmentVariable("REXPLAYER_MAC_LIBRARY_TESTS") != "1")
        {
            return false;
        }

        if (action == "show")
        {
            ShowLibrary();
            return true;
        }

        if (action == "status" && _libraryWindow is not null)
        {
            RefreshLibrary(force: true);
            return true;
        }

        if (action == "add-folder")
        {
            return AddLibraryFolders(paths.ToList()).Ok;
        }

        if (_libraryWindow?.IsVisible != true || _libraryCategory is null || _librarySearch is null)
        {
            return false;
        }

        if (action is "songs" or "videos" or "folders")
        {
            _librarySearch.StringValue = "";
            _libraryCategory.SelectItem(action switch { "songs" => 1, "videos" => 2, _ => 7 });
            ChangeLibraryView();
            return true;
        }

        if (action == "continue" && _mediaLibrary?.Entries.FirstOrDefault(entry =>
                entry.Kind == LibraryKind.Video) is { } unfinished)
        {
            _player.Memory.Left(unfinished.Path, TimeSpan.FromSeconds(15), TimeSpan.FromMinutes(2));
            _librarySearch.StringValue = "";
            _libraryCategory.SelectItem(6);
            ChangeLibraryView();
            return _libraryMatchingCount == 1 &&
                LibraryEntryDescription(unfinished).Contains("Continue at 0:15", StringComparison.Ordinal);
        }

        if (action is "search-song" or "search-missing")
        {
            _librarySearch.StringValue = action == "search-song" ? "Sample Song" : "No fixture with this name";
            ChangeLibraryView();
            return true;
        }

        if (action == "clear-search")
        {
            _librarySearch.StringValue = "";
            ChangeLibraryView();
            return true;
        }

        if (action == "verify-accessibility")
        {
            return _libraryFirstPlay?.AccessibilityLabel?.StartsWith("Play: ", StringComparison.Ordinal) == true
                && _libraryFirstQueue?.AccessibilityLabel?.EndsWith(" to Up Next", StringComparison.Ordinal) == true
                && _librarySearch.AccessibilityLabel == "Search library"
                && _libraryCategory.AccessibilityLabel == "Media category";
        }

        if (action == "remove-folder" && _mediaLibrary?.Folders.FirstOrDefault() is { } folder)
        {
            if (!_mediaLibrary.RemoveFolder(folder))
            {
                return false;
            }

            _libraryScanner?.Request();
            RefreshLibrary(force: true);
            return true;
        }

        if (action == "play-first" && _libraryFirstPlay?.Enabled == true)
        {
            _libraryFirstPlay.PerformClick(_libraryFirstPlay);
            return true;
        }

        if (action == "minimum")
        {
            return ResizeLibraryWindow(_libraryWindow.MinSize.Width).Ok;
        }

        if (action is "capture-empty" or "capture-populated" or "capture-videos" or "capture-minimum"
            or "capture-folders" or "capture-cleared" or "capture-search-empty")
        {
            var output = Environment.GetEnvironmentVariable("REXPLAYER_MAC_LIBRARY_SCREENSHOTS");
            if (string.IsNullOrWhiteSpace(output))
            {
                return false;
            }

            Directory.CreateDirectory(output);
            File.WriteAllText(Path.Combine(output, "provenance.txt"),
                "Capture origin: native AppKit content view" + Environment.NewLine
                + "Platform: native macOS AppKit" + Environment.NewLine
                + "Application version: " + typeof(MainWindow).Assembly.GetName().Version + Environment.NewLine
                + "Source: automated smoke test, synthetic local media fixtures" + Environment.NewLine
                + "Status: not a finalized-release screenshot; do not publish as release media" + Environment.NewLine);
            CaptureLibraryScreenshot(Path.Combine(output, action["capture-".Length..] + ".png"));
            return true;
        }

        return false;
    }

    private string LibrarySmokeTitle()
    {
        var items = _libraryMatchingCount;
        var folders = _libraryFolderCount;
        var query = _librarySearch?.StringValue.Trim();
        return $"{items} {(items == 1 ? "item" : "items")} · {folders} {(folders == 1 ? "folder" : "folders")}"
            + (string.IsNullOrEmpty(query) ? "" : " · Search: " + query);
    }

    private void CloseLibrary()
    {
        _closingLibrary = true;
        _libraryPeriodicScan?.Invalidate();
        _libraryPeriodicScan?.Dispose();
        _libraryPeriodicScan = null;
        _libraryScanner?.Dispose();
        _libraryScanner = null;
        _libraryWindow?.Close();
        _libraryWindow?.Dispose();
        _libraryWindow = null;
        foreach (var image in _libraryThumbnails.Values) image.Dispose();
        _libraryThumbnails.Clear();
        _libraryThumbnailOrder.Clear();
    }
}
