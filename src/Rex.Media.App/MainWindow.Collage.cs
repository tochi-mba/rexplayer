using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Rex.Media.Settings;
using Rex.Media.AppCore.Library;
using Rex.Media.AppCore.Player;
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
    internal void OnCollageItemLoaded(object sender, RoutedEventArgs args) => LoadCollagePicture(sender);

    internal void OnCollageItemDataChanged(FrameworkElement sender, DataContextChangedEventArgs args) => LoadCollagePicture(sender);

    private void LoadCollagePicture(object sender)
    {
        // The tile's data context is its row (the template binds it): an items view never sets it
        // for a compiled template, so without that binding no tile would ask for its picture.
        if (sender is FrameworkElement { DataContext: LibraryRow row } tile)
        {
            FollowCollageTile(tile, row);
            RequestLibraryPicture(row, CollagePosters(row));
        }
    }

    /// <summary>Whether a collage tile is poster-shaped, as the collage lays it out.</summary>
    private bool CollagePosters(LibraryRow row) =>
        _librarySource == LibrarySource.Movies || (row.Item is LibraryGroup && _librarySource == LibrarySource.TvShows);

    private void OnCollageDoubleTapped(object sender, DoubleTappedRoutedEventArgs args)
    {
        if (_settings.MediaOpenClickMode == MediaOpenClick.DoubleClick)
        {
            Activate(TappedRow(args.OriginalSource) ?? LibraryCollage.SelectedItem as LibraryRow);
        }

        args.Handled = true;
    }

    private void OnCollageTapped(object sender, TappedRoutedEventArgs args)
    {
        if (_settings.MediaOpenClickMode == MediaOpenClick.SingleClick)
        {
            Activate(TappedRow(args.OriginalSource));
        }
    }

    private void OnCollageKeyDown(object sender, KeyRoutedEventArgs args)
    {
        switch (args.Key)
        {
            case VirtualKey.Enter when LibraryCollage.SelectedItem is LibraryRow row:
                Activate(row);
                args.Handled = true;
                break;
            case VirtualKey.F2 when LibraryCollage.SelectedItem is LibraryRow { Item: NamedPlaylist playlist }:
                Forget(RenamePlaylistAsync(playlist.Id));
                args.Handled = true;
                break;
            case VirtualKey.Delete when LibraryCollage.SelectedItems.Count > 0:
                Forget(ChooseLibraryDeletionAsync());
                args.Handled = true;
                break;
            case VirtualKey.Back when _libraryGroup is not null:
                OnLibraryBack(sender, args);
                args.Handled = true;
                break;
        }
    }
}
