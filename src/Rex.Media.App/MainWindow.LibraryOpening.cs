using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Rex.Media.AppCore.Library;
using Rex.Media.Settings;

namespace Rex.Media.App;

/// <summary>
/// The library's memory of place (LIB-05). <see cref="LibraryPlaces"/> keeps, per route, the line
/// at the top, the line chosen and the scroll of the surfaces without lines; this file measures
/// those from the live controls and applies them again once a view is built, after its layout and
/// never after a fixed delay. A view built again around the same things (a scan, another order or
/// filter) stays anchored on the line in view instead of snapping to the top.
/// </summary>
public sealed partial class MainWindow
{
    private LibraryPlaces _places = new();

    /// <summary>The key of the route the list shows now; empty before the first build.</summary>
    private string _placeKey = "";

    /// <summary>Whether the route of the last session, or of the update hand-off, has been applied.</summary>
    private bool _libraryRouteApplied;

    /// <summary>The route shown now, for the next launch; null for Home.</summary>
    private LibraryRoute? CurrentLibraryRoute() => _librarySource == LibrarySource.Home && _libraryGroup is null ? null
        : new LibraryRoute(_librarySource.ToString(), _libraryGroup?.Name, _libraryGroup?.Detail, _librarySeason?.Name);

    /// <summary>
    /// Keeps where the user is in the route shown now, before anything changes it, with
    /// <paramref name="selected"/> as the line chosen when a line is being opened. Kept places
    /// reach the settings; a search's place lasts the session.
    /// </summary>
    private void KeepPlace(string? selected = null)
    {
        if (_placeKey.Length == 0 || !LibraryOpen)
        {
            return;
        }

        if (_places.Remember(_placeKey, CapturePlace(selected)) && !LibraryPlaces.IsSearch(_placeKey))
        {
            _settings = _settings with { LibraryPlaces = _places.Snapshot() };
            RememberLater();
        }
    }

    /// <summary>Where the user is on the surface shown: Home's page and shelves, the collage, or the list.</summary>
    private LibraryPlace CapturePlace(string? selected = null)
    {
        if (LibraryHome.Visibility == Visibility.Visible)
        {
            return new LibraryPlace(Offset: LibraryHome.VerticalOffset, Shelves: [.. ShelfViewers().Select(viewer => viewer.HorizontalOffset)]);
        }

        var chosen = selected ?? SelectedLibraryRows().Select(SelectionIdentity).FirstOrDefault();
        if (LibraryCollage.Visibility == Visibility.Visible)
        {
            return new LibraryPlace(Selected: chosen, Offset: LibraryCollage.ScrollView?.VerticalOffset ?? 0);
        }

        return new LibraryPlace(RowIdentityAt(FirstVisibleRow()), chosen);
    }

    /// <summary>Builds the view and, when it is another route than was shown, returns it to the place kept for it.</summary>
    private void ShowLibraryAtPlace()
    {
        var before = _placeKey;
        ShowLibrary();
        if (_placeKey != before && _places.PlaceOf(_placeKey) is { } place)
        {
            ApplyPlaceAfterLayout(place);
        }
    }

    /// <summary>Applies <paramref name="place"/> once the layout has realised the rows, unless the route has moved on by then.</summary>
    private void ApplyPlaceAfterLayout(LibraryPlace place)
    {
        var key = _placeKey;
        DispatcherQueue.TryEnqueue(() =>
        {
            if (LibraryOpen && _placeKey == key)
            {
                LibraryPane.UpdateLayout();
                ApplyPlace(place);
            }
        });
    }

    private void ApplyPlace(LibraryPlace place)
    {
        if (LibraryHome.Visibility == Visibility.Visible)
        {
            LibraryHome.ChangeView(null, place.Offset, null, disableAnimation: true);
            if (place.Shelves is { } offsets)
            {
                foreach (var (viewer, offset) in ShelfViewers().Zip(offsets))
                {
                    viewer.ChangeView(offset, null, null, disableAnimation: true);
                }
            }

            return;
        }

        var selected = place.Selected is null ? -1 : _libraryRows.FindIndex(row => SelectionIdentity(row) == place.Selected);
        if (LibraryCollage.Visibility == Visibility.Visible)
        {
            if (selected >= 0)
            {
                LibraryCollage.Select(selected);
                LibraryCollage.StartBringItemIntoView(selected, new BringIntoViewOptions());
            }
            else
            {
                LibraryCollage.ScrollView?.ScrollTo(0, place.Offset, new ScrollingScrollOptions(ScrollingAnimationMode.Disabled, ScrollingSnapPointsMode.Ignore));
            }

            return;
        }

        if (place.Anchor is not null && _libraryRows.FindIndex(row => SelectionIdentity(row) == place.Anchor) is >= 0 and var anchor)
        {
            LibraryList.ScrollIntoView(_libraryRows[anchor], ScrollIntoViewAlignment.Leading);
        }

        if (selected >= 0)
        {
            var row = _libraryRows[selected];
            if (LibraryList.SelectedItems.Count == 0)
            {
                LibraryList.SelectedItem = row;
            }

            // The line that opened a page, or was chosen, stays on screen; the list moves only as far as it must.
            LibraryList.ScrollIntoView(row);
        }
    }

