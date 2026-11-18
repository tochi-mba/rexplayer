using Rex.Media.AppCore.Library;

namespace Rex.Media.App.Mac;

/// <summary>
/// The library window's native sidebar: an NSTableView of sections in the fixed split-view column.
/// Which sections are listed is decided by the portable <see cref="LibrarySections"/> rules from the
/// catalogue's <see cref="LibraryFacts"/>: Home always, every other view only while it has something
/// in it, and a change during a scan waits until it settles. Only the views the Mac can draw today
/// are listed; later pages add their sections to the catalogue below.
/// </summary>
public sealed partial class MainWindow
{
    private const double LibrarySidebarWidth = 200;

    /// <summary>The Mac has no listening shelves yet, so those sections stay off its sidebar.</summary>
    private static readonly ListeningShelves NoListeningShelves = new([], [], [], []);

    /// <summary>
    /// Every section the Mac shell can draw, in <see cref="SidebarView"/> order, each mapped to the
    /// media category that renders it on the shared list page.
    /// </summary>
    private static readonly (SidebarView Section, string Title, int Category)[] LibrarySidebarCatalog =
    [
        (SidebarView.Home, "Home", 0),
        (SidebarView.Songs, "Songs", 1),
        (SidebarView.Videos, "Videos", 2),
        (SidebarView.Pictures, "Pictures", 3),
        (SidebarView.ContinueWatching, "Continue watching", 6),
        (SidebarView.RecentlyPlayed, "Recently played", 5),
        (SidebarView.RecentlyAdded, "Recently added", 4),
        (SidebarView.Folders, "Folders", 7),
    ];

    private readonly LibrarySections _librarySections = new();
    private NSSplitView? _librarySplit;
    private NSTableView? _librarySidebar;
    private NSScrollView? _librarySidebarScroll;
    private LibrarySidebarSource? _librarySidebarSource;
    private NSTimer? _librarySidebarSettle;
    private IReadOnlyList<(SidebarView Section, string Title, int Category)> _librarySidebarRows = [];
    private int _librarySidebarFolderCount = -1;
    private bool _librarySidebarSyncing;

    /// <summary>The width the sidebar column and its divider take from the library window.</summary>
    private double LibrarySidebarTotalWidth => LibrarySidebarWidth + (double)(_librarySplit?.DividerThickness ?? 1);

    /// <summary>The sidebar column: the navigation Back button above the native section list.</summary>
    private NSView BuildLibrarySidebar()
    {
        var table = new NSTableView
        {
            HeaderView = null,
            AllowsEmptySelection = true,
            AllowsMultipleSelection = false,
            RowHeight = 26,
        };
        table.AddColumn(new NSTableColumn("library-section") { Editable = false, Width = 172 });
        ((NSView)table).AccessibilityLabel = "Library sections";
        _librarySidebarSource = new LibrarySidebarSource(this);
        table.Source = _librarySidebarSource;
        _librarySidebar = table;
        _librarySidebarScroll = new NSScrollView
        {
            HasVerticalScroller = true,
            DocumentView = table,
        };
        _librarySidebarScroll.AccessibilityLabel = "Library sections";

        var back = NSButton.CreateButton("Back", () => NavigateLibraryBack());
        back.Enabled = false;
        back.ToolTip = "Return to the previous library view";
        ((NSView)back).AccessibilityLabel = "Back to the previous library view";
        _libraryNavigationBack = back;

        var column = new NSView();
        back.TranslatesAutoresizingMaskIntoConstraints = false;
        _librarySidebarScroll.TranslatesAutoresizingMaskIntoConstraints = false;
        column.AddSubview(back);
        column.AddSubview(_librarySidebarScroll);
        back.TopAnchor.ConstraintEqualTo(column.TopAnchor, 14).Active = true;
        back.LeadingAnchor.ConstraintEqualTo(column.LeadingAnchor, 12).Active = true;
        _librarySidebarScroll.TopAnchor.ConstraintEqualTo(back.BottomAnchor, 10).Active = true;
        _librarySidebarScroll.LeadingAnchor.ConstraintEqualTo(column.LeadingAnchor, 8).Active = true;
        _librarySidebarScroll.TrailingAnchor.ConstraintEqualTo(column.TrailingAnchor, -4).Active = true;
        _librarySidebarScroll.BottomAnchor.ConstraintEqualTo(column.BottomAnchor, -10).Active = true;
        column.WidthAnchor.ConstraintEqualTo((nfloat)LibrarySidebarWidth).Active = true;
        return column;
    }

    /// <summary>
    /// Brings the sidebar in line with what the catalogue holds. The facts come from the same
    /// snapshot the list renders; a folder added or given up is the person's own doing and shows at
    /// once, while scan results wait for the portable settle rule.
    /// </summary>
    private void RefreshLibrarySidebar(bool immediate = false)
    {
        if (_closingLibrary || _mediaLibrary is null || _librarySidebar is null)
        {
            return;
        }

        var all = _mediaLibrary.Entries;
        var folders = _mediaLibrary.Folders.Count;
        immediate |= _librarySections.Looked && folders != _librarySidebarFolderCount;
        _librarySidebarFolderCount = folders;
        var facts = LibraryFacts.Of(all, all, NoListeningShelves,
            _ => false, _ => false, null,
            path => _player.Memory.ResumePoint(path),
            recentlyPlayed: LibraryViews.RecentlyPlayed(all).Count > 0,
            playlists: false, folders: folders > 0);
        var now = DateTimeOffset.UtcNow;
        var changed = _librarySections.Observe(LibrarySections.Present(facts),
            _libraryScanner?.IsBusy == true, now, immediate);
        _librarySidebarSettle?.Invalidate();
        _librarySidebarSettle?.Dispose();
        _librarySidebarSettle = null;
        if (_librarySections.NextLook is { } next)
        {
            var wait = TimeSpan.FromMilliseconds(Math.Max(50, (next - now).TotalMilliseconds));
            _librarySidebarSettle = NSTimer.CreateScheduledTimer(wait,
                _ => OnWindowThread(() => RefreshLibrarySidebar()));
        }

        if (changed || _librarySidebarRows.Count == 0)
        {
            _librarySidebarRows = [.. LibrarySidebarCatalog.Where(item => _librarySections.IsShown(item.Section))];
            _librarySidebar.ReloadData();
        }

        SyncLibrarySidebarSelection();
    }

