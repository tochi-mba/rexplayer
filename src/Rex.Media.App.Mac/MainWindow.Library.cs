using Rex.Media.AppCore.Library;
using Rex.Media.Library;

namespace Rex.Media.App.Mac;

/// <summary>
/// A native, offline library browser over the shared persistent media index.
/// It never pretends a queued file exists on disk, and scanning stays off AppKit's thread.
/// Up Next and playback remain owned by the existing PlayerController.
/// </summary>
public sealed partial class MainWindow
{
    private MediaLibrary? _library;
    private LibraryScanner? _libraryScanner;
    private NSWindow? _libraryWindow;
    private NSScrollView? _libraryScroll;
    private NSTextField? _librarySummary;
    private NSPopUpButton? _libraryKind;
    private NSTextField? _librarySearch;
    private NSButton? _librarySearchButton;
    private string _libraryQuery = "";
    private NSButton? _libraryPrevious;
    private NSButton? _libraryNext;
    private bool _closingLibrary;
    private int _libraryPage;
    private const int LibraryPageSize = 60;

    private void InitializeLibrary()
    {
        try
        {
            _library = new MediaLibrary(RexStore.Open(_roots.LibraryStore));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.Warning("library", "The persistent media library could not be opened: " + ex.Message);
            _library = new MediaLibrary(RexStore.InMemory());
        }

        // The same controller hook as Windows records real play history without
        // duplicating counts in a window callback or losing album/artist tags.
        _player.Library = _library;
        _library.Changed += (_, _) => OnWindowThread(RefreshLibrary);
        _libraryScanner = new LibraryScanner(_library, LibraryScanner.ListFolder,
            LibraryScanner.ProbeFile, _log);
        _libraryScanner.StatusChanged += (_, _) => OnWindowThread(RefreshLibrary);
        if (_library.Folders.Count > 0)
        {
            _libraryScanner.Request();
        }
    }

    private void ShowLibrary()
    {
        if (_libraryWindow is null)
        {
            _libraryWindow = new NSWindow(new CGRect(0, 0, 720, 590),
                NSWindowStyle.Titled | NSWindowStyle.Closable | NSWindowStyle.Resizable,
                NSBackingStore.Buffered, false)
            {
                Title = "Library - rexplayer",
                MinSize = new CGSize(540, 430),
            };
            _libraryWindow.ReleaseWhenClosed(false);

            var heading = NSTextField.CreateLabel("Your library");
            heading.Font = NSFont.SystemFontOfSize(26, NSFontWeight.Semibold)!;
            var hint = NSTextField.CreateLabel("Files from folders you chose. All browsing works offline.");
            hint.TextColor = NSColor.SecondaryLabel;

            _libraryKind = new NSPopUpButton(new CGRect(0, 0, 210, 28), false);
            foreach (var title in new[] { "All media", "Songs", "Videos", "Pictures", "Folders" })
            {
                _libraryKind.AddItem(title);
            }

            _libraryKind.ToolTip = "Choose what to browse in your local library";
            _libraryKind.Activated += (_, _) =>
            {
                _libraryPage = 0;
                _libraryQuery = "";
                if (_librarySearch is not null)
                {
                    _librarySearch.StringValue = "";
                }

                RefreshLibrary();
            };

            var add = NSButton.CreateButton("Add folder...", AddLibraryFolder);
            add.ToolTip = "Choose a local folder to include in your library";
            var rescan = NSButton.CreateButton("Rescan", () => _libraryScanner?.Request());
            rescan.ToolTip = "Look for newly added, changed and disconnected files";
            var queue = NSButton.CreateButton("Up Next", ShowQueue);
            var actions = NSStackView.FromViews([_libraryKind, add, rescan, queue]);
            actions.Orientation = NSUserInterfaceLayoutOrientation.Horizontal;
            actions.Spacing = 8;

            _libraryPrevious = NSButton.CreateButton("Previous page", () =>
            {
                _libraryPage = Math.Max(0, _libraryPage - 1);
                RefreshLibrary();
            });
            _libraryNext = NSButton.CreateButton("Next page", () =>
            {
                _libraryPage++;
                RefreshLibrary();
            });
            var pages = NSStackView.FromViews([_libraryPrevious, _libraryNext]);
            pages.Orientation = NSUserInterfaceLayoutOrientation.Horizontal;
            pages.Spacing = 8;
            _librarySearch = new NSTextField(new CGRect(0, 0, 360, 26))
            {
                PlaceholderString = "Search titles, artists and filenames",
            };
            ((NSView)_librarySearch).AccessibilityLabel = "Search the local library";
            _librarySearchButton = NSButton.CreateButton("Search", () =>
            {
                _libraryQuery = _librarySearch.StringValue.Trim();
                _libraryPage = 0;
                RefreshLibrary();
            });
            var searchRow = NSStackView.FromViews([_librarySearch, _librarySearchButton]);
            searchRow.Orientation = NSUserInterfaceLayoutOrientation.Horizontal;
            searchRow.Spacing = 8;
            _librarySummary = NSTextField.CreateLabel("");
            _librarySummary.TextColor = NSColor.SecondaryLabel;
            _librarySummary.LineBreakMode = NSLineBreakMode.TruncatingTail;
            _libraryScroll = new NSScrollView(new CGRect(0, 0, 660, 380))
            {
                HasVerticalScroller = true,
            };
            _libraryScroll.HeightAnchor.ConstraintGreaterThanOrEqualTo(180).Active = true;

            var stack = NSStackView.FromViews([heading, hint, actions, searchRow, pages, _librarySummary, _libraryScroll]);
            stack.Orientation = NSUserInterfaceLayoutOrientation.Vertical;
            stack.Alignment = NSLayoutAttribute.Leading;
            stack.Spacing = 10;
            stack.EdgeInsets = new NSEdgeInsets(16, 24, 16, 24);
            stack.TranslatesAutoresizingMaskIntoConstraints = false;
            var container = new NSView(new CGRect(0, 0, 720, 590));
            container.AddSubview(stack);
            stack.TopAnchor.ConstraintEqualTo(container.TopAnchor).Active = true;
            stack.BottomAnchor.ConstraintEqualTo(container.BottomAnchor).Active = true;
            stack.LeadingAnchor.ConstraintEqualTo(container.LeadingAnchor).Active = true;
            stack.TrailingAnchor.ConstraintEqualTo(container.TrailingAnchor).Active = true;
            _libraryScroll.WidthAnchor.ConstraintEqualTo(stack.WidthAnchor, 1, -48).Active = true;
            _librarySearch.WidthAnchor.ConstraintEqualTo(300).Active = true;
            _libraryWindow.ContentView = container;
            _libraryWindow.Center();
        }

        RefreshLibrary();
        _libraryWindow.MakeKeyAndOrderFront(null);
    }