    /// <summary>
    /// The lines to stay anchored on when the list is built again around the same things: the
    /// chosen lines on screen, then the line at the top, then the chosen lines off screen.
    /// </summary>
    private List<string?> AnchorCandidates()
    {
        if (LibraryCollage.Visibility == Visibility.Visible)
        {
            return [.. SelectedLibraryRows().Select(SelectionIdentity)];
        }

        var (first, last) = VisibleRowRange();
        var chosen = SelectedLibraryRows().Select(row => (Row: row, Index: LibraryList.Items.IndexOf(row))).ToList();
        return
        [
            .. chosen.Where(pair => pair.Index >= first && pair.Index <= last).Select(pair => SelectionIdentity(pair.Row)),
            RowIdentityAt(first),
            .. chosen.Where(pair => pair.Index < first || pair.Index > last).Select(pair => SelectionIdentity(pair.Row)),
        ];
    }

    /// <summary>Scrolls the rebuilt list so that the first of <paramref name="candidates"/> still in it, the line that was in view, is in view again.</summary>
    private void HoldSurvivingAnchor(List<string?> candidates, bool collage)
    {
        if (candidates.Count == 0 || LibraryPlaces.SurvivingAnchor(candidates, _libraryRows.Select(SelectionIdentity).ToHashSet(StringComparer.Ordinal)) is not { } anchor)
        {
            return;
        }

        var key = _placeKey;
        OnWindowThread(() =>
        {
            if (!LibraryOpen || _placeKey != key || _libraryRows.FindIndex(row => SelectionIdentity(row) == anchor) is not (>= 0 and var index))
            {
                return;
            }

            if (collage)
            {
                LibraryCollage.StartBringItemIntoView(index, new BringIntoViewOptions());
            }
            else
            {
                LibraryList.ScrollIntoView(_libraryRows[index], ScrollIntoViewAlignment.Leading);
            }
        });
    }

    private int FirstVisibleRow() => VisibleRowRange().First;

    private (int First, int Last) VisibleRowRange() => LibraryList.ItemsPanelRoot switch
    {
        ItemsStackPanel stack => (stack.FirstVisibleIndex, stack.LastVisibleIndex),
        ItemsWrapGrid grid => (grid.FirstVisibleIndex, grid.LastVisibleIndex),
        _ => (-1, -1),
    };

    private string? RowIdentityAt(int index) =>
        index >= 0 && index < LibraryList.Items.Count && LibraryList.Items[index] is LibraryRow row ? SelectionIdentity(row) : null;

    /// <summary>The shelves' own scrollers, top to bottom, on Home and in the streaming browse.</summary>
    private IEnumerable<ScrollViewer> ShelfViewers() => LibraryVisuals(LibraryShelves).OfType<ScrollViewer>();

    /// <summary>
    /// Takes the library to <paramref name="route"/>: its view, the group opened in it and the
    /// group's page, as far as the library still has them. False when the view is unknown.
    /// </summary>
    private bool ApplyLibraryRoute(LibraryRoute route)
    {
        _libraryRouteApplied = true;
        if (_library is null || !Enum.TryParse<LibrarySource>(route.View, out var source) || !Enum.IsDefined(source))
        {
            return false;
        }

        var index = Array.FindIndex(LibrarySourceItems, item => item.Source == source);
        if (index < 0)
        {
            return false;
        }

        _places.RememberSearch(source.ToString(), "");
        LibrarySources.SelectedIndex = index;
        _librarySource = source;
        if (route.Group is { Length: > 0 })
        {
            _libraryGroup = Groups(source, _library.Entries).FirstOrDefault(group =>
                group.Name == route.Group && (route.GroupDetail is null || group.Detail == route.GroupDetail));
            if (_libraryGroup is not null && route.Page is { Length: > 0 })
            {
                _librarySeason = source == LibrarySource.Artists
                    ? ArtistPages.Albums(_libraryGroup).Select(album => album.Album).FirstOrDefault(album => album.Name == route.Page)
                    : LibraryViews.TvSeasons(_libraryGroup).FirstOrDefault(season => season.Name == route.Page);
            }
        }

        return true;
    }

    private void OnLibrarySourceChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LibrarySources.SelectedIndex >= 0)
        {
            KeepPlace();
            _librarySource = LibrarySourceItems[LibrarySources.SelectedIndex].Source;
            _libraryGroup = null;
            _librarySeason = null;
            if (_shelfChoice?.Source != _librarySource)
            {
                _shelfChoice = null;
            }

            // The view's own search this session comes back with it.
            LibrarySearch.Text = _places.SearchOf(_librarySource.ToString());
            ShowLibraryAtPlace();
        }
    }

    /// <summary>
    /// The box reports a change after the fact, so setting it while changing view would build the
    /// new view twice; it is built again only when the search differs from the one it was built for.
    /// Clearing the search returns to the place the view was at before it.
    /// </summary>
    private void OnLibrarySearchChanged(object sender, TextChangedEventArgs e)
    {
        if (_librarySearchText.Changes(LibrarySearch.Text))
        {
            KeepPlace();
            _places.RememberSearch(_librarySource.ToString(), LibrarySearch.Text);
            ShowLibraryAtPlace();
        }
    }

    /// <summary>Back returns to the parent as it was left, with the line that opened the page chosen and on screen.</summary>
    private void OnLibraryBack(object sender, RoutedEventArgs e)
    {
        KeepPlace();
        if (_librarySeason is not null)
        {
            _librarySeason = null;
        }
        else
        {
            _libraryGroup = null;
        }

        ShowLibraryAtPlace();
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
