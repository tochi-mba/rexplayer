using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Rex.Media.AppCore.Library;
using Rex.Media.Settings;
using VirtualKey = Windows.System.VirtualKey;

namespace Rex.Media.App;

public sealed partial class MainWindow
{
    private (object? List, LibraryRow? Line) _rightPressedLine;

    private void OnLibraryTapped(object sender, TappedRoutedEventArgs e)
    {
        if (_settings.MediaOpenClickMode == MediaOpenClick.SingleClick && !IsDown(VirtualKey.Control) && !IsDown(VirtualKey.Shift)
            && !TapCameFromAction(e.OriginalSource))
        {
            Activate(TappedRow(e.OriginalSource));
        }
    }

    /// <summary>A button inside a row owns its click; activating the row as well would play two commands.</summary>
    private static bool TapCameFromAction(object source)
    {
        for (var current = source as DependencyObject; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is Microsoft.UI.Xaml.Controls.Primitives.ButtonBase or TextBox or ComboBox or Slider)
            {
                return true;
            }

            if (current is FrameworkElement { DataContext: LibraryRow })
            {
                return false;
            }
        }

        return false;
    }

    private void OnLibraryDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (_settings.MediaOpenClickMode == MediaOpenClick.DoubleClick)
        {
            Activate(TappedRow(e.OriginalSource) ?? LibraryList.SelectedItem as LibraryRow);
        }
    }

    /// <summary>
    /// Notes the line a right button went down on. The list's own lines show its menu and mark the
    /// right-click handled, so the list never hears ContextRequested; a pointer press is heard first,
    /// even when handled, and always before the menu opens.
    /// </summary>
    private void OnLibraryPointerPressed(object sender, PointerRoutedEventArgs e) =>
        _rightPressedLine = e.GetCurrentPoint(null).Properties.IsRightButtonPressed
            ? (sender, TappedRow(e.OriginalSource))
            : default;

    /// <summary>
    /// As the menu opens: a right-click on a line that is not chosen chooses it alone, so the menu
    /// offers what can be done with the line under the pointer, never with an older choice. Gives
    /// the lines the menu acts on.
    /// </summary>
    private List<LibraryRow> ChoosePointedLine()
    {
        var (list, pointed) = _rightPressedLine;
        _rightPressedLine = default;
        var chosen = SelectedLibraryRows();
        var lines = LibraryActions.ContextLines(chosen, pointed);
        if (pointed is null || ReferenceEquals(lines, chosen))
        {
            return chosen;
        }

        if (ReferenceEquals(list, LibraryCollage))
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

        return [pointed];
    }

    /// <summary>
    /// In the search box, Escape clears what was typed and Down moves into what it found, choosing
    /// the first item when nothing is chosen, so Enter plays it without reaching for the mouse. A
    /// preview, because the box moves its caret on Down and marks the key handled before KeyDown.
    /// </summary>
    private void OnLibrarySearchPreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key is VirtualKey.Down or VirtualKey.Enter)
        {
            FinishLibrarySearch();
        }

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

                FocusListLine(LibraryList.SelectedItem);
                e.Handled = true;
                break;
        }
    }

    /// <summary>
    /// Gives the keyboard to a line's own container: the ListView is no tab stop, so focus on it
    /// landed nowhere the next key reached while the search box held the focus. The collage is a
    /// tab stop itself and hands the keys to its current item.
    /// </summary>
    private void FocusListLine(object? item)
    {
        LibraryList.ScrollIntoView(item);
        LibraryList.UpdateLayout();
        if (LibraryList.ContainerFromItem(item) is not Control line || !line.Focus(FocusState.Keyboard))
        {
            LibraryList.Focus(FocusState.Keyboard);
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
