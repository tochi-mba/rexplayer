using Microsoft.UI.Xaml;
using Rex.Media.AppCore.Library;

namespace Rex.Media.App;

/// <summary>
/// The rows under the library banner (LIB-05): the search box, then the view's options with the
/// scan status and the transcription queue at their end. A page about one thing (an album, a show,
/// a movie's details) leaves the library search out, a row with nothing to show takes no room, and
/// Home in a tiny library gives both rows to its shelves; <see cref="LibraryHeaderLayout"/> decides.
/// Run when a view is drawn, when the window is resized and when the status beside the options
/// changes; never per frame.
/// </summary>
public sealed partial class MainWindow
{
    private void ShowLibraryHeaderRows()
    {
        var typed = LibrarySearch.Text;
        var search = LibraryHeaderLayout.OffersSearch(_libraryGroup is not null || _details.Count > 0, typed);
        var tools = LibraryLookButton.Visibility == Visibility.Visible || LibrarySortButton.Visibility == Visibility.Visible
            || LibraryGroupButton.Visibility == Visibility.Visible || LibraryFilterButton.Visibility == Visibility.Visible
            || LibrarySizePanel.Visibility == Visibility.Visible || LibraryTranscription.Visibility == Visibility.Visible
            || LibraryStatus.Text.Length > 0;
        var home = _librarySource == LibrarySource.Home && _details.Count == 0 && typed.Trim().Length == 0;
        LibrarySearch.Visibility = search ? Visibility.Visible : Visibility.Collapsed;
        LibrarySearch.Margin = new Thickness(0, 0, 0, tools ? 4 : 12);
        LibraryToolbar.Visibility = tools ? Visibility.Visible : Visibility.Collapsed;
        LibraryViewOptionsHost.Visibility = (search || tools) && LibraryHeaderLayout.OffersViewOptions(home, LibraryPane.ActualHeight)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }
}
