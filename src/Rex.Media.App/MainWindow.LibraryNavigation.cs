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

    private static double LibraryLayoutToken(string key) => (double)Application.Current.Resources[key];

    // The window's viewport, not the library Grid's measured content, determines whether
    // navigation must compact. A wide child can outgrow its viewport during resize, and
    // a saved collapsed rail otherwise gets mistaken for a wide window on the next click.
    private bool LibraryNavigationIsNarrow =>
        Root.XamlRoot is { Size.Width: > 0 } root
            ? root.Size.Width < LibraryLayoutToken("LibraryNavigationNarrowBreakpoint")
            : LibraryPane.ActualWidth > 0 && LibraryPane.ActualWidth < LibraryLayoutToken("LibraryNavigationNarrowBreakpoint");

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

    private void OnLibraryPaneSized(object sender, SizeChangedEventArgs e)
    {
        ApplyLibraryNavigationWidth();
        ApplySmallHomeViewport(LibraryHome.Visibility == Visibility.Visible);
        // At the supported 360×430 minimum the 196px expanded header, view
        // controls and outer margins otherwise leave the Home shelf ScrollViewer
        // with zero height. Keep the hero's title and Back accessible, but use
        // the compact 128px header so the actual shelf viewport remains mounted.
        // Re-expand when there is adequate vertical room.
        if (e.NewSize.Height > 0 && e.NewSize.Height < 550)
        {
            SetLibraryHeaderCompact(true);
        }
        else if (e.NewSize.Height >= 550 && LibraryHome.Visibility == Visibility.Visible)
        {
            SetLibraryHeaderCompact(false);
        }
    }

    // On a 360x430 desktop the search/filter toolbar and expanded header
    // otherwise consume every row of the Home scroll viewport. Shelves and
    // navigation have priority until more height is available. Other routes
    // keep their search and filters even at the same small window size.
    private void ApplySmallHomeViewport(bool home)
    {
        var tinyHome = home && LibraryPane.ActualHeight is > 0 and < 550;
        LibraryViewOptionsHost.Visibility = tinyHome ? Visibility.Collapsed : Visibility.Visible;
        if (tinyHome)
        {
            SetLibraryHeaderCompact(true);
        }
    }

    /// <summary>
    /// A temporary expanded rail is only for choosing a destination on a narrow window.
    /// Once a view is chosen, give its content the available width back without changing
    /// the persisted desktop navigation preference.
    /// </summary>
    private void CollapseTemporaryLibraryNavigation()
    {
        if (!LibraryNavigationIsNarrow || !_libraryNavigationExpandedWhileNarrow)
        {
            return;
        }

        _libraryNavigationExpandedWhileNarrow = false;
        ApplyLibraryNavigationWidth();
    }

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
        LibraryNavigationColumn.Width = new GridLength(LibraryLayoutToken(compact ? "LibraryNavigationCompactWidth" : "LibraryNavigationExpandedWidth"));
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
