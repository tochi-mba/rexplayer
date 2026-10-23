using Rex.Media.AppCore.Automation;
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
    private NSTextField? _libraryPageSummary;
    private NSButton? _libraryPreviousPage;
    private NSButton? _libraryNextPage;
    private int _libraryPageIndex;
    private const int LibraryPageSize = 80;
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
                MinSize = new CGSize(590, 460),
            };
            _libraryWindow.ReleaseWhenClosed(false);

            var headline = NSTextField.CreateLabel("Your library");
            headline.Font = NSFont.SystemFontOfSize(26, NSFontWeight.Semibold)!;
            var description = NSTextField.CreateLabel("Your files stay on this Mac. Choose folders to keep them organised.");
            description.TextColor = NSColor.SecondaryLabel;
            var add = NSButton.CreateButton("Add folder…", AddLibraryFolder);
            var rescan = NSButton.CreateButton("Rescan", () => _libraryScanner?.Request());
            var close = NSButton.CreateButton("Back to player", HideLibrary);
            var actions = NSStackView.FromViews([add, rescan, close]);
            actions.Orientation = NSUserInterfaceLayoutOrientation.Horizontal;
            actions.Spacing = 10;

            _librarySearch = new NSSearchField(new CGRect(0, 0, 400, 30))
            {
                PlaceholderString = "Search titles, artists, albums and folders",
            };
            _librarySearch.Activated += (_, _) => ChangeLibraryView();
            _librarySearch.ToolTip = "Press Return to search the local library";
            _librarySearch.AccessibilityLabel = "Search library";

            _libraryCategory = new NSPopUpButton(new CGRect(0, 0, 220, 30), false);
            foreach (var name in new[] { "All media", "Music", "Videos", "Pictures", "Recently added", "Recently played" })
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
                }
            });
            _libraryPreviousPage.ToolTip = "Show the previous library results";
            ((NSView)_libraryPreviousPage).AccessibilityLabel = "Previous results page";
            _libraryNextPage = NSButton.CreateButton("Next", () =>
            {
                _libraryPageIndex++;
                RefreshLibrary();
            });
            _libraryNextPage.ToolTip = "Show the next library results";
            ((NSView)_libraryNextPage).AccessibilityLabel = "Next results page";
            _libraryPageSummary = NSTextField.CreateLabel("");
            _libraryPageSummary.TextColor = NSColor.SecondaryLabel;

            // Connect search to the category picker explicitly, while allowing
            // AppKit to keep the remaining buttons and dynamic rows in its key loop.
            _librarySearch.NextKeyView = _libraryCategory;
            var pages = NSStackView.FromViews([_libraryPreviousPage, _libraryPageSummary, _libraryNextPage]);
            pages.Orientation = NSUserInterfaceLayoutOrientation.Horizontal;
            pages.Spacing = 12;

            _libraryScroll = new NSScrollView(new CGRect(0, 0, 740, 390))
            {
                HasVerticalScroller = true,
            };
            _libraryScroll.HeightAnchor.ConstraintGreaterThanOrEqualTo(170).Active = true;
            _libraryScroll.AccessibilityLabel = "Library results";

            var stack = NSStackView.FromViews([headline, description, actions, filters, _librarySummary, pages, _libraryScroll]);
            stack.Orientation = NSUserInterfaceLayoutOrientation.Vertical;
            stack.Alignment = NSLayoutAttribute.Leading;
            stack.Spacing = 14;
            stack.EdgeInsets = new NSEdgeInsets(22, 24, 22, 24);
            stack.TranslatesAutoresizingMaskIntoConstraints = false;
            stack.WantsLayer = true;
            stack.Layer!.BackgroundColor = NSColor.WindowBackground.CGColor;
            _libraryWindow.ContentView = stack;
            _libraryScroll.WidthAnchor.ConstraintEqualTo(stack.WidthAnchor, 1, -48).Active = true;
            _librarySummary.WidthAnchor.ConstraintEqualTo(stack.WidthAnchor, 1, -48).Active = true;
            _libraryWindow.Center();
        }

        RefreshLibrary();
        _libraryWindow.MakeKeyAndOrderFront(null);
        _libraryWindow.MakeFirstResponder(_librarySearch);
        ShowChecks();
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

    /// <summary>Start from the first page when the search text or media category changes.</summary>
    private void ChangeLibraryView()
    {
        _libraryPageIndex = 0;
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
            var matching = LibraryViews.Search(all, query, entry => [Path.GetFileName(Path.GetDirectoryName(entry.Path))])
                .Select(entry => entry.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
            entries = entries.Where(entry => matching.Contains(entry.Path)).ToList();
        }

        var lastPage = Math.Max(0, (entries.Count - 1) / LibraryPageSize);
        _libraryPageIndex = Math.Clamp(_libraryPageIndex, 0, lastPage);
        var shown = entries.Skip(_libraryPageIndex * LibraryPageSize).Take(LibraryPageSize).ToList();
        if (_libraryPreviousPage is not null && _libraryNextPage is not null && _libraryPageSummary is not null)
        {
            _libraryPreviousPage.Enabled = _libraryPageIndex > 0;
            _libraryNextPage.Enabled = _libraryPageIndex < lastPage;
            _libraryPageSummary.StringValue = $"Page {_libraryPageIndex + 1} of {lastPage + 1}";
        }
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
            ((NSView)play).AccessibilityLabel = "Play " + entry.Title;
            play.Alignment = NSTextAlignment.Left;
            play.WidthAnchor.ConstraintGreaterThanOrEqualTo(250).Active = true;
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
            ((NSView)queue).AccessibilityLabel = "Queue " + entry.Title;
            var row = NSStackView.FromViews([words, queue]);
            row.Orientation = NSUserInterfaceLayoutOrientation.Horizontal;
            row.Spacing = 14;
            row.WidthAnchor.ConstraintGreaterThanOrEqualTo(490).Active = true;
            views.Add(row);
        }

        // AppKit views use bottom-left coordinates by default. A flipped document and
        // top-anchored stack keep the first result and empty state at the top of the list.
        var document = new TopAlignedLibraryDocument
        {
            Frame = new CGRect(0, 0, Math.Max(510, _libraryScroll.ContentSize.Width), Math.Max(340, shown.Count * 59 + 35)),
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
        var status = _libraryScanner?.Status;
        _librarySummary.StringValue = $"{entries.Count} matching • {all.Count} total • {_mediaLibrary.Folders.Count} folders"
            + (string.IsNullOrEmpty(status) ? "" : " • " + status)
            + (entries.Count > LibraryPageSize ? $" • results {_libraryPageIndex * LibraryPageSize + 1}-{_libraryPageIndex * LibraryPageSize + shown.Count}" : "");
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
        var at = selection.ToList().FindIndex(entry => string.Equals(entry.Path, chosen.Path, StringComparison.OrdinalIgnoreCase));
        _player.PlayFromLibrary(selection, Math.Max(0, at));
        HideLibrary();
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
                new UiControl { Id = "LibrarySummary", Kind = "text", Name = "Library summary", Text = _librarySummary.StringValue },
                new UiControl { Id = "LibraryPage", Kind = "text", Name = "Library page", Text = _libraryPageSummary.StringValue },
                new UiControl { Id = "LibraryPrevious", Kind = "button", Name = ((NSView)_libraryPreviousPage).AccessibilityLabel ?? "", Enabled = _libraryPreviousPage.Enabled },
                new UiControl { Id = "LibraryNext", Kind = "button", Name = ((NSView)_libraryNextPage).AccessibilityLabel ?? "", Enabled = _libraryNextPage.Enabled },
            ],
        };
    }

    /// <summary>Exercise AppKit's real Tab key-view loop without synthesizing OS-wide input.</summary>
    private AutomationReply ProbeLibraryKeyboard()
    {
        if (_libraryWindow?.IsVisible != true || _librarySearch is null)
        {
            return AutomationReply.Failed("Open the library before checking keyboard navigation.");
        }

        _libraryWindow.RecalculateKeyViewLoop();
        _libraryWindow.MakeFirstResponder(_librarySearch);
        var before = _libraryWindow.FirstResponder;
        _libraryWindow.SelectKeyViewFollowingView(_librarySearch);
        var after = _libraryWindow.FirstResponder;
        _libraryWindow.SelectKeyViewPrecedingView(_libraryCategory);
        _libraryWindow.MakeFirstResponder(_librarySearch);
        return ReferenceEquals(before, after)
            ? AutomationReply.Failed("AppKit did not advance focus to another library control.")
            : new AutomationReply { Ok = true, Text = "AppKit advanced and reversed keyboard focus." };
    }

    private AutomationReply PressLibraryControl(string? target)
    {
        var button = target switch
        {
            "LibraryPrevious" => _libraryPreviousPage,
            "LibraryNext" => _libraryNextPage,
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
            !double.IsFinite(index) || index != Math.Truncate(index) || index < 0 || index >= 6)
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
