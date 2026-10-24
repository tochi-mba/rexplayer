using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
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
                && Listening().Audiobooks.Any(book =>
                    ListeningViews.BookIdentity(book) == ListeningViews.BookIdentity(group)):
                // A book on Completed is playable directly, but its title and
                // author alone must not match a different same-credited edition.
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
            case BookChapter chapter:
                // Authors uses _libraryGroup for the author and _librarySeason for the
                // opened book. Queue only that book, never the author's entire catalogue.
                if ((_librarySource == LibrarySource.Authors ? _librarySeason : _libraryGroup) is { } book)
                {
                    // A stale row from a refreshed author page must not fall through to
                    // the first part of another same-titled book.
                    if (book.Entries.Any(part => LibraryViews.SameLocation(part.Path, chapter.Part.Path)))
                    {
                        PlayBook(book, chapter);
                    }
                }

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
            case LibraryEntry entry when entry.Kind == LibraryKind.Music
                && (ShelfOf(entry) is AudioShelf.Recordings or AudioShelf.Sounds):
                // A voice note or short effect is a single listening task. Finishing it must
                // not roll into another recording or sound from the library's mixed list.
                Play([entry], 0, resume: ShelfOf(entry) == AudioShelf.Recordings);
                break;
            case LibraryEntry entry when entry.Kind == LibraryKind.Music && ShelfOf(entry) == AudioShelf.Podcasts:
                PlayPodcastEpisode(entry);
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
        if (PlayOpenBook(chosen) || (chosen.Count == 1 && PlayBookCard(chosen[0]))
            || (chosen.Count == 0 && PlayShowPageNextUp()))
        {
            return;
        }

        // At both Authors levels, an empty selection normally means "all".
        // Never interpret that as playing every book in an author's catalogue.
        // A single book on the author page was already handled by PlayBookCard.
        if (_librarySource == LibrarySource.Authors && _librarySeason is null)
        {
            if (_libraryGroup is null && chosen is [{ Item: LibraryGroup }])
            {
                Activate(chosen[0]);
            }
            else
            {
                Say(_libraryGroup is null ? "Open an author and choose a book to play."
                    : "Choose one book to play.");
            }

            return;
        }

        if (CompletedSelectionSpansBooks(ChosenRows()))
        {
            Say("Choose one finished audiobook to play.");
            return;
        }

        // A shelf of audiobook cards is not a mixed-file playlist. The
        // single-card case already routed through PlayBookCard above; no
        // selection or a multi-selection must never autoplay all books together.
        if (_librarySource == LibrarySource.Audiobooks && _libraryGroup is null)
        {
            Say("Choose one audiobook to play.");
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
        // Enqueueing a whole author or several books would mix otherwise scoped
        // audiobook queues. Only an explicitly selected book may be appended.
        if (_librarySource == LibrarySource.Authors && _librarySeason is null
            && (_libraryGroup is null || SelectedLibraryRows() is not [{ Item: LibraryGroup }]))
        {
            Say("Choose one book to queue.");
            return;
        }

        if (_librarySource == LibrarySource.Audiobooks && _libraryGroup is null
            && SelectedLibraryRows() is not [{ Item: LibraryGroup }])
        {
            Say("Choose one audiobook to queue.");
            return;
        }

        var rows = ChosenRows();
        if (CompletedSelectionSpansBooks(rows))
        {
            Say("Choose one finished audiobook to queue.");
            return;
        }

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

    /// <summary>
    /// Completed mixes whole books with individual movies and recordings. The
    /// implicit "all" selection must never flatten a finished book together
    /// with another book or unrelated media into a single playback queue.
    /// A single explicit book remains playable via the normal book action.
    /// </summary>
    private bool CompletedSelectionSpansBooks(IReadOnlyList<LibraryRow> rows) =>
        _librarySource == LibrarySource.Completed
        && rows.Any(row => row.Item is LibraryGroup)
        && (rows.Count != 1 || SelectedLibraryRows().Count != 1);

    /// <summary>Plays the chosen items, or the whole view, in shuffled order.</summary>
    private void OnLibraryShuffle(object sender, RoutedEventArgs e)
    {
        if (_librarySource is LibrarySource.Authors or LibrarySource.Audiobooks)
        {
            Say("Audiobook chapters keep their reading order. Choose a book to play.");
            return;
        }

        if (_librarySource == LibrarySource.Completed
            && ChosenRows().Any(row => row.Item is LibraryGroup))
        {
            Say("Finished audiobooks play in chapter order, not shuffled.");
            return;
        }

        var entries = EntriesOf(ChosenRows());
        if (entries.Count == 0)
        {
            Say("There is nothing here to play yet.");
            return;
        }

        _player.Playlist.Shuffle = true;
        Play(entries, Random.Shared.Next(entries.Count));
        Say("Shuffle on");
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

        // Stop using the previous audiobook's speed before handing music or a
        // podcast to the player; do not rely on its eventual Changed notification.
        // A rejected play above must never change the book currently sounding.
        if (_bookPlaying is { } previous
            && !previous.Book.Entries.Any(part => LibraryViews.SameLocation(part.Path, chosen.Path)))
        {
            _bookPlaying = null;
            if (Math.Abs(_player.Speed - 1) > 0.005)
            {
                _player.SetSpeed(1);
            }
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
