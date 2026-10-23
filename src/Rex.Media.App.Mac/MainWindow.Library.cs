using Rex.Media.AppCore.Library;
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
    private NSWindow? _libraryWindow;
    private NSSearchField? _librarySearch;
    private NSPopUpButton? _libraryCategory;
    private NSScrollView? _libraryScroll;
    private NSTextField? _librarySummary;
    private bool _closingLibrary;

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
            store = RexStore.InMemory();
        }

        _mediaLibrary = new Rex.Media.Library.MediaLibrary(store);
        _player.Library = _mediaLibrary;
        _libraryScanner = new LibraryScanner(_mediaLibrary, LibraryScanner.ListFolder, LibraryScanner.ProbeFile, _log);
        _mediaLibrary.Changed += (_, _) => OnWindowThread(RefreshLibrary);
        _libraryScanner.StatusChanged += (_, _) => OnWindowThread(RefreshLibrary);
        _libraryScanner.Request();
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
                MinSize = new CGSize(640, 470),
            };
            _libraryWindow.ReleaseWhenClosed(false);

            var headline = NSTextField.CreateLabel("Your library");
            headline.Font = NSFont.SystemFontOfSize(26, NSFontWeight.Semibold)!;
            var description = NSTextField.CreateLabel("Your files stay on this Mac. Choose folders to keep them organised.");
            description.TextColor = NSColor.SecondaryLabel;
            var add = NSButton.CreateButton("Add folder…", AddLibraryFolder);
            var rescan = NSButton.CreateButton("Rescan", () => _libraryScanner?.Request());
            var close = NSButton.CreateButton("Back to player", () => _libraryWindow?.OrderOut(null));
            var actions = NSStackView.FromViews([add, rescan, close]);
            actions.Orientation = NSUserInterfaceLayoutOrientation.Horizontal;
            actions.Spacing = 10;

            _librarySearch = new NSSearchField(new CGRect(0, 0, 400, 30))
            {
                PlaceholderString = "Search titles, artists, albums and folders",
            };
            _librarySearch.Activated += (_, _) => RefreshLibrary();
            _librarySearch.ToolTip = "Press Return to search the local library";

            _libraryCategory = new NSPopUpButton(new CGRect(0, 0, 220, 30), false);
            foreach (var name in new[] { "All media", "Music", "Videos", "Pictures", "Recently added", "Recently played" })
            {
                _libraryCategory.AddItem(name);
            }

            _libraryCategory.Activated += (_, _) => RefreshLibrary();
            var filters = NSStackView.FromViews([_librarySearch, _libraryCategory]);
            filters.Orientation = NSUserInterfaceLayoutOrientation.Horizontal;
            filters.Spacing = 12;
            _librarySearch.WidthAnchor.ConstraintGreaterThanOrEqualTo(250).Active = true;
            _libraryCategory.WidthAnchor.ConstraintEqualTo(190).Active = true;

            _librarySummary = NSTextField.CreateLabel("");
            _librarySummary.TextColor = NSColor.SecondaryLabel;
            _librarySummary.Font = NSFont.SystemFontOfSize(12, NSFontWeight.Regular)!;

            _libraryScroll = new NSScrollView(new CGRect(0, 0, 740, 390))
            {
                HasVerticalScroller = true,
            };
            _libraryScroll.HeightAnchor.ConstraintGreaterThanOrEqualTo(320).Active = true;

            var stack = NSStackView.FromViews([headline, description, actions, filters, _librarySummary, _libraryScroll]);
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

        RefreshLibrary();
        _libraryWindow.MakeKeyAndOrderFront(null);
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
        if (panel.RunModal() != 1)
        {
            return;
        }

        var added = 0;
        foreach (var folder in panel.Urls.Select(url => url.Path!).Where(Directory.Exists))
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
                Say("Could not add the folder: " + ex.Message);
            }
        }

        if (added > 0)
        {
            _libraryScanner?.Request();
        }

        RefreshLibrary();
    }

    /// <summary>Recreate the visible rows from an immutable catalogue snapshot on the UI thread.</summary>
    private void RefreshLibrary()
    {
        if (_closingLibrary || _libraryWindow is null || _libraryScroll is null || _librarySummary is null || _mediaLibrary is null)
        {
            return;
        }

        var all = _mediaLibrary.Entries;
        var entries = (int)(_libraryCategory?.IndexOfSelectedItem ?? 0) switch
        {
            1 => LibraryViews.Songs(all),
            2 => LibraryViews.Videos(all),
            3 => LibraryViews.Pictures(all),
            4 => LibraryViews.RecentlyAdded(all),
            5 => LibraryViews.RecentlyPlayed(all),
            _ => all.OrderBy(entry => entry.Title, StringComparer.CurrentCultureIgnoreCase).ToList(),
        };
        var query = _librarySearch?.StringValue.Trim();
        if (!string.IsNullOrEmpty(query))
        {
            var matching = LibraryViews.Search(all, query)
                .Select(entry => entry.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
            entries = entries.Where(entry => matching.Contains(entry.Path)).ToList();
        }

        const int visibleLimit = 80;
        var shown = entries.Take(visibleLimit).ToList();
        var views = new List<NSView>();
        if (shown.Count == 0)
        {
            var hint = NSTextField.CreateLabel(all.Count == 0
                ? "No media yet. Add a music, video or picture folder to get started."
                : "Nothing matches this view. Try another category or search.");
            hint.TextColor = NSColor.SecondaryLabel;
            views.Add(hint);
        }

        foreach (var entry in shown)
        {
            var title = entry.Title;
            var secondary = entry.Artist ?? entry.Album ?? Path.GetFileName(Path.GetDirectoryName(entry.Path)) ?? "";
            var play = NSButton.CreateButton(title, () => PlayLibraryEntry(entry, entries));
            play.ToolTip = entry.Path;
            play.Alignment = NSTextAlignment.Left;
            play.WidthAnchor.ConstraintGreaterThanOrEqualTo(360).Active = true;
            var detail = NSTextField.CreateLabel(secondary);
            detail.TextColor = NSColor.SecondaryLabel;
            detail.Font = NSFont.SystemFontOfSize(11, NSFontWeight.Regular)!;
            detail.LineBreakMode = NSLineBreakMode.TruncatingTail;
            var words = NSStackView.FromViews([play, detail]);
            words.Orientation = NSUserInterfaceLayoutOrientation.Vertical;
            words.Alignment = NSLayoutAttribute.Leading;
            words.Spacing = 2;
            var queue = NSButton.CreateButton("Queue", () =>
            {
                _player.EnqueueFromLibrary([entry]);
                Say("Added to Up Next: " + entry.Title);
            });
            queue.ToolTip = "Add to Up Next without replacing what is playing";
            var row = NSStackView.FromViews([words, queue]);
            row.Orientation = NSUserInterfaceLayoutOrientation.Horizontal;
            row.Spacing = 14;
            row.WidthAnchor.ConstraintGreaterThanOrEqualTo(650).Active = true;
            views.Add(row);
        }

        var content = NSStackView.FromViews([.. views]);
        content.Orientation = NSUserInterfaceLayoutOrientation.Vertical;
        content.Alignment = NSLayoutAttribute.Leading;
        content.Spacing = 9;
        content.EdgeInsets = new NSEdgeInsets(12, 14, 14, 12);
        // An explicit document frame allows AppKit's scroller to work even before the first layout.
        content.Frame = new CGRect(0, 0, 690, Math.Max(340, shown.Count * 59 + 35));
        _libraryScroll.DocumentView = content;
        var status = _libraryScanner?.Status;
        _librarySummary.StringValue = $"{entries.Count} matching • {all.Count} total • {_mediaLibrary.Folders.Count} folders"
            + (string.IsNullOrEmpty(status) ? "" : " • " + status)
            + (entries.Count > visibleLimit ? $" • showing first {visibleLimit}, search to narrow" : "");
    }

    private void PlayLibraryEntry(LibraryEntry chosen, IReadOnlyList<LibraryEntry> selection)
    {
        var at = selection.ToList().FindIndex(entry => string.Equals(entry.Path, chosen.Path, StringComparison.OrdinalIgnoreCase));
        _player.PlayFromLibrary(selection, Math.Max(0, at));
        _libraryWindow?.OrderOut(null);
        _window.MakeKeyAndOrderFront(null);
    }

    private void CloseLibrary()
    {
        _closingLibrary = true;
        _libraryScanner?.Dispose();
        _libraryScanner = null;
        _libraryWindow?.Close();
        _libraryWindow?.Dispose();
        _libraryWindow = null;
    }
}
