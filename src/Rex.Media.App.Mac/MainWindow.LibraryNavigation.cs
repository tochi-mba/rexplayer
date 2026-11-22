namespace Rex.Media.App.Mac;

/// <summary>
/// The library's page host: the content of the rounded page beside the navigation, which pages
/// swap into. The list page is the root; a sidebar move, and any page later shown over the list (an album, an
/// artist, a show), remembers the view being left as a stop, so Back returns to it exactly as it
/// was: its page, section, search and results page.
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>Where the library was before a navigation, so Back can return to it.</summary>
    private readonly record struct LibraryNavigationStop(int Category, string Search, int PageIndex, NSView? Page);

    private readonly Stack<LibraryNavigationStop> _libraryNavigationStops = new();
    private NSView? _libraryPageHost;
    private NSView? _libraryListPage;
    private NSView? _libraryCurrentPage;
    private RexButton? _libraryNavigationBack;
    private bool _libraryNavigationApplying;
    private string? _librarySmokeAnswer;

    /// <summary>The content area pages swap into, with the list page as its root.</summary>
    private NSView BuildLibraryPageHost(NSView listPage)
    {
        _libraryPageHost = new NSView { TranslatesAutoresizingMaskIntoConstraints = false };
        _libraryNavigationBack = MacTheme.TextButton("Back", () => NavigateLibraryBack(), RexButtonLook.Standard, "chevron.left",
            "Back to the previous library view");
        _libraryNavigationBack.Enabled = false;
        _libraryNavigationBack.Hidden = true;
        _libraryListPage = listPage;
        AttachLibraryPage(listPage);
        return _libraryPageHost;
    }

    /// <summary>Puts <paramref name="page"/> in the content area; Back returns to what was showing.</summary>
    private void ShowLibraryPage(NSView page, bool remember = true)
    {
        if (_libraryPageHost is null || ReferenceEquals(_libraryCurrentPage, page))
        {
            return;
        }

        if (remember)
        {
            RememberLibraryNavigationStop();
        }

        _libraryCurrentPage?.RemoveFromSuperview();
        AttachLibraryPage(page);
        UpdateLibraryNavigationBack();
        SyncLibrarySidebarSelection();
        // The focus loop follows the page now showing: the list's controls, or a detail page's own.
        WireLibraryKeyViews();
    }

    /// <summary>Removing a page discards its edge constraints, so attaching makes them again.</summary>
    private void AttachLibraryPage(NSView page)
    {
        var host = _libraryPageHost!;
        page.TranslatesAutoresizingMaskIntoConstraints = false;
        host.AddSubview(page);
        page.TopAnchor.ConstraintEqualTo(host.TopAnchor).Active = true;
        page.LeadingAnchor.ConstraintEqualTo(host.LeadingAnchor).Active = true;
        page.TrailingAnchor.ConstraintEqualTo(host.TrailingAnchor).Active = true;
        page.BottomAnchor.ConstraintEqualTo(host.BottomAnchor).Active = true;
        _libraryCurrentPage = page;
        // Back sits over the artwork of whichever banner is showing, as it does on Windows.
        var aside = page is LibraryDetailPage detail ? detail.Aside : _libraryListHero?.Aside;
        if (_libraryNavigationBack is { } back && aside is not null)
        {
            back.RemoveFromSuperview();
            aside.InsertArrangedSubview(back, 0);
        }
    }

    /// <summary>Keeps the view being left - its page, section, search and results page - for Back.</summary>
    private void RememberLibraryNavigationStop()
    {
        if (_libraryNavigationApplying || _libraryCategory is null)
        {
            return;
        }

        _libraryNavigationStops.Push(new LibraryNavigationStop(
            (int)_libraryCategory.IndexOfSelectedItem,
            _librarySearch?.StringValue ?? "",
            _libraryPageIndex,
            _libraryCurrentPage is { } page && !ReferenceEquals(page, _libraryListPage) ? page : null));
        UpdateLibraryNavigationBack();
    }

    /// <summary>Back returns to the previous stop as it was left; false when there is none.</summary>
    private bool NavigateLibraryBack()
    {
        if (!LibraryShown || _libraryCategory is null || _librarySearch is null ||
            !_libraryNavigationStops.TryPop(out var stop))
        {
            return false;
        }

        _libraryNavigationApplying = true;
        if (_libraryListPage is { } list)
        {
            ShowLibraryPage(stop.Page ?? list, remember: false);
        }

        _librarySearch.StringValue = stop.Search;
        _libraryCategory.SelectItem((nint)stop.Category);
        _libraryPageIndex = stop.PageIndex;
        RefreshLibrary();
        ScrollLibraryToStart();
        _libraryNavigationApplying = false;
        UpdateLibraryNavigationBack();
        return true;
    }

    /// <summary>The Back button works only while a stop is there to return to.</summary>
    private void UpdateLibraryNavigationBack()
    {
        if (_libraryNavigationBack is { } back)
        {
            back.Enabled = _libraryNavigationStops.Count > 0;
            back.Hidden = !back.Enabled;
        }
    }

    /// <summary>
    /// A page shown over the list (an album, an artist): its banner, then a scrolling column of its
    /// sections inset as the list's are, the controls that join the pane's focus loop while it
    /// shows, and the line scripted checks read.
    /// </summary>
    private static LibraryDetailPage LibraryDetailPageOf(string label, string summary, LibraryHero hero, IReadOnlyList<NSView> views, IReadOnlyList<NSView> keyViews)
    {
        var sections = MacTheme.Stack(true, 2, [.. views]);
        sections.Alignment = NSLayoutAttribute.Width;
        sections.EdgeInsets = new NSEdgeInsets(MacTheme.Space(2), 18, MacTheme.Space(4), 18);
        var content = MacTheme.Stack(true, 0, hero.View, sections);
        content.Alignment = NSLayoutAttribute.Width;
        var document = new TopAlignedLibraryDocument { TranslatesAutoresizingMaskIntoConstraints = false };
        document.AddSubview(content);
        content.TopAnchor.ConstraintEqualTo(document.TopAnchor).Active = true;
        content.LeadingAnchor.ConstraintEqualTo(document.LeadingAnchor).Active = true;
        content.TrailingAnchor.ConstraintEqualTo(document.TrailingAnchor).Active = true;
        content.BottomAnchor.ConstraintEqualTo(document.BottomAnchor).Active = true;

        var scroll = new NSScrollView
        {
            HasVerticalScroller = true,
            AutohidesScrollers = true,
            DrawsBackground = false,
            BorderType = NSBorderType.NoBorder,
            TranslatesAutoresizingMaskIntoConstraints = false,
        };
        scroll.DocumentView = document;
        // The column is as wide as the page and as tall as its views, so it scrolls only up and down.
        document.TopAnchor.ConstraintEqualTo(scroll.ContentView.TopAnchor).Active = true;
        document.LeadingAnchor.ConstraintEqualTo(scroll.ContentView.LeadingAnchor).Active = true;
        document.WidthAnchor.ConstraintEqualTo(scroll.ContentView.WidthAnchor).Active = true;

        var page = new LibraryDetailPage(summary, hero.Aside, [.. keyViews]);
        page.AccessibilityLabel = label;
        MacTheme.Fill(page, scroll);
        return page;
    }

    /// <summary>
    /// A detail page in the content area. It keeps its own controls and summary, so Back can bring
    /// it back exactly as it was left, focus loop and all.
    /// </summary>
    private sealed class LibraryDetailPage : NSView
    {
        public LibraryDetailPage(string summary, NSStackView aside, IReadOnlyList<NSView> keyViews)
        {
            Summary = summary;
            Aside = aside;
            KeyViews = keyViews;
        }

        /// <summary>What the page shows in one line, for scripted checks.</summary>
        public string Summary { get; }

        /// <summary>The banner's column over the artwork, where Back sits while the page shows.</summary>
        public NSStackView Aside { get; }

        /// <summary>The page's controls in focus order, after the navigation and Back.</summary>
        public IReadOnlyList<NSView> KeyViews { get; }
    }

    private void CloseLibraryNavigation()
    {
        _libraryNavigationStops.Clear();
        _libraryCurrentPage = null;
        _libraryPageHost = null;
        _libraryListPage = null;
        _libraryNavigationBack = null;
        _librarySmokeAnswer = null;
    }
}