    private void AddLibraryFolder()
    {
        var panel = NSOpenPanel.OpenPanel;
        panel.CanChooseFiles = false;
        panel.CanChooseDirectories = true;
        panel.AllowsMultipleSelection = true;
        if (panel.RunModal() != 1 || _library is null)
        {
            return;
        }

        foreach (var url in panel.Urls)
        {
            if (url.Path is { } folder && Directory.Exists(folder) && _library.AddFolder(folder))
            {
                _libraryScanner?.Request(folder);
            }
        }

        RefreshLibrary();
    }

    private void RefreshLibrary()
    {
        if (_closingLibrary || _libraryWindow is null || _libraryScroll is null ||
            _librarySummary is null || _libraryKind is null || _library is null)
        {
            return;
        }

        var filter = (int)_libraryKind.IndexOfSelectedItem;
        var folders = _library.Folders;
        // The persistent index may still remember an unavailable volume. Never offer
        // its old entries as playable, and never delete their history just for being away.
        var available = _library.Entries.Where(entry => File.Exists(entry.Path)
            && (filter == 0 || filter == 1 && entry.Kind == LibraryKind.Music
                || filter == 2 && entry.Kind == LibraryKind.Video
                || filter == 3 && entry.Kind == LibraryKind.Picture));
        var matched = _libraryQuery.Length > 0 ? LibraryViews.Search(available, _libraryQuery) : available;
        var items = matched.OrderBy(entry => entry.Title, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(entry => entry.Path, StringComparer.OrdinalIgnoreCase).ToList();
        var matchingFolders = filter == 4 && _libraryQuery.Length > 0
            ? folders.Where(folder => folder.Contains(_libraryQuery, StringComparison.CurrentCultureIgnoreCase)).ToList()
            : folders.ToList();
        var count = filter == 4 ? matchingFolders.Count : items.Count;
        _libraryPage = Math.Clamp(_libraryPage, 0, Math.Max(0, (count - 1) / LibraryPageSize));
        var start = _libraryPage * LibraryPageSize;
        var end = Math.Min(count, start + LibraryPageSize);
        _libraryPrevious!.Enabled = _libraryPage > 0;
        _libraryNext!.Enabled = end < count;

        var rows = new List<NSView>();
        if (count == 0)
        {
            var why = folders.Count == 0
                ? "No folders yet. Choose Add folder to build your private library."
                : filter == 4 ? "No folders are being watched."
                : "No available files of this kind. Reconnect a drive or rescan its folder.";
            var empty = NSTextField.CreateLabel(why);
            empty.TextColor = NSColor.SecondaryLabel;
            rows.Add(empty);
        }

        if (filter == 4)
        {
            for (var i = start; i < end; i++)
            {
                var folder = matchingFolders[i];
                var open = NSButton.CreateButton(folder, () => NSWorkspace.SharedWorkspace.OpenUrl(NSUrl.FromFilename(folder)));
                open.Alignment = NSTextAlignment.Left;
                open.ToolTip = "Open folder in Finder";
                ((NSView)open).AccessibilityLabel = "Open folder: " + folder;
                rows.Add(open);
            }
        }
        else
        {
            for (var i = start; i < end; i++)
            {
                var entry = items[i];
                var play = NSButton.CreateButton("Play: " + entry.Title, () =>
                {
                    if (File.Exists(entry.Path))
                    {
                        _player.PlayFromLibrary([entry]);
                    }
                    else
                    {
                        Say("This file is no longer available. Reconnect its drive and rescan.");
                        _libraryScanner?.Request();
                    }
                });
                play.Alignment = NSTextAlignment.Left;
                play.ToolTip = Path.GetFileName(entry.Path);
                ((NSView)play).AccessibilityLabel = "Play: " + entry.Title + ", " + Path.GetFileName(entry.Path);
                var queue = NSButton.CreateButton("Add to Up Next", () =>
                {
                    if (File.Exists(entry.Path))
                    {
                        _player.EnqueueFromLibrary([entry]);
                    }
                    else
                    {
                        Say("This file is no longer available. Reconnect its drive and rescan.");
                        _libraryScanner?.Request();
                    }
                });
                ((NSView)queue).AccessibilityLabel = "Add " + entry.Title + " to Up Next";
                var detail = NSTextField.CreateLabel((entry.Artist is { Length: > 0 } artist ? artist + " · " : "")
                    + Path.GetFileName(entry.Path));
                detail.TextColor = NSColor.SecondaryLabel;
                detail.LineBreakMode = NSLineBreakMode.TruncatingTail;
                var row = NSStackView.FromViews([play, detail, queue]);
                row.Orientation = NSUserInterfaceLayoutOrientation.Vertical;
                row.Alignment = NSLayoutAttribute.Leading;
                row.Spacing = 3;
                rows.Add(row);
            }
        }

        var content = NSStackView.FromViews([.. rows]);
        content.Orientation = NSUserInterfaceLayoutOrientation.Vertical;
        content.Alignment = NSLayoutAttribute.Leading;
        content.Spacing = 10;
        content.EdgeInsets = new NSEdgeInsets(12, 12, 14, 12);
        content.Frame = new CGRect(0, 0, 620, Math.Max(200, (end - start) * (filter == 4 ? 42 : 90) + 45));
        _libraryScroll.DocumentView = content;
        var clip = _libraryScroll.ContentView;
        clip.ScrollToPoint(new CGPoint(0, Math.Max(0, content.Frame.Height - clip.Bounds.Height)));
        _libraryScroll.ReflectScrolledClipView(clip);

        var busy = _libraryScanner?.Status ?? "";
        var problem = _libraryScanner?.Problem;
        _librarySummary.StringValue = (count == 1 ? "1 item" : count + " items")
            + (count > LibraryPageSize ? " · viewing " + (start + 1) + "-" + end : "")
            + " · " + folders.Count + (folders.Count == 1 ? " folder" : " folders")
            + (problem is not null ? " · " + problem : busy.Length > 0 ? " · " + busy : "");
    }

    /// <summary>Restricted to the test process: real AppKit library fixture captures.</summary>
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
            RefreshLibrary();
            return true;
        }

