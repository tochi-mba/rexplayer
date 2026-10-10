using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Rex.Media.AppCore.Library;
using Rex.Media.Library;
using VirtualKey = Windows.System.VirtualKey;

namespace Rex.Media.App;

/// <summary>Justified, virtualized artwork rows using the same selection and playback actions as the list.</summary>
public sealed partial class MainWindow
{
    /// <summary>
    /// The flow asks only for proportions in its visible neighbourhood. Supplying them before the
    /// tiles are realized keeps scrolling smooth without decoding offscreen artwork to size a card.
    /// </summary>
    private void OnCollageItemsInfoRequested(LinedFlowLayout sender, LinedFlowLayoutItemsInfoRequestedEventArgs args)
    {
        var start = args.ItemsRangeStartIndex;
        var length = args.ItemsRangeRequestedLength;
        if (length <= 0 || start < 0)
        {
            return;
        }

        var ratios = new double[length];
        for (var offset = 0; offset < ratios.Length; offset++)
        {
            var index = start + offset;
            if (index >= _libraryRows.Count)
            {
                ratios[offset] = 1;
                continue;
            }

            var item = _libraryRows[index].Item;
            var kind = item is LibraryEntry entry ? entry.Kind
                : item is LibraryGroup && _librarySource == LibrarySource.TvShows ? LibraryKind.Video
                : LibraryKind.Music;
            var poster = _librarySource == LibrarySource.Movies || item is LibraryGroup && _librarySource == LibrarySource.TvShows;
            ratios[offset] = CollageLayout.AspectRatio(kind, poster, index);
        }

        args.SetDesiredAspectRatios(ratios);
    }

    // Both events matter: a recycled item may keep its container while receiving a new data item.
    private void OnCollageItemLoaded(object sender, RoutedEventArgs args) => LoadCollagePicture(sender);

    private void OnCollageItemDataChanged(FrameworkElement sender, DataContextChangedEventArgs args) => LoadCollagePicture(sender);

    private void LoadCollagePicture(object sender)
    {
        if (sender is FrameworkElement { DataContext: LibraryRow row })
        {
            RequestLibraryPicture(row);
        }
    }

    private void OnCollageDoubleTapped(object sender, DoubleTappedRoutedEventArgs args)
    {
        Activate(LibraryCollage.SelectedItem as LibraryRow);
        args.Handled = true;
    }

    private void OnCollageKeyDown(object sender, KeyRoutedEventArgs args)
    {
        switch (args.Key)
        {
            case VirtualKey.Enter when LibraryCollage.SelectedItem is LibraryRow row:
                Activate(row);
                args.Handled = true;
                break;
            case VirtualKey.Back when _libraryGroup is not null:
                OnLibraryBack(sender, args);
                args.Handled = true;
                break;
        }
    }
}
