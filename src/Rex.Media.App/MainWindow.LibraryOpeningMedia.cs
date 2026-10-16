using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Rex.Media.AppCore.Library;
using Rex.Media.AppCore.Player;
using Rex.Media.Library;
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

    /// <summary>A line opened: a group shows its songs, a playlist plays, a song or video plays with the rest of the view after it.</summary>
    private void Activate(LibraryRow? row)
    {
        switch (row?.Item)
        {
            case LibraryGroup group:
                if (_librarySource == LibrarySource.TvShows && _libraryGroup is not null && _librarySeason is null)
                {
                    _librarySeason = group;
                }
                else
                {
                    _libraryGroup = group;
                    _librarySeason = null;
                }

                LibrarySearch.Text = "";
                ShowLibrary();
                break;
            case NamedPlaylist playlist:
                _player.PlayPlaylist(playlist.Id);
                break;
            case LibraryEntry entry:
                var view = entry.Kind == LibraryKind.Video
                    ? LibraryViews.AutoplayVideos(_library!.Entries, entry)
                    : EntriesOf(_libraryRows);
                Play(view, view.ToList().FindIndex(item => string.Equals(item.Path, entry.Path, StringComparison.OrdinalIgnoreCase)));
                if (view.Count > 1 && LibraryViews.EpisodeOf(entry) is { } episode)
                {
                    Say($"Playing {episode.Series}, season {episode.Season}, from episode {episode.Episode}");
                }

                break;
        }
    }
}
