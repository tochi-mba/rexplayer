using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Rex.Media.AppCore.Library;
using Rex.Media.Library;

namespace Rex.Media.App;

/// <summary>
/// The library's lists and the words around them, kept even: the view-option buttons spaced alike
/// whichever are shown, the order button's direction as an icon, an active filter that looks active,
/// Play and its neighbours only enabled when there is something to play, a list that returns to its
/// very top (headings included), and the fact lines in one voice (<see cref="ViewFacts"/>).
/// </summary>
public sealed partial class MainWindow
{
    private const double ViewOptionGap = 8;

    /// <summary>Whether the open view's progress filter speaks of watching (videos) rather than starting (what is listened to).</summary>
    private bool _progressWatched = true;

    private void WireLibraryLists()
    {
        // The option buttons share a grid whose column spacing stayed behind a hidden button, so the
        // gaps read 8 and 16 px. Space them by margins instead, worked out again whenever one shows or hides.
        if (LibraryLookButton.Parent is Grid options)
        {
            options.ColumnSpacing = 0;
            foreach (var child in options.Children)
            {
                child.RegisterPropertyChangedCallback(UIElement.VisibilityProperty, (_, _) => SpaceViewOptions(options));
            }

            SpaceViewOptions(options);
        }
    }

    private static void SpaceViewOptions(Grid options)
    {
        var first = true;
        foreach (var child in options.Children.OfType<FrameworkElement>().OrderBy(Grid.GetColumn))
        {
            if (child.Visibility != Visibility.Visible)
            {
                continue;
            }

            child.Margin = child.Margin with { Left = first ? 0 : ViewOptionGap };
            first = false;
        }
    }

    /// <summary>The order button: an arrow icon for the direction (not a typed one) and the order's name; a screen reader hears both.</summary>
    private void ShowSortButton(string sortName, bool descending)
    {
        LibrarySortButton.Content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Children =
            {
                new FontIcon { Glyph = descending ? "\uE74A" : "\uE74B", FontSize = 12 },
                new TextBlock { Text = sortName },
            },
        };
        AutomationProperties.SetName(LibrarySortButton, descending ? sortName + ", reversed" : sortName);
    }

    /// <summary>A filter in use colours its button, so a view narrowed by it never passes for the whole view.</summary>
    private void ShowFilterActive(bool filtered)
    {
        if (filtered)
        {
            LibraryFilterButton.Foreground = RexTheme.Brush("AccentTextFillColorPrimaryBrush");
            LibraryFilterButton.BorderBrush = RexTheme.Brush("AccentFillColorDefaultBrush");
        }
        else
        {
            LibraryFilterButton.ClearValue(Control.ForegroundProperty);
            LibraryFilterButton.ClearValue(Control.BorderBrushProperty);
        }
    }

    /// <summary>
    /// Play, Shuffle and Add to queue act only when the view holds something that can be played. A
    /// file still downloading counts: Play on it says how far its download is, which a disabled
    /// button could not.
    /// </summary>
    private void SetLibraryActionsPlayable(IEnumerable<LibraryRow>? rows)
    {
        var playable = rows is null || rows.Any(row => row.Item is not LibraryEntry { Problem: not null, Downloading: false });
        LibraryPlay.IsEnabled = LibraryShuffle.IsEnabled = LibraryEnqueue.IsEnabled = playable;
    }

    /// <summary>How long it all lasts together: "48 min", "2 h", "3 h 12 min"; empty when nothing has a length.</summary>
    private static string Total(IEnumerable<LibraryEntry> entries) => ViewFacts.Length(entries.Select(entry => entry.Duration));

    /// <summary>What Home says the library holds, listening shelves included and no kind it lacks.</summary>
    private string HomeFacts(int songs, int videos, int pictures)
    {
        var listening = Listening();
        return ViewFacts.Home(songs, videos, pictures, listening.Podcasts.Count, listening.Audiobooks.Count, listening.Recordings.Count);
    }

    /// <summary>Scrolls the list to its very top, so the Songs table's headings show with its first line.</summary>
    private void ScrollLibraryListToTop()
    {
        if (FindScroller(LibraryList) is { } scroller)
        {
            scroller.ChangeView(null, 0, null, disableAnimation: true);
        }
        else if (LibraryList.Items.Count > 0)
        {
            LibraryList.ScrollIntoView(LibraryList.Items[0]);
        }
    }

    private static ScrollViewer? FindScroller(DependencyObject parent)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is ScrollViewer scroller)
            {
                return scroller;
            }

            if (FindScroller(child) is { } inner)
            {
                return inner;
            }
        }

        return null;
    }

    /// <summary>
    /// A short empty view drops its icon first, so its words and its way out (Clear search) stay
    /// inside the card rather than being cut off at its edge.
    /// </summary>
    private void OnLibraryEmptySized(object sender, SizeChangedEventArgs e) =>
        LibraryEmptyIcon.Visibility = e.NewSize.Height is > 0 and < 240 ? Visibility.Collapsed : Visibility.Visible;
}