        if (action == "add-folder" && _library is not null && paths.Count == 1 && Directory.Exists(paths[0]))
        {
            _library.AddFolder(paths[0]);
            _libraryScanner?.Request(paths[0]);
            RefreshLibrary();
            return true;
        }

        if (_libraryWindow?.ContentView is not { } view || _libraryKind is null)
        {
            return false;
        }

        if (action is "songs" or "videos" or "folders")
        {
            _libraryKind.SelectItem(action switch { "songs" => 1, "videos" => 2, _ => 4 });
            _libraryQuery = "";
            if (_librarySearch is not null)
            {
                _librarySearch.StringValue = "";
            }

            _libraryPage = 0;
            RefreshLibrary();
            return true;
        }

        if (action == "search-song" && _librarySearch is not null && _librarySearchButton is not null)
        {
            _librarySearch.StringValue = "Sample Song";
            _librarySearchButton.PerformClick(_librarySearchButton);
            return true;
        }

        if (action == "verify-accessibility")
        {
            return _librarySummary is not null && _libraryScroll?.DocumentView is not null
                && _libraryKind.ToolTip?.Contains("local library", StringComparison.Ordinal) == true;
        }

        if (action is "capture-empty" or "capture-populated" or "capture-videos")
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
            view.LayoutSubtreeIfNeeded();
            using var bitmap = view.BitmapImageRepForCachingDisplayInRect(view.Bounds);
            if (bitmap is null)
            {
                return false;
            }

            view.CacheDisplay(view.Bounds, bitmap);
            using var data = bitmap.RepresentationUsingTypeProperties(NSBitmapImageFileType.Png);
            return data?.Save(NSUrl.FromFilename(Path.Combine(output, action["capture-".Length..] + ".png")), true) == true;
        }

        return false;
    }

    private void CloseLibrary()
    {
        _closingLibrary = true;
        _libraryScanner?.Dispose();
        _libraryWindow?.Close();
        _libraryWindow?.Dispose();
        _libraryWindow = null;
    }
}
