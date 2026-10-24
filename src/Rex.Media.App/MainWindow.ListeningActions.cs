using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Rex.Media.AppCore.Commands;
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

        if (BookForListeningMenu(chosen) is { Entries.Count: > 0 } chosenBook)
        {
            var finished = Audiobooks.Finished(chosenBook);
            menu.Items.Add(MenuItem(finished ? "Mark as unfinished" : "Mark as finished", finished ? "LibraryMenuBookUnfinished" : "LibraryMenuBookFinished",
                () => MarkBookFinished(chosenBook, !finished)));

            if (chosen is [{ Item: BookChapter chapter }])
            {
                menu.Items.Add(MenuItem("Play from this chapter", "LibraryMenuPlayChapter", () => PlayBook(chosenBook, chapter)));
            }

            menu.Items.Add(MenuItem(finished ? "Start this book again" : "Resume this book",
                "LibraryMenuResumeBook", () => PlayBook(chosenBook, null)));

            if (_player.Playlist.Current?.Location is { } current
                && chosenBook.Entries.Any(part => LibraryViews.SameLocation(part.Path, current)))
            {
                var tools = new MenuFlyoutSubItem { Text = "Audiobook playback" };
                AutomationProperties.SetAutomationId(tools, "LibraryMenuBookTools");
                tools.Items.Add(MenuItem("Previous chapter", "LibraryMenuBookPreviousChapter", () => MoveBookChapter(chosenBook, -1)));
                tools.Items.Add(MenuItem("Next chapter", "LibraryMenuBookNextChapter", () => MoveBookChapter(chosenBook, 1)));
                tools.Items.Add(MenuItem("Back 10 seconds", "LibraryMenuBookBack10", () => Run(CommandCatalog.JumpBackShort)));
                tools.Items.Add(MenuItem("Forward 10 seconds", "LibraryMenuBookForward10", () => Run(CommandCatalog.JumpForwardShort)));
                tools.Items.Add(MenuItem("Back 1 minute", "LibraryMenuBookBackMinute", () => Run(CommandCatalog.JumpBackMedium)));
                tools.Items.Add(MenuItem("Forward 1 minute", "LibraryMenuBookForwardMinute", () => Run(CommandCatalog.JumpForwardMedium)));
                tools.Items.Add(MenuItem("Bookmark this position", "LibraryMenuBookBookmark", () => Run(CommandCatalog.AddBookmark)));
                tools.Items.Add(MenuItem("Sleep in 15 minutes", "LibraryMenuBookSleep15", () => Run(CommandCatalog.SleepPrefix + "15")));
                tools.Items.Add(MenuItem("Sleep at the end of this part", "LibraryMenuBookSleepEnd", () => Run(CommandCatalog.SleepAtEndOfItem)));
                menu.Items.Add(tools);
            }
        }

        if (_librarySource == LibrarySource.Podcasts && (chosen is [{ Item: LibraryGroup show }] ? show : _libraryGroup) is { Entries.Count: > 0 } chosenShow)
        {
            menu.Items.Add(MenuItem("Read local show notes...", "LibraryMenuShowNotes",
                () => Forget(ShowPodcastShowNotesAsync(chosenShow))));
            menu.Items.Add(MenuItem("Play unplayed in order", "LibraryMenuPlayUnplayed", () => PlayUnplayedEpisodes(chosenShow)));
            menu.Items.Add(MenuItem("Mark all played", "LibraryMenuMarkAllPlayed", () => MarkListened([.. chosenShow.Entries.Select(episode => episode.Path)])));
        }
    }

    /// <summary>Only a book, not the parent author, may receive book completion and playback actions.</summary>
    private LibraryGroup? BookForListeningMenu(IReadOnlyList<LibraryRow> chosen)
    {
        if (_librarySource == LibrarySource.Authors && _libraryGroup is null)
        {
            return null;
        }

        // A completed audiobook is still a logical book. Offer "Mark as
        // unfinished" for that one edition, without changing its physical files'
        // play counts or exposing book actions on completed movies or podcasts.
        if (_librarySource == LibrarySource.Completed)
        {
            return chosen is [{ Item: LibraryGroup completed }]
                && Listening().Audiobooks.Any(book =>
                    ListeningViews.BookIdentity(book) == ListeningViews.BookIdentity(completed))
                ? completed : null;
        }

        if (_librarySource is not (LibrarySource.Authors or LibrarySource.Audiobooks))
        {
            return null;
        }

        return chosen is [{ Item: LibraryGroup book }] ? book
            : _librarySource == LibrarySource.Authors ? _librarySeason : _libraryGroup;
    }

    /// <summary>
    /// Opening an episode from a mixed Home/search shelf queues only available files from
    /// its show, in the natural oldest-to-newest listening order. Disk probes are off-thread;
    /// a later click or a route change invalidates the previous pending request.
    /// </summary>
    private void PlayPodcastEpisode(LibraryEntry episode)
    {
        var show = Listening().Podcasts.FirstOrDefault(candidate =>
            candidate.Entries.Any(item => LibraryViews.SameLocation(item.Path, episode.Path)));
        IReadOnlyList<LibraryEntry> candidates = show is null ? [episode] : show.Entries;
        var source = _librarySource;
        var opened = _libraryGroup?.Name;
        var request = Interlocked.Increment(ref _episodeQueueRequest);
        Forget(Task.Run(() => ListeningViews.AvailableEpisodeQueue(candidates, episode.Path,
                item => !item.Downloading && item.Problem is null && File.Exists(item.Path)))
            .ContinueWith(task => OnWindowThread(() =>
            {
                if (_closing || request != Volatile.Read(ref _episodeQueueRequest)
                    || _librarySource != source || _libraryGroup?.Name != opened)
                {
                    return;
                }

                if (!task.IsCompletedSuccessfully)
                {
                    Say("The local podcast files could not be checked.");
                    return;
                }

                var (playable, index) = task.Result;
                if (index < 0)
                {
                    Say("This episode is missing, incomplete or unavailable locally.");
                    return;
                }

                Play(playable, index, resume: true);
            }), TaskScheduler.Default));
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

    /// <summary>Marks the logical book finished without altering the files' play counts or resume points.</summary>
    private void MarkBookFinished(LibraryGroup book, bool finished)
    {
        Audiobooks.SetFinished(book, finished);
        Say(finished ? $"{book.Name} marked as finished" : $"{book.Name} marked as unfinished");
        _shownLibrary = "";
        if (!UpdateSections(immediate: true))
        {
            ShowLibrary();
        }
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

    /// <summary>Treat an audiobook card as a book, not as a generic list of files.</summary>
    private bool PlayBookCard(LibraryRow? row)
    {
        if (row?.Item is not LibraryGroup book)
        {
            return false;
        }

        var bookCard = (_librarySource == LibrarySource.Audiobooks && _libraryGroup is null)
            || (_librarySource == LibrarySource.Authors && _libraryGroup is not null && _librarySeason is null)
            || (_librarySource == LibrarySource.Completed && Listening().Audiobooks.Any(candidate =>
                ListeningViews.BookIdentity(candidate) == ListeningViews.BookIdentity(book)));
        if (!bookCard)
        {
            return false;
        }

        PlayBook(book, null);
        return true;
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
        if (chapter is not null && IndexOfPart(book, chapter.Part) < 0)
        {
            Say("That chapter is no longer in this book.");
            return;
        }

        var place = chapter is not null ? new BookPlace(IndexOfPart(book, chapter.Part), chapter.Start)
            : Audiobooks.Finished(book) ? default : ListeningViews.PlaceIn(book, _player.LeftAt);
        if (book.Entries.Count == 0)
        {
            LibraryNotice.Message = "This book has no playable parts.";
            LibraryNotice.IsOpen = true;
            return;
        }

        if (NotYetPlayable(book.Entries[Math.Clamp(place.Part, 0, book.Entries.Count - 1)]) is { } blocked)
        {
            LibraryNotice.Message = blocked;
            LibraryNotice.IsOpen = true;
            return;
        }

        var wasFinished = Audiobooks.Finished(book);
        if (wasFinished)
        {
            Audiobooks.SetFinished(book, false);
        }

        // Playlist.Clear does not reset shuffle. Without this, a user who
        // shuffled music immediately before opening a multi-part book hears its
        // files in random order, even though book pages hide the Shuffle action.
        if (_player.Playlist.Shuffle)
        {
            _player.Playlist.Shuffle = false;
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
        if (wasFinished)
        {
            _shownLibrary = "";
            if (!UpdateSections(immediate: true))
            {
                ShowLibrary();
            }
        }
    }

    private bool MoveBookChapter(LibraryGroup book, int direction)
    {
        var location = _player.Playlist.Current?.Location;
        if (location is null || !book.Entries.Any(part => LibraryViews.SameLocation(part.Path, location))) return false;
        var part = book.Entries.ToList().FindIndex(entry => LibraryViews.SameLocation(entry.Path, location));
        var chapters = ListeningViews.Chapters(book);
        var targetIndex = ListeningViews.AdjacentChapterIndex(chapters, book,
            new BookPlace(part, _player.Position), next: direction > 0);
        if (targetIndex < 0 || targetIndex >= chapters.Count)
        {
            Say(direction < 0 ? "Already at the first chapter" : "Already at the last chapter");
            return true;
        }
        var target = chapters[targetIndex];
        if (LibraryViews.SameLocation(target.Part.Path, location)) _player.Seek(target.Start);
        else PlayBook(book, target);
        return true;
    }

    private static int IndexOfPart(LibraryGroup book, LibraryEntry part) =>
        book.Entries.ToList().FindIndex(entry => LibraryViews.SameLocation(entry.Path, part.Path));

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
