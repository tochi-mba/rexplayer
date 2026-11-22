using Rex.Media.AppCore.Design;
using Rex.Media.AppCore.Library;

namespace Rex.Media.App.Mac;

/// <summary>
/// The library's navigation column, as the Windows one: the brand over "YOUR LIBRARY", a toggle
/// that narrows it to an icon rail, the views with their symbols, and Add folder at the foot. Which
/// views are listed is decided by the portable <see cref="LibrarySections"/> rules from the
/// catalogue's <see cref="LibraryFacts"/>: Home always, every other view only while it has something
/// in it, and a change during a scan waits until it settles. Only the views the Mac can draw today
/// are listed; later pages add their sections to the catalogue below.
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>The column's width, and its width as an icon rail, as on Windows.</summary>
    private const double LibraryNavigationExpandedWidth = 200;
    private const double LibraryNavigationCompactWidth = 64;

    /// <summary>Below this width the pane shows the navigation as its icon rail.</summary>
    private const double LibraryNavigationBreakpoint = 760;

    /// <summary>The Mac has no listening shelves yet, so those sections stay off its navigation.</summary>
    private static readonly ListeningShelves NoListeningShelves = new([], [], [], []);

    /// <summary>
    /// Every section the Mac shell can draw, in <see cref="SidebarView"/> order, with the name and
    /// symbol Windows gives it, each mapped to the media category that renders it on the list page.
    /// </summary>
    private static readonly (SidebarView Section, string Title, string Symbol, int Category)[] LibrarySidebarCatalog =
    [
        (SidebarView.Home, "Home", "house", 0),
        (SidebarView.Songs, "Songs", "music.note", 1),
        (SidebarView.Videos, "All videos", "film", 2),
        (SidebarView.Pictures, "Pictures", "photo", 3),
        (SidebarView.ContinueWatching, "Continue watching", "play.circle", 6),
        (SidebarView.RecentlyPlayed, "Recently played", "clock.arrow.circlepath", 5),
        (SidebarView.RecentlyAdded, "Recently added", "calendar.badge.plus", 4),
        (SidebarView.Folders, "Folders", "folder", 7),
    ];

    private readonly LibrarySections _librarySections = new();
    private readonly List<(SidebarView Section, LibraryNavItem Item)> _libraryNavItems = [];
    private NSStackView? _libraryNavList;
    private NSScrollView? _librarySidebarScroll;
    private NSLayoutConstraint? _libraryNavWidth;
    private RexButton? _libraryNavToggle;
    private NSView? _libraryBrand;
    private NSTimer? _librarySidebarSettle;
    private IReadOnlyList<(SidebarView Section, string Title, string Symbol, int Category)> _librarySidebarRows = [];
    private int _librarySidebarFolderCount = -1;
    private bool _libraryNavCollapsed;
    private bool _libraryNavNarrow;

    /// <summary>Whether the navigation shows as its icon rail: chosen, or because the pane is narrow.</summary>
    private bool LibraryNavCompact => _libraryNavCollapsed || _libraryNavNarrow;

    /// <summary>The navigation column: brand and toggle, the scrolling list of views, Add folder at the foot.</summary>
    private NSView BuildLibrarySidebar()
    {
        var column = new NSView { TranslatesAutoresizingMaskIntoConstraints = false };
        var brandName = MacTheme.Label("rexplayer", 4, DesignRole.TextPrimary, semibold: true);
        var brand = MacTheme.Stack(true, 0, brandName, MacTheme.Kicker("Your library"));
        brand.Spacing = 2;
        _libraryBrand = brand;
        ((NSView)brand).AccessibilityElement = false;

        _libraryNavToggle = MacTheme.IconButton("chevron.left", "Collapse library navigation", () =>
        {
            _libraryNavCollapsed = !_libraryNavCollapsed;
            ApplyLibraryNavCompact();
        });

        _libraryNavList = MacTheme.Stack(true, 0);
        _libraryNavList.Alignment = NSLayoutAttribute.Width;
        _libraryNavList.Spacing = 4;
        _libraryNavList.EdgeInsets = new NSEdgeInsets(MacTheme.Space(1), MacTheme.Space(2), MacTheme.Space(2), MacTheme.Space(2));
        var document = new TopAlignedLibraryDocument { TranslatesAutoresizingMaskIntoConstraints = false };
        document.AddSubview(_libraryNavList);
        _libraryNavList.TopAnchor.ConstraintEqualTo(document.TopAnchor).Active = true;
        _libraryNavList.LeadingAnchor.ConstraintEqualTo(document.LeadingAnchor).Active = true;
        _libraryNavList.TrailingAnchor.ConstraintEqualTo(document.TrailingAnchor).Active = true;
        _libraryNavList.BottomAnchor.ConstraintEqualTo(document.BottomAnchor).Active = true;
        _librarySidebarScroll = new NSScrollView
        {
            HasVerticalScroller = true,
            AutohidesScrollers = true,
            DrawsBackground = false,
            BorderType = NSBorderType.NoBorder,
            DocumentView = document,
            TranslatesAutoresizingMaskIntoConstraints = false,
        };
        document.TopAnchor.ConstraintEqualTo(_librarySidebarScroll.ContentView.TopAnchor).Active = true;
        document.LeadingAnchor.ConstraintEqualTo(_librarySidebarScroll.ContentView.LeadingAnchor).Active = true;
        document.WidthAnchor.ConstraintEqualTo(_librarySidebarScroll.ContentView.WidthAnchor).Active = true;
        _librarySidebarScroll.AccessibilityLabel = "Library sections";

        _libraryAddButton = MacTheme.TextButton("Add folder", AddLibraryFolder, RexButtonLook.Standard, "plus", "Add a folder to the library");
        var edge = MacTheme.Separator(vertical: true);

        column.AddSubview(brand);
        column.AddSubview(_libraryNavToggle);
        column.AddSubview(_librarySidebarScroll);
        column.AddSubview(_libraryAddButton);
        column.AddSubview(edge);
        brand.TopAnchor.ConstraintEqualTo(column.TopAnchor, 18).Active = true;
        brand.LeadingAnchor.ConstraintEqualTo(column.LeadingAnchor, 20).Active = true;
        brand.TrailingAnchor.ConstraintLessThanOrEqualTo(_libraryNavToggle.LeadingAnchor, -MacTheme.Space(0)).Active = true;
        _libraryNavToggle.TopAnchor.ConstraintEqualTo(column.TopAnchor, MacTheme.Space(1)).Active = true;
        _libraryNavToggle.TrailingAnchor.ConstraintEqualTo(column.TrailingAnchor, -10).Active = true;
        _librarySidebarScroll.TopAnchor.ConstraintEqualTo(_libraryNavToggle.BottomAnchor, MacTheme.Space(2)).Active = true;
        _librarySidebarScroll.LeadingAnchor.ConstraintEqualTo(column.LeadingAnchor).Active = true;
        _librarySidebarScroll.TrailingAnchor.ConstraintEqualTo(edge.LeadingAnchor).Active = true;
        _librarySidebarScroll.BottomAnchor.ConstraintEqualTo(_libraryAddButton.TopAnchor, -MacTheme.Space(1)).Active = true;
        _libraryAddButton.LeadingAnchor.ConstraintEqualTo(column.LeadingAnchor, MacTheme.Space(2)).Active = true;
        _libraryAddButton.TrailingAnchor.ConstraintEqualTo(edge.LeadingAnchor, -MacTheme.Space(2)).Active = true;
        _libraryAddButton.BottomAnchor.ConstraintEqualTo(column.BottomAnchor, -MacTheme.Space(2)).Active = true;
        edge.TopAnchor.ConstraintEqualTo(column.TopAnchor).Active = true;
        edge.BottomAnchor.ConstraintEqualTo(column.BottomAnchor).Active = true;
        edge.TrailingAnchor.ConstraintEqualTo(column.TrailingAnchor).Active = true;
        _libraryNavWidth = column.WidthAnchor.ConstraintEqualTo((nfloat)LibraryNavigationExpandedWidth);
        _libraryNavWidth.Active = true;
        return column;
    }

    /// <summary>The pane's width changed: below the breakpoint the navigation narrows to its icon rail.</summary>
    private void OnLibraryPaneResized(double width)
    {
        var narrow = width > 0 && width < LibraryNavigationBreakpoint;
        if (narrow != _libraryNavNarrow)
        {
            _libraryNavNarrow = narrow;
            // The column's width is a constraint; change it after this layout pass, not inside it.
            OnWindowThread(ApplyLibraryNavCompact);
        }
    }

    /// <summary>Shows the navigation full width with its words, or as the icon rail.</summary>
    private void ApplyLibraryNavCompact()
    {
        var compact = LibraryNavCompact;
        if (_libraryNavWidth is not null)
        {
            _libraryNavWidth.Constant = (nfloat)(compact ? LibraryNavigationCompactWidth : LibraryNavigationExpandedWidth);
        }

        if (_libraryBrand is not null)
        {
            _libraryBrand.Hidden = compact;
        }

        if (_libraryNavToggle is not null)
        {
            _libraryNavToggle.Symbol = compact ? "chevron.right" : "chevron.left";
            _libraryNavToggle.Label = compact ? "Expand library navigation" : "Collapse library navigation";
        }

        if (_libraryAddButton is not null)
        {
            _libraryAddButton.Text = compact ? null : "Add folder";
        }

        foreach (var (_, item) in _libraryNavItems)
        {
            item.Compact = compact;
        }
    }

    /// <summary>
    /// Brings the navigation in line with what the catalogue holds. The facts come from the same
    /// snapshot the list renders; a folder added or given up is the person's own doing and shows at
    /// once, while scan results wait for the portable settle rule.
    /// </summary>
    private void RefreshLibrarySidebar(bool immediate = false)
    {
        if (_closingLibrary || _mediaLibrary is null || _libraryNavList is null)
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
            RebuildLibraryNavItems();
        }

        SyncLibrarySidebarSelection();
    }

    /// <summary>Replaces the listed views with those the rules now show, and relinks the focus loop.</summary>
    private void RebuildLibraryNavItems()
    {
        if (_libraryNavList is null)
        {
            return;
        }

        foreach (var (_, item) in _libraryNavItems)
        {
            item.RemoveFromSuperview();
        }

        _libraryNavItems.Clear();
        foreach (var row in _librarySidebarRows)
        {
            var section = row.Section;
            var item = new LibraryNavItem(row.Symbol, row.Title, "Library section: " + row.Title, () => SelectLibrarySection(section))
            {
                Compact = LibraryNavCompact,
            };
            _libraryNavList.AddArrangedSubview(item);
            _libraryNavItems.Add((section, item));
        }

        WireLibraryKeyViews();
    }

    /// <summary>
    /// Keeps the marked view matching the media category, however the category was chosen; a
    /// category whose section is not listed leaves no view marked.
    /// </summary>
    private void SyncLibrarySidebarSelection()
    {
        if (_libraryCategory is null)
        {
            return;
        }

        var category = (int)_libraryCategory.IndexOfSelectedItem;
        var showingList = _libraryCurrentPage is null || ReferenceEquals(_libraryCurrentPage, _libraryListPage);
        foreach (var (section, item) in _libraryNavItems)
        {
            var entry = Array.Find(LibrarySidebarCatalog, candidate => candidate.Section == section);
            item.Selected = showingList && entry.Category == category;
        }
    }

    /// <summary>
    /// Opens a section: Back remembers the view being left, then the section's category drives the
    /// shared list page. A detail page shown over the list returns to the list first.
    /// </summary>
    private bool SelectLibrarySection(SidebarView section)
    {
        if (!LibraryShown || _libraryCategory is null)
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
        _libraryNavItems.Clear();
        _libraryNavList = null;
        _librarySidebarScroll = null;
        _libraryNavWidth = null;
        _libraryNavToggle = null;
        _libraryBrand = null;
    }
}
