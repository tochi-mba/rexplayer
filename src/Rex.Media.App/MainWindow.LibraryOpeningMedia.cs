using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Rex.Media.AppCore.Library;
using Rex.Media.Settings;

namespace Rex.Media.App;

public sealed partial class MainWindow
{
    private void OnLibraryTapped(object sender, TappedRoutedEventArgs e)
    {
        if (_settings.MediaOpenClickMode == MediaOpenClick.SingleClick)
        {
            Activate(TappedRow(e.OriginalSource));
        }
    }

    private void OnLibraryDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (_settings.MediaOpenClickMode == MediaOpenClick.DoubleClick)
        {
            Activate(TappedRow(e.OriginalSource) ?? LibraryList.SelectedItem as LibraryRow);
        }
    }

    /// <summary>
    /// Before the menu opens: a right-click on a line that is not chosen chooses it alone, so the
    /// menu offers what can be done with the line under the pointer, never with an older choice.
    /// </summary>
    private void OnLibraryContextRequested(UIElement sender, ContextRequestedEventArgs args)
    {
        var chosen = SelectedLibraryRows();
        if (TappedRow(args.OriginalSource) is not { } pointed
            || ReferenceEquals(LibraryActions.ContextLines(chosen, pointed), chosen))
        {
            return;
        }

        if (sender == LibraryCollage)
        {
            LibraryCollage.DeselectAll();
            if (_libraryRows.IndexOf(pointed) is >= 0 and var index)
            {
                LibraryCollage.Select(index);
            }
        }
        else
        {
            LibraryList.SelectedItems.Clear();
            LibraryList.SelectedItem = pointed;
        }
    }

    private static LibraryRow? TappedRow(object source)
    {
        if (source is not DependencyObject current)
        {
            return null;
        }

        for (; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is FrameworkElement { DataContext: LibraryRow row })
            {
                return row;
            }
        }

        return null;
    }

}
