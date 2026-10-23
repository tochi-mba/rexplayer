using Microsoft.UI.Xaml.Controls;
using Rex.Media.AppCore.Library;
using Rex.Media.Library;
using static Rex.Media.AppCore.Library.LibraryActions;

namespace Rex.Media.App;

/// <summary>
/// What a person does with audio that is not music: moving a file to where it belongs (kept with
/// their other edits, so rescans and cleared metadata never undo it), resuming a book where it was
/// left at the speed they listen to it at, marking a book finished, and marking a show played.
/// </summary>
public sealed partial class MainWindow
{
    private static readonly (AudioShelf Shelf, string Name)[] MoveTargets =
        [(AudioShelf.Music, "Music"), (AudioShelf.Audiobooks, "Audiobooks"), (AudioShelf.Podcasts, "Podcasts"), (AudioShelf.Recordings, "Recordings")];

    /// <summary>The book playing now, once one of its parts has been seen playing, so its speed is kept and given back.</summary>
    private (LibraryGroup Book, bool Seen)? _bookPlaying;

    /// <summary>The library view an audio file sits in now.</summary>
    private LibrarySource SourceOfAudio(LibraryEntry entry) => ShelfOf(entry) switch
    {
        AudioShelf.Audiobooks => LibrarySource.Audiobooks,
        AudioShelf.Podcasts => LibrarySource.Podcasts,
        AudioShelf.Recordings or AudioShelf.Sounds => LibrarySource.Recordings,
        _ => LibrarySource.Songs,
    };

    /// <summary>"Move to Music", "Move to Audiobooks" and the rest, whichever would move something; and a book's or a show's own actions.</summary>
    private void AddListeningMenuItems(MenuFlyout menu, IReadOnlyList<LibraryEntry> entries, IReadOnlyList<LibraryRow> chosen)
    {
        var audio = entries.Where(entry => entry.Kind == LibraryKind.Music).ToList();
        if (audio.Count == 0)
        {
            return;
        }

        if (audio is [{ } sound] && ShelfOf(sound) == AudioShelf.Sounds)
        {
            menu.Items.Add(MenuItem("Replay sound", "LibraryMenuReplaySound", () => Play([sound], 0)));
            menu.Items.Add(MenuItem("Sound controls...", "LibraryMenuSoundControls",
                () => Forget(ShowSoundControlsAsync(sound))));
        }

        if (audio is [{ } recording] && ShelfOf(recording) == AudioShelf.Recordings)
        {
            menu.Items.Add(MenuItem("Recording details...", "LibraryMenuRecordingDetails",
                () => Forget(ShowRecordingDetailsAsync(recording))));
            menu.Items.Add(MenuItem("Review waveform...", "LibraryMenuWaveform",
                () => Forget(ShowRecordingWaveformAsync(recording))));
        }

        if (audio is [{ } speech] && ShelfOf(speech) is AudioShelf.Podcasts or AudioShelf.Recordings)
        {
            menu.Items.Add(MenuItem("Read saved transcript...", "LibraryMenuReadTranscript",
                () => Forget(ShowTranscriptReaderAsync(speech.Path))));
        }

        if (audio is [{ } episode] && ShelfOf(episode) == AudioShelf.Podcasts)
        {
            menu.Items.Add(MenuItem("Podcast playback controls...", "LibraryMenuPodcastControls",
                () => Forget(ShowPodcastControlsAsync(episode))));
            menu.Items.Add(MenuItem("View local episode notes...", "LibraryMenuPodcastNotes",
                () => Forget(ShowPodcastNotesAsync(episode))));
        }

        foreach (var (shelf, name) in MoveTargets)
        {
            if (audio.Any(entry => ShelfOf(entry) is var now && now != shelf && !(shelf == AudioShelf.Recordings && now == AudioShelf.Sounds)))
            {
                menu.Items.Add(MenuItem("Move to " + name, "LibraryMenuMoveTo" + name, () => MoveAudio(audio, shelf, name)));
            }
        }

        if ((_librarySource is LibrarySource.Audiobooks or LibrarySource.Authors)
            && (chosen is [{ Item: LibraryGroup book }] ? book
                : _librarySource == LibrarySource.Authors ? _librarySeason : _libraryGroup) is { Entries.Count: > 0 } chosenBook)
        {
            var finished = Audiobooks.Finished(chosenBook);
            menu.Items.Add(MenuItem(finished ? "Mark as unfinished" : "Mark as finished", finished ? "LibraryMenuBookUnfinished" : "LibraryMenuBookFinished",
                () => MarkBookFinished(chosenBook, !finished)));
        }

        if (_librarySource == LibrarySource.Podcasts && (chosen is [{ Item: LibraryGroup show }] ? show : _libraryGroup) is { Entries.Count: > 0 } chosenShow)
        {
            menu.Items.Add(MenuItem("Read local show notes...", "LibraryMenuShowNotes",
                () => Forget(ShowPodcastShowNotesAsync(chosenShow))));
            menu.Items.Add(MenuItem("Play unplayed in order", "LibraryMenuPlayUnplayed", () => PlayUnplayedEpisodes(chosenShow)));
            menu.Items.Add(MenuItem("Mark all played", "LibraryMenuMarkAllPlayed", () => MarkListened([.. chosenShow.Entries.Select(episode => episode.Path)])));
        }
    }

    /// <summary>
    /// Always scope podcast playback to its own show, including Home's mixed new/continue shelves.
    /// No other show's episode may enter the automatic playback queue.
    /// </summary>
    private void PlayPodcastEpisode(LibraryEntry episode)
    {
        var show = Listening().Podcasts.FirstOrDefault(candidate =>
            candidate.Entries.Any(item => LibraryViews.SameLocation(item.Path, episode.Path)));
        if (show is null)
        {
            Play([episode], 0, resume: true);
            return;
        }

        var index = show.Entries.ToList().FindIndex(item =>
            LibraryViews.SameLocation(item.Path, episode.Path));
        Play(show.Entries, index, resume: true);
    }

