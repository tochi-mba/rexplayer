using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Rex.Media.App;

public sealed partial class MainWindow
{
    private readonly List<Action> _libraryViewport = [];
    private string _libraryViewportRoute = "";

    // The surfaces survive being hidden, but replacing rows after a scan can reset their scroll
    // offsets. Keep the offsets of the active surface and Home's horizontal shelves as well.
    private void KeepLibraryViewport()
    {
        _libraryViewport.Clear();
        _libraryViewportRoute = _selectionView;
        foreach (var element in LibraryVisuals(LibraryPane))
        {
            if (element is ScrollViewer viewer)
            {
                var (x, y) = (viewer.HorizontalOffset, viewer.VerticalOffset);
                _libraryViewport.Add(() => viewer.ChangeView(x, y, null, disableAnimation: true));
            }
            else if (element is ScrollView view)
            {
                var (x, y) = (view.HorizontalOffset, view.VerticalOffset);
                _libraryViewport.Add(() => view.ScrollTo(x, y,
                    new ScrollingScrollOptions(ScrollingAnimationMode.Disabled, ScrollingSnapPointsMode.Ignore)));
            }
        }
    }

    private void RestoreLibraryViewport()
    {
        var route = _libraryViewportRoute;
        var restore = _libraryViewport.ToArray();
        DispatcherQueue.TryEnqueue(() =>
        {
            if (!LibraryOpen || _selectionView != route)
            {
                return;
            }

            LibraryPane.UpdateLayout();
            foreach (var apply in restore)
            {
                apply();
            }
        });
    }

    // Closing cancels artwork requests. Retained rows must be allowed to request their missing
    // pictures again; only realized cards are requested, never the whole library.
    private void ResumeLibraryPictures()
    {
        foreach (var row in _libraryRows.Concat(_homeShelfRows).Where(row => row.Picture is null))
        {
            row.PictureAsked = false;
        }

        foreach (var element in LibraryVisuals(LibraryPane))
        {
            if (element is FrameworkElement { DataContext: LibraryRow row })
            {
                RequestLibraryPicture(row);
            }
        }
    }

    private static IEnumerable<DependencyObject> LibraryVisuals(DependencyObject parent)
    {
        if (parent is UIElement { Visibility: Visibility.Collapsed })
        {
            yield break;
        }

        yield return parent;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            foreach (var child in LibraryVisuals(VisualTreeHelper.GetChild(parent, index)))
            {
                yield return child;
            }
        }
    }
}
