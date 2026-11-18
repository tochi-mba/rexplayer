namespace Rex.Media.App.Mac;

/// <summary>
/// The library window's page host: the content area beside the sidebar that pages swap into. The
/// list page is the root; a sidebar move, and any page later shown over the list (an album, an
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
    private NSButton? _libraryNavigationBack;
    private bool _libraryNavigationApplying;
    private string? _librarySmokeAnswer;

    /// <summary>The content area pages swap into, with the list page as its root.</summary>
    private NSView BuildLibraryPageHost(NSView listPage)
    {
        _libraryPageHost = new NSView();
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
        if (_libraryWindow?.IsVisible != true || _libraryCategory is null || _librarySearch is null ||
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
        }
    }

    /// <summary>
    /// A page shown over the list (an album, an artist): a scrolling column of its views, the
    /// controls that join the window's focus loop while it shows, and the line scripted checks read.
    /// </summary>
    private static LibraryDetailPage LibraryDetailPageOf(string label, string summary, IReadOnlyList<NSView> views, IReadOnlyList<NSView> keyViews)
    {
        var content = NSStackView.FromViews([.. views]);
        content.Orientation = NSUserInterfaceLayoutOrientation.Vertical;
        content.Alignment = NSLayoutAttribute.Leading;
        content.Spacing = 10;
        content.EdgeInsets = new NSEdgeInsets(22, 24, 22, 24);
        content.TranslatesAutoresizingMaskIntoConstraints = false;
        var document = new TopAlignedLibraryDocument { TranslatesAutoresizingMaskIntoConstraints = false };
        document.AddSubview(content);
        content.TopAnchor.ConstraintEqualTo(document.TopAnchor).Active = true;
        content.LeadingAnchor.ConstraintEqualTo(document.LeadingAnchor).Active = true;
        content.TrailingAnchor.ConstraintLessThanOrEqualTo(document.TrailingAnchor).Active = true;
        content.BottomAnchor.ConstraintEqualTo(document.BottomAnchor).Active = true;

        var scroll = new NSScrollView { HasVerticalScroller = true, DrawsBackground = false };
        scroll.TranslatesAutoresizingMaskIntoConstraints = false;
        scroll.DocumentView = document;
        // The column is as wide as the page and as tall as its views, so it scrolls only up and down.
        document.TopAnchor.ConstraintEqualTo(scroll.ContentView.TopAnchor).Active = true;
        document.LeadingAnchor.ConstraintEqualTo(scroll.ContentView.LeadingAnchor).Active = true;
        document.WidthAnchor.ConstraintEqualTo(scroll.ContentView.WidthAnchor).Active = true;

        var page = new LibraryDetailPage(summary, [.. keyViews]);
        page.AccessibilityLabel = label;
        page.AddSubview(scroll);
        scroll.TopAnchor.ConstraintEqualTo(page.TopAnchor).Active = true;
        scroll.LeadingAnchor.ConstraintEqualTo(page.LeadingAnchor).Active = true;
        scroll.TrailingAnchor.ConstraintEqualTo(page.TrailingAnchor).Active = true;
        scroll.BottomAnchor.ConstraintEqualTo(page.BottomAnchor).Active = true;
        return page;
    }

    /// <summary>
    /// A detail page in the content area. It keeps its own controls and summary, so Back can bring
    /// it back exactly as it was left, focus loop and all.
    /// </summary>
    private sealed class LibraryDetailPage : NSView
    {
        public LibraryDetailPage(string summary, IReadOnlyList<NSView> keyViews)
        {
            Summary = summary;
            KeyViews = keyViews;
        }

        /// <summary>What the page shows in one line, for scripted checks.</summary>
        public string Summary { get; }

        /// <summary>The page's controls in focus order, after the sidebar and its Back button.</summary>
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
