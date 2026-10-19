using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Rex.Media.AppCore.Library;
using Rex.Media.Settings;
using VirtualKey = Windows.System.VirtualKey;

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

    /// <summary>
    /// In the search box, Escape clears what was typed and Down moves into what it found, choosing
    /// the first item when nothing is chosen, so Enter plays it without reaching for the mouse.
    /// </summary>
    private void OnLibrarySearchKeyDown(object sender, KeyRoutedEventArgs e)
    {
        switch (e.Key)
        {
            case VirtualKey.Escape when LibrarySearch.Text.Length > 0:
                LibrarySearch.Text = "";
                e.Handled = true;
                break;
            case VirtualKey.Down when _libraryRows.Count > 0 && LibraryCollage.Visibility == Visibility.Visible:
                if (LibraryCollage.SelectedItems.Count == 0)
                {
                    LibraryCollage.Select(0);
                }

                LibraryCollage.Focus(FocusState.Keyboard);
                e.Handled = true;
                break;
            case VirtualKey.Down when _libraryRows.Count > 0 && LibraryList.Visibility == Visibility.Visible:
                if (LibraryList.SelectedItems.Count == 0)
                {
                    LibraryList.SelectedItem = _libraryRows[0];
                }

                LibraryList.Focus(FocusState.Keyboard);
                e.Handled = true;
                break;
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
