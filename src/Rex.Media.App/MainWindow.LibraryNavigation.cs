using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace Rex.Media.App;

public sealed partial class MainWindow
{
    // A narrow window starts with an icon rail. Its temporary expansion never overwrites
    // the person's saved preference for wider windows, even when that preference is collapsed.
    private bool _libraryNavigationExpandedWhileNarrow;
    private bool? _libraryNavigationCompact;

    // The window's viewport, not the library Grid's measured content, determines whether
    // navigation must compact. A wide child can outgrow its viewport during resize, and
    // a saved collapsed rail otherwise gets mistaken for a wide window on the next click.
    private bool LibraryNavigationIsNarrow =>
        Root.XamlRoot is { Size.Width: > 0 } root
            ? root.Size.Width < 760
            : LibraryPane.ActualWidth > 0 && LibraryPane.ActualWidth < 760;

    private void OnLibraryNavigationToggle(object sender, RoutedEventArgs e)
    {
        if (LibraryNavigationIsNarrow)
        {
            _libraryNavigationExpandedWhileNarrow = !_libraryNavigationExpandedWhileNarrow;
        }
        else
        {
            _settings = _settings with { LibraryNavigationCollapsed = !_settings.LibraryNavigationCollapsed };
            RememberLater();
        }

        ApplyLibraryNavigationWidth();
    }

    private void OnLibraryPaneSized(object sender, SizeChangedEventArgs e) => ApplyLibraryNavigationWidth();

    /// <summary>Uses an icon rail by choice or when the window is narrow, without removing navigation.</summary>
    private void ApplyLibraryNavigationWidth()
    {
        if (LibraryNavigationColumn is null || LibraryNavigationToggle is null)
        {
            return;
        }

        var narrow = LibraryNavigationIsNarrow;
        if (!narrow)
        {
            _libraryNavigationExpandedWhileNarrow = false;
        }

        var compact = narrow ? !_libraryNavigationExpandedWhileNarrow : _settings.LibraryNavigationCollapsed;
        LibraryBrand.Visibility = LibraryQuickAddText.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        LibraryNavigationColumn.Width = new GridLength(compact ? 64 : 200);
        LibraryNavigationToggle.HorizontalAlignment = compact ? HorizontalAlignment.Center : HorizontalAlignment.Right;
        LibraryNavigationToggle.Margin = compact ? new Thickness(0, 8, 0, 0) : new Thickness(8, 8, 8, 0);
        LibraryNavigationToggleIcon.Glyph = compact ? "\uE76C" : "\uE76B";
        var action = compact ? "Expand library navigation" : "Collapse library navigation";
        ToolTipService.SetToolTip(LibraryNavigationToggle, action);
        AutomationProperties.SetName(LibraryNavigationToggle, action);

        if (_libraryNavigationCompact != compact)
        {
            _libraryNavigationCompact = compact;
            LibrarySources.ItemTemplate = (DataTemplate)Root.Resources[compact
                ? "LibrarySourceCompactTemplate"
                : "LibrarySourceTemplate"];
        }
    }
}
