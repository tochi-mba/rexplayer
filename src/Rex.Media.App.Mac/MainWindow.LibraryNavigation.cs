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
