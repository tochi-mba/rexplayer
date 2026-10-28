using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Rex.Media.AppCore.Library;

namespace Rex.Media.App;

/// <summary>
/// Keeps the library header useful without letting it crowd out the media below; what it does is
/// <see cref="LibraryHeaderLayout"/>'s decision. Its expanded height follows its words, so Back and
/// the title can never be clipped by a long biography. A short library, a scrolled view and a page
/// that is only its name (Browse, the music Home) get the compact header: Back, the title and the
/// actions on one line when they fit, the actions under the title when not. Returning to the top
/// of a view restores the artwork and supporting metadata.
/// </summary>
public sealed partial class MainWindow
{
    private bool _libraryHeaderCompact;
    private bool _libraryHeaderTitleOnly;
    private double _libraryScrolled;
    private ScrollViewer? _libraryListScroller;

    private void WireLibraryHeaderScrolling()
    {
        LibraryHome.ViewChanged += (_, _) => UpdateLibraryHeaderForScroll(LibraryHome.VerticalOffset);
        LibraryHome.SizeChanged += (_, e) => FitBillboard(e.NewSize.Height);
        LibraryHeroWords.SizeChanged += (_, _) => PlaceHeroActions();
        LibraryHeroActionRow.SizeChanged += (_, _) => PlaceHeroActions();
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

    // WinUI also reports ViewChanged during resize and layout; the decision includes the library's
    // height, so such a notification can never undo the compact header a short library needs.
    private void UpdateLibraryHeaderForScroll(double offset) => RefreshLibraryHeader(offset);

    /// <summary>Compacts or expands the header for the library's height, the view's scroll (the last seen, unless given) and the page.</summary>
    private void RefreshLibraryHeader(double? scrolled = null)
    {
        _libraryScrolled = scrolled ?? _libraryScrolled;
        SetLibraryHeaderCompact(LibraryHeaderLayout.Compact(LibraryPane.ActualHeight, _libraryScrolled, _libraryHeaderTitleOnly, _libraryHeaderCompact));
    }

    /// <summary>A page that is only its name (a Browse page, the music Home) shows that name as a compact header.</summary>
    private void ShowLibraryTitleOnly(bool titleOnly)
    {
        if (titleOnly)
        {
            var item = LibrarySourceItems.First(pair => pair.Source == _librarySource).Item;
            ShowHero("", item.Name, "", item.Glyph, null);
        }

        if (_libraryHeaderTitleOnly != titleOnly)
        {
            _libraryHeaderTitleOnly = titleOnly;
            RefreshLibraryHeader(0);
        }
    }

    private void SetLibraryHeaderCompact(bool compact)
    {
        if (_libraryHeaderCompact == compact)
        {
            return;
        }

        _libraryHeaderCompact = compact;
        ApplyLibraryHeaderState();
    }

    private void ApplyLibraryHeaderState()
    {
        var compact = _libraryHeaderCompact;
        static Visibility Shown(bool shown) => shown ? Visibility.Visible : Visibility.Collapsed;
        LibraryHeroArtBox.Visibility = Shown(!compact);
        LibraryKicker.Visibility = Shown(!compact && LibraryKicker.Text.Length > 0);
        LibrarySubtitle.Visibility = Shown(!compact && LibrarySubtitle.Text.Length > 0);
        LibraryFacts.Visibility = Shown(!compact && LibraryFacts.Text.Length > 0);
        LibraryOverview.Visibility = Shown(!compact && LibraryOverview.Text.Length > 0);
        LibraryCredit.Visibility = Shown(!compact && LibraryCredit.Content is not null);
        if (_heroArtists is not null)
        {
            _heroArtists.Visibility = Shown(!compact && _shownMusicPage.StartsWith("album\n", StringComparison.Ordinal) && _heroArtists.Children.Count > 0);
        }

        // Compact: Back, the title and the actions in one line, the title a size smaller.
        LibraryHeroContent.ColumnSpacing = compact ? 0 : 24;
        LibraryBack.Margin = compact ? new Thickness(0, 0, 12, 0) : new Thickness(0, 0, 0, 12);
        LibraryBack.VerticalAlignment = compact ? VerticalAlignment.Center : VerticalAlignment.Top;
        LibraryHeroAside.VerticalAlignment = compact ? VerticalAlignment.Center : VerticalAlignment.Stretch;
        LibraryHeroWords.VerticalAlignment = compact ? VerticalAlignment.Center : VerticalAlignment.Bottom;
        if (compact)
        {
            LibraryTitle.FontSize = LibraryLayoutToken("RexDisplayFontSize");
        }
        else
        {
            LibraryTitle.ClearValue(TextBlock.FontSizeProperty);
        }

        PlaceHeroActions();
        ApplyHeroLook();
        AutomationProperties.SetName(LibraryHero, compact ? "Compact library header" : "Expanded library header");
    }

    /// <summary>The compact header's actions sit beside its title while the title keeps its room, and under it otherwise.</summary>
    private void PlaceHeroActions()
    {
        var actions = LibraryHeroActionRow.ActualWidth;
        var beside = _libraryHeaderCompact && LibraryHeaderLayout.ActionsBesideTitle(LibraryHeroWords.ActualWidth, actions);
        Grid.SetRow(LibraryHeroActions, beside ? 1 : 7);
        Grid.SetColumn(LibraryHeroActions, beside ? 1 : 0);
        Grid.SetColumnSpan(LibraryHeroActions, beside ? 1 : 2);
        Grid.SetColumnSpan(LibraryTitle, beside ? 1 : 2);
        LibraryHeroActions.VerticalAlignment = beside ? VerticalAlignment.Center : VerticalAlignment.Top;
        LibraryHeroActions.Margin = beside ? new Thickness(16, 0, 0, 0)
            : new Thickness(0, actions > 0 ? (_libraryHeaderCompact ? 8 : 12) : 0, 0, 0);
    }
}
