using Microsoft.UI.Dispatching;
using Rex.Media.AppCore.Library;
using Rex.Media.AppCore.Player;
using Rex.Media.Library;

namespace Rex.Media.App;

public sealed partial class MainWindow
{
    /// <summary>A line opened: a group shows its songs, a playlist plays, a song or video plays with the rest of the view after it.</summary>
    private void Activate(LibraryRow? row)
    {
        switch (row?.Item)
        {
            case LibraryGroup group when _librarySource == LibrarySource.Completed
                && Listening().Audiobooks.Any(book => string.Equals(book.Name, group.Name, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(book.Detail, group.Detail, StringComparison.OrdinalIgnoreCase)):
                PlayBook(group, null);
                break;
            case LibraryGroup group:
                // The parent is kept with this line chosen, so Back finds it chosen and on screen.
                KeepLibraryParent(SelectionIdentity(row));
                // A group inside an open group nests: a show's season, or an artist's album.
                if ((_librarySource is LibrarySource.TvShows or LibrarySource.Artists or LibrarySource.Authors)
                    && _libraryGroup is not null && _librarySeason is null)
                {
                    _librarySeason = group;
                }
                else
                {
                    _libraryGroup = group;
                    _librarySeason = null;
                }

                LibrarySearch.Text = "";
                _places.RememberSearch(_librarySource.ToString(), "");
                ShowLibraryAtPlace();
                break;
            case BookChapter chapter when _libraryGroup is { } book:
                PlayBook(book, chapter);
                break;
            case NamedPlaylist playlist:
                if (_settings.ShowPlayerWhenLibraryMediaStarts)
                {
                    SetLibraryOpen(false);
                }

                _player.PlayPlaylist(playlist.Id);
                break;
            case LibraryEntry entry when entry.Kind == LibraryKind.Video && (_librarySource == LibrarySource.Movies || IsMovie(entry)):
                // Opening a movie goes to its own details page. The explicit Play button there
                // starts or resumes the film, instead of a title click unexpectedly starting it.
                OpenMovieDetails(entry);
                break;
            case LibraryEntry entry:
                var view = EntriesOf(_libraryRows);
                if (entry.Kind == LibraryKind.Picture)
                {
                    var sequence = LibraryViews.PictureSlideshow(view, entry);
                    Play(sequence.Items, sequence.Start);
                }
                else
                {
                    Play(view, view.FindIndex(item => string.Equals(item.Path, entry.Path, StringComparison.OrdinalIgnoreCase)));
                }
                if (view.Count > 1 && LibraryViews.EpisodeOf(entry) is { } episode)
                {
                    Say(episode.Episode is { } number ? $"Playing {episode.Series}, season {episode.Season}, from episode {number}" : $"Playing {episode.Series}, season {episode.Season}");
                }

                break;
        }
    }

    private void PlayChosen()
    {
        var chosen = SelectedLibraryRows();
        if (PlayOpenBook(chosen) || (chosen.Count == 0 && PlayShowPageNextUp()))
        {
            return;
        }

        if (chosen is [{ Item: LibraryEntry or NamedPlaylist }])
        {
            Activate(chosen[0]);
            return;
        }

        Play(EntriesOf(ChosenRows()), 0);
    }

    private void EnqueueChosen()
    {
        var rows = ChosenRows();
        foreach (var playlist in rows.Select(row => row.Item).OfType<NamedPlaylist>())
        {
            _player.PlayPlaylist(playlist.Id, enqueue: true);
        }

        var entries = EntriesOf(rows);
        if (entries.Count > 0)
        {
            _player.EnqueueFromLibrary(entries);
            Say($"Added {LibraryActions.Count(entries.Count, "item")} to the playlist");
        }
    }

    /// <summary>Plays from the visible library view and follows the chosen player transition.</summary>
    private void Play(IReadOnlyList<LibraryEntry> entries, int start, bool resume = false)
    {
        if (entries.Count == 0)
        {
            Say("There is nothing here to play yet.");
            return;
        }

        var chosen = entries[Math.Clamp(start, 0, entries.Count - 1)];
        if (NotYetPlayable(chosen) is { } notYet)
        {
            // The library stays open over the player, so it says so itself.
            App.Log.Info(LogSource, notYet);
            LibraryNotice.Message = notYet;
            LibraryNotice.IsOpen = true;
            return;
        }

        var resumeAt = resume || _librarySource == LibrarySource.ContinueWatching ? _player.LeftAt(chosen.Path) : null;
        if (_settings.ShowPlayerWhenLibraryMediaStarts || chosen.Kind is LibraryKind.Video or LibraryKind.Picture)
        {
            // Let the pane disappear and the window paint before opening a potentially large image
            // or video. Its probing and decoding stay on the engine threads, never this UI turn.
            SetLibraryOpen(false);
            OnWindowThread(DispatcherQueuePriority.Low, () =>
            {
                if (!_closing)
                {
                    _player.PlayFromLibrary(entries, start, resumeAt);
                }
            });
        }
        else
        {
            _player.PlayFromLibrary(entries, start, resumeAt);
        }
    }
}