    /// <summary>
    /// Keeps the highlighted sidebar row matching the media category, however the category was
    /// chosen; a category whose section is not listed leaves no row highlighted.
    /// </summary>
    private void SyncLibrarySidebarSelection()
    {
        if (_librarySidebar is null || _libraryCategory is null)
        {
            return;
        }

        var category = (int)_libraryCategory.IndexOfSelectedItem;
        var row = -1;
        for (var index = 0; index < _librarySidebarRows.Count; index++)
        {
            if (_librarySidebarRows[index].Category == category)
            {
                row = index;
                break;
            }
        }

        _librarySidebarSyncing = true;
        if (row < 0)
        {
            _librarySidebar.DeselectAll(null);
        }
        else
        {
            _librarySidebar.SelectRows(NSIndexSet.FromIndex((nint)row), byExtendingSelection: false);
        }

        _librarySidebarSyncing = false;
    }

    /// <summary>A person's click on a sidebar row; programmatic syncing never re-enters here.</summary>
    private void OnLibrarySidebarSelectionChanged()
    {
        if (_librarySidebarSyncing || _librarySidebar is null)
        {
            return;
        }

        var row = (int)_librarySidebar.SelectedRow;
        if (row >= 0 && row < _librarySidebarRows.Count)
        {
            SelectLibrarySection(_librarySidebarRows[row].Section);
        }
    }

    /// <summary>
    /// Opens a section: Back remembers the view being left, then the section's category drives the
    /// shared list page. A detail page shown over the list returns to the list first.
    /// </summary>
    private bool SelectLibrarySection(SidebarView section)
    {
        if (_libraryWindow?.IsVisible != true || _libraryCategory is null)
        {
            return false;
        }

        var chosen = Array.FindIndex(LibrarySidebarCatalog, item => item.Section == section);
        if (chosen < 0)
        {
            return false;
        }

        var category = LibrarySidebarCatalog[chosen].Category;
        if (ReferenceEquals(_libraryCurrentPage, _libraryListPage) &&
            (int)_libraryCategory.IndexOfSelectedItem == category)
        {
            SyncLibrarySidebarSelection();
            return true;
        }

        RememberLibraryNavigationStop();
        if (_libraryListPage is { } list)
        {
            ShowLibraryPage(list, remember: false);
        }

        _libraryCategory.SelectItem((nint)category);
        ChangeLibraryView();
        return true;
    }

    /// <summary>The section a scripted "sidebar-select:" names, or null for one the Mac has no page for.</summary>
    private static SidebarView? FindLibrarySidebarSection(string name)
    {
        foreach (var item in LibrarySidebarCatalog)
        {
            if (string.Equals(item.Section.ToString(), name, StringComparison.OrdinalIgnoreCase))
            {
                return item.Section;
            }
        }

        return null;
    }

    /// <summary>The sections listed and the one the current category belongs to, for scripted checks.</summary>
    private string LibrarySidebarSummary()
    {
        var category = (int)(_libraryCategory?.IndexOfSelectedItem ?? 0);
        var selected = SidebarView.Home;
        foreach (var item in LibrarySidebarCatalog)
        {
            if (item.Category == category)
            {
                selected = item.Section;
                break;
            }
        }

        return "sections=" + string.Join(",", _librarySidebarRows.Select(item => item.Section.ToString().ToLowerInvariant()))
            + ";selected=" + selected.ToString().ToLowerInvariant();
    }

    private void CloseLibrarySidebar()
    {
        _librarySidebarSettle?.Invalidate();
        _librarySidebarSettle?.Dispose();
        _librarySidebarSettle = null;
        _librarySidebarRows = [];
        _librarySidebarFolderCount = -1;
        _librarySidebar = null;
        _librarySidebarScroll = null;
        _librarySidebarSource = null;
        _librarySplit = null;
    }

    /// <summary>Feeds the section rows to the native table and answers its selection changes.</summary>
    private sealed class LibrarySidebarSource : NSTableViewSource
    {
        private readonly MainWindow _window;

        public LibrarySidebarSource(MainWindow window) => _window = window;

        public override nint GetRowCount(NSTableView tableView) => _window._librarySidebarRows.Count;

        public override NSView GetViewForItem(NSTableView tableView, NSTableColumn tableColumn, nint row)
        {
            var item = _window._librarySidebarRows[(int)row];
            var label = NSTextField.CreateLabel(item.Title);
            label.LineBreakMode = NSLineBreakMode.TruncatingTail;
            label.AccessibilityLabel = "Library section: " + item.Title;
            return label;
        }

        public override void SelectionDidChange(NSNotification notification) =>
            _window.OnLibrarySidebarSelectionChanged();
    }
}
