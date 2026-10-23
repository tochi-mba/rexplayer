using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace Rex.Media.App;

public sealed partial class MainWindow
{
    // A narrow window starts with an icon rail. Its temporary expansion never overwrites
    // the person's saved preference for wider windows, even when that preference is collapsed.
    private bool _libraryNavigationExpandedWhileNarrow;
    private bool? _libraryNavigationIsCompact;

    private bool LibraryNavigationIsNarrow => LibraryPane.ActualWidth > 0 && LibraryPane.ActualWidth < 760;

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
        LibraryNavigationColumn.Width = new GridLength(compact ? 64 : 200);
        LibraryNavigationToggle.HorizontalAlignment = compact ? HorizontalAlignment.Center : HorizontalAlignment.Right;
        LibraryNavigationToggle.Margin = compact ? new Thickness(0, 8, 0, 0) : new Thickness(8, 8, 8, 0);
        LibraryNavigationToggleIcon.Glyph = compact ? "\uE8A0" : "\uE89F";
        var action = compact ? "Expand library navigation" : "Collapse library navigation";
        ToolTipService.SetToolTip(LibraryNavigationToggle, action);
        AutomationProperties.SetName(LibraryNavigationToggle, action);

        if (_libraryNavigationIsCompact != compact)
        {
            LibrarySources.ItemTemplate = (DataTemplate)Root.Resources[compact ? "LibrarySourceCompactTemplate" : "LibrarySourceTemplate"];
            _libraryNavigationIsCompact = compact;
        }
    }
}
