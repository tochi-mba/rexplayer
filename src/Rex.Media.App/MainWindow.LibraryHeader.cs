using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace Rex.Media.App;

/// <summary>
/// Keeps the library header useful without letting it crowd out the media below. Its expanded
/// height follows the content, so Back and the title can never be clipped by a long biography.
/// Once the active surface scrolls, it becomes a compact sticky header; returning to the top
/// restores the artwork and supporting metadata.
/// </summary>
public sealed partial class MainWindow
{
    private const double CompactLibraryHeaderHeight = 128;
    private const double LibraryHeaderCollapseOffset = 28;

    private double _expandedLibraryHeaderHeight = HeroHeight;
    private bool _libraryHeaderCompact;
    private ScrollViewer? _libraryListScroller;

    private void WireLibraryHeaderScrolling()
    {
        LibraryHome.ViewChanged += (_, _) => UpdateLibraryHeaderForScroll(LibraryHome.VerticalOffset);
        LibraryList.Loaded += (_, _) =>
        {
            var scroller = LibraryVisuals(LibraryList).OfType<ScrollViewer>().FirstOrDefault();
            if (scroller is null || ReferenceEquals(scroller, _libraryListScroller))
            {
                return;
            }

            if (_libraryListScroller is not null)
            {
                _libraryListScroller.ViewChanged -= OnLibraryListViewChanged;
            }

            _libraryListScroller = scroller;
            _libraryListScroller.ViewChanged += OnLibraryListViewChanged;
        };
        LibraryCollage.Loaded += (_, _) =>
        {
            if (LibraryCollage.ScrollView is { } scroll)
            {
                scroll.ViewChanged -= OnLibraryCollageViewChanged;
                scroll.ViewChanged += OnLibraryCollageViewChanged;
            }
        };
    }

    private void OnLibraryListViewChanged(object? sender, ScrollViewerViewChangedEventArgs e)
    {
        if (sender is ScrollViewer scroll)
        {
            UpdateLibraryHeaderForScroll(scroll.VerticalOffset);
        }
    }

    private void OnLibraryCollageViewChanged(ScrollView sender, object args) =>
        UpdateLibraryHeaderForScroll(sender.VerticalOffset);

    private void UpdateLibraryHeaderForScroll(double offset)
    {
        if (LibraryHero.Visibility == Visibility.Visible)
        {
            // A short library viewport needs the compact header even at scroll
            // offset zero. WinUI emits ViewChanged during resize/layout; that
            // notification must not undo OnLibraryPaneSized's compact layout.
            var shortViewport = LibraryPane.ActualHeight is > 0 and < 550;
            SetLibraryHeaderCompact(shortViewport || offset > LibraryHeaderCollapseOffset);
        }
    }

    /// <summary>Changes the normal minimum without reintroducing the fixed height that clipped rich headers.</summary>
    private void SetExpandedLibraryHeaderHeight(double height)
    {
        _expandedLibraryHeaderHeight = height;
        if (!_libraryHeaderCompact)
        {
            ApplyLibraryHeaderState();
        }
    }

    private void SetLibraryHeaderCompact(bool compact)
    {
        if (_libraryHeaderCompact == compact && (!compact || LibraryHero.Height == CompactLibraryHeaderHeight))
        {
            return;
        }

        _libraryHeaderCompact = compact;
        ApplyLibraryHeaderState();
    }

    private void ApplyLibraryHeaderState()
    {
        if (_libraryHeaderCompact)
        {
            LibraryHero.MinHeight = LibraryHero.Height = CompactLibraryHeaderHeight;
            LibraryHeroContent.Padding = new Thickness(18, 6, 18, 6);
            LibraryHeroContent.ColumnSpacing = 0;
            LibraryHeroArtBox.Visibility = Visibility.Collapsed;
            LibraryKicker.Visibility = Visibility.Collapsed;
            LibrarySubtitle.Visibility = Visibility.Collapsed;
            LibraryFacts.Visibility = Visibility.Collapsed;
            LibraryOverview.Visibility = Visibility.Collapsed;
            LibraryCredit.Visibility = Visibility.Collapsed;
            if (_heroArtists is not null)
            {
                _heroArtists.Visibility = Visibility.Collapsed;
            }

            AutomationProperties.SetName(LibraryHero, "Compact library header");
        }
        else
        {
            // Auto height is essential: biographies, credits and narrow windows may need more
            // than the design minimum, and Back is the first thing otherwise pushed above it.
            LibraryHero.Height = double.NaN;
            LibraryHero.MinHeight = _expandedLibraryHeaderHeight;
            LibraryHeroContent.Padding = new Thickness(28, 24, 28, 22);
            LibraryHeroContent.ColumnSpacing = 24;
            LibraryHeroArtBox.Visibility = Visibility.Visible;
            LibraryKicker.Visibility = Visibility.Visible;
            LibrarySubtitle.Visibility = Visibility.Visible;
            LibraryFacts.Visibility = LibraryFacts.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            LibraryOverview.Visibility = LibraryOverview.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            LibraryCredit.Visibility = LibraryCredit.Content is null ? Visibility.Collapsed : Visibility.Visible;
            if (_heroArtists is not null)
            {
                _heroArtists.Visibility = _shownMusicPage.StartsWith("album\n", StringComparison.Ordinal) && _heroArtists.Children.Count > 0
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }

            AutomationProperties.SetName(LibraryHero, "Expanded library header");
        }

        LibraryHeroBackdrop.Height = LibraryHeroWide.Height = _libraryHeaderCompact
            ? CompactLibraryHeaderHeight
            : Math.Max(_expandedLibraryHeaderHeight, LibraryHeroContent.ActualHeight);
    }
}