    private int _episodeQueueRequest;

    /// <summary>
    /// Check indexed episodes against the local disk away from the window thread: a large show
    /// or disconnected drive must not freeze its menu. Only this show's available whole files
    /// enter the queue, and navigating away invalidates the pending playback request.
    /// </summary>
    private void PlayUnplayedEpisodes(LibraryGroup show)
    {
        var unplayed = ListeningViews.UnplayedInOrder(show, _player.LeftAt);
        var source = _librarySource;
        var opened = _libraryGroup?.Name;
        var request = Interlocked.Increment(ref _episodeQueueRequest);
        Forget(Task.Run(() => ListeningViews.UnplayedInOrder(
            new LibraryGroup(show.Name, show.Detail, unplayed), _ => null,
            episode => File.Exists(episode.Path) && !episode.Downloading && episode.Problem is null))
            .ContinueWith(task => OnWindowThread(() =>
            {
                if (_closing || request != Volatile.Read(ref _episodeQueueRequest)
                    || _librarySource != source || _libraryGroup?.Name != opened)
                {
                    return;
                }

                if (!task.IsCompletedSuccessfully)
                {
                    Say("The local episodes could not be checked. Try again.");
                    return;
                }

                Play(task.Result, 0);
            }), TaskScheduler.Default));
    }

    /// <summary>Keeps the person's move over the rules; the sidebar follows at once, and the view keeps its place.</summary>
    private void MoveAudio(IReadOnlyList<LibraryEntry> entries, AudioShelf shelf, string name)
    {
        _library!.SetOverride([.. entries.Select(entry => entry.Path)], new DetailOverride { Audio = AudioRules.StoredName(shelf) });
        Interlocked.Increment(ref _listeningVersion);
        Say(entries.Count == 1 ? $"{entries[0].Title} moved to {name}" : $"{Count(entries.Count, "file")} moved to {name}");
        if (!UpdateSections(immediate: true))
        {
            ShowLibrary();
        }
    }

    /// <summary>Marks the book finished (every part played, nowhere to resume) or not.</summary>
    private void MarkBookFinished(LibraryGroup book, bool finished)
    {
        Audiobooks.SetFinished(book, finished);
        if (finished)
        {
            MarkListened([.. book.Entries.Select(part => part.Path)]);
        }

        Say(finished ? $"{book.Name} marked as finished" : $"{book.Name} marked as unfinished");
        _shownLibrary = "";
        ShowLibrary();
    }

    /// <summary>Played through: nowhere to go back to, and played at least once.</summary>
    private void MarkListened(IReadOnlyList<string> paths)
    {
        foreach (var path in paths)
        {
            _player.Memory.Forget(path);
        }

        _library!.MarkPlayed(paths);
    }

    /// <summary>Play on an open book: from the chapter chosen, else on from where the book was left.</summary>
    private bool PlayOpenBook(IReadOnlyList<LibraryRow> chosen)
    {
        if (LibrarySearch.Text.Length > 0 ||
            (_librarySource == LibrarySource.Audiobooks ? _libraryGroup
                : _librarySource == LibrarySource.Authors ? _librarySeason : null) is not { Entries.Count: > 0 } book)
        {
            return false;
        }

        PlayBook(book, chosen is [{ Item: BookChapter chapter }] ? chapter : null);
        return true;
    }

    /// <summary>
    /// Plays a book from where it was left (or from <paramref name="chapter"/>), at its own speed. Only
    /// its parts are queued, so finishing it never runs on into another book. A finished book starts
    /// again from the top.
    /// </summary>
    private void PlayBook(LibraryGroup book, BookChapter? chapter)
    {
        var place = chapter is not null ? new BookPlace(IndexOfPart(book, chapter.Part), chapter.Start)
            : Audiobooks.Finished(book) ? default : ListeningViews.PlaceIn(book, _player.LeftAt);
        if (chapter is null && Audiobooks.Finished(book))
        {
            Audiobooks.SetFinished(book, false);
        }

        _bookPlaying = (book, false);
        var speed = Audiobooks.Speed(book) ?? 1;
        if (Math.Abs(_player.Speed - speed) > 0.005)
        {
            _player.SetSpeed(speed);
        }

        if (_settings.ShowPlayerWhenLibraryMediaStarts)
        {
            SetLibraryOpen(false);
        }

        _player.PlayFromLibrary(book.Entries, Math.Max(0, place.Part), place.At > TimeSpan.Zero ? place.At : null);
    }

    private static int IndexOfPart(LibraryGroup book, LibraryEntry part) =>
        Math.Max(0, book.Entries.Select(entry => entry.Path).ToList().IndexOf(part.Path));

    /// <summary>While a book plays, a change of speed is kept for it; when something else plays, normal speed comes back.</summary>
    private void FollowBookSpeed()
    {
        if (_bookPlaying is not { } playing || _player.Playlist.Current?.Location is not { } location)
        {
            return;
        }

        if (playing.Book.Entries.Any(part => LibraryViews.SameLocation(part.Path, location)))
        {
            _bookPlaying = (playing.Book, true);
            if (Math.Abs(_player.Speed - (Audiobooks.Speed(playing.Book) ?? 1)) > 0.005)
            {
                Audiobooks.KeepSpeed(playing.Book, _player.Speed);
            }
        }
        else if (playing.Seen)
        {
            _bookPlaying = null;
            if (Math.Abs(_player.Speed - 1) > 0.005)
            {
                _player.SetSpeed(1);
            }
        }
    }
}
