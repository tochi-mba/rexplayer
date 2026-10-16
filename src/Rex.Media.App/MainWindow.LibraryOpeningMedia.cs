using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
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
