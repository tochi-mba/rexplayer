using System.Globalization;
using Rex.Media.AppCore.Library;
using Rex.Media.AppCore.Metadata;
using Rex.Media.AppCore.Player;
using Rex.Media.Library;
using Rex.Media.Settings;
using static Rex.Media.AppCore.Library.LibraryActions;

namespace Rex.Media.App;

/// <summary>
/// The Audiobooks, Podcasts and Recordings views (<see cref="AudioRules"/> decides what is in each).
/// Each is listed in the sidebar only while it has something in it (<see cref="LibrarySections"/>),
/// and the music views never show what is in them. Nothing is worked out until the library is
/// opened: the classification is built from the stored entries when a view first asks, and again
/// only after the library or the music lookups change.
/// </summary>
public sealed partial class MainWindow
{
    private readonly Dictionary<string, AudioShelf> _audioShelfOf = new(StringComparer.OrdinalIgnoreCase);
    private ListeningShelves? _listening;
    private int _listeningVersion;
    private int _listeningBuiltAt = -1;
    private int _listeningAlbums = -1;
    private int _listeningBooks = -1;
    private AudiobookMemory? _audiobookMemory;

    private AudiobookMemory Audiobooks => _audiobookMemory ??= new AudiobookMemory(_library!.Store);

    private static bool IsListening(LibrarySource source) => source is LibrarySource.Audiobooks or LibrarySource.Authors or LibrarySource.Podcasts or LibrarySource.Recordings;

    private static AudioShelf ShelfOfSource(LibrarySource source) => source switch
    {
        LibrarySource.Audiobooks or LibrarySource.Authors => AudioShelf.Audiobooks,
        LibrarySource.Podcasts => AudioShelf.Podcasts,
        _ => AudioShelf.Recordings,
    };

    private void WireListening()
    {
        _library!.Changed += (_, _) => Interlocked.Increment(ref _listeningVersion);
        _player.Changed += (_, _) => FollowBookSpeed();
        WireSections();
    }

    /// <summary>What the music lookups said about an audio file's album, from memory only: safe on the window's thread.</summary>
    private MusicLookup MusicLookupOf(LibraryEntry entry)
    {
        var metadata = _settings.Metadata;
        if (!metadata.Music || entry.Album is not { } album || MetadataPolicy.Excludes(metadata.Excluded, entry.Path))
        {
            return MusicLookup.NotSearched;
        }

        lock (_metadataGate)
        {
            return !_metadataAlbums.TryGetValue(MusicBrainz.AlbumKey(entry.FiledArtist, album), out var known) || known.LocalArt ? MusicLookup.NotSearched
                : known.ReleaseGroupId is null ? MusicLookup.Unmatched : MusicLookup.Matched;
        }
    }

    /// <summary>The whole library's audio that is not music, worked out again only after the library or the lookups changed.</summary>
    private ListeningShelves Listening()
    {
        var version = Volatile.Read(ref _listeningVersion);
        int albums;
        int books;
        lock (_metadataGate)
        {
            albums = _metadataAlbums.Count;
            books = _metadataBooks.Count;
        }

        if (_listening is null || version != _listeningBuiltAt || albums != _listeningAlbums || books != _listeningBooks)
        {
            _listening = ListeningViews.Arrange(_library!.Entries, MusicLookupOf, book =>
            {
                if (!_settings.Metadata.Music || !book.Entries.Any(entry =>
                    MetadataPolicy.Allows(_settings.Metadata, MetadataWork.Music, entry.Path)))
                {
                    return null;
                }

                lock (_metadataGate)
                {
                    return _metadataBooks.TryGetValue(OpenLibraryBooks.BookKey(book.Name, null), out var found)
                        && found.Current && found.WorkId is not null ? found.Author : null;
                }
            });
            _listeningBuiltAt = version;
            _listeningAlbums = albums;
            _listeningBooks = books;
            _audioShelfOf.Clear();
            foreach (var (shelf, files) in new (AudioShelf, IEnumerable<LibraryEntry>)[]
            {
                (AudioShelf.Audiobooks, _listening.Audiobooks.SelectMany(book => book.Entries)),
                (AudioShelf.Podcasts, _listening.Podcasts.SelectMany(show => show.Entries)),
                (AudioShelf.Recordings, _listening.Recordings),
                (AudioShelf.Sounds, _listening.Sounds),
            })
            {
                foreach (var file in files)
                {
                    _audioShelfOf[file.Path] = shelf;
                }
            }
        }

        return _listening;
    }

    /// <summary>Where an audio file is now: Music, or one of the listening shelves.</summary>
    private AudioShelf ShelfOf(LibraryEntry entry)
    {
        Listening();
        return _audioShelfOf.GetValueOrDefault(entry.Path, AudioShelf.Music);
    }

    /// <summary>The entries without the audio that is not music, for the music views.</summary>
    private IReadOnlyList<LibraryEntry> WithoutListening(IReadOnlyList<LibraryEntry> entries)
    {
        Listening();
        return _audioShelfOf.Count == 0 ? entries : [.. entries.Where(entry => !_audioShelfOf.ContainsKey(entry.Path))];
    }

    /// <summary>The Audiobooks, Podcasts or Recordings view over <paramref name="entries"/> (already filtered as the view's choices say).</summary>
    private (string Kicker, string Title, string Subtitle, List<LibraryRowGroup> Sections, bool GroupsView) ListeningView(IReadOnlyList<LibraryEntry> entries)
    {
        var title = LibrarySourceItems.First(pair => pair.Source == _librarySource).Item.Name;
        Listening();
        var mine = entries.Where(entry => _audioShelfOf.TryGetValue(entry.Path, out var shelf) && (shelf == ShelfOfSource(_librarySource) || (shelf == AudioShelf.Sounds && _librarySource == LibrarySource.Recordings))).ToList();
        switch (_librarySource)
        {
            case LibrarySource.Authors:
                var authoredBooks = ListeningViews.Books(mine);
                var authors = ListeningViews.Authors(authoredBooks);
                if (_libraryGroup is { } openedAuthor)
                {
                    var author = LibraryViews.RefreshGroup(authors, openedAuthor);
                    _libraryGroup = author;
                    var theirBooks = ListeningViews.BooksByAuthor(authoredBooks, author.Name);
                    if (_librarySeason is { } openedAuthorBook)
                    {
                        var book = LibraryViews.RefreshGroup(theirBooks, openedAuthorBook, detailIdentifiesGroup: true);
                        _librarySeason = book;
                        var chapters = book.Entries.Count == 0 ? [] : ListeningViews.Chapters(book);
                        var place = book.Entries.Count == 0 ? default : ListeningViews.PlaceIn(book, _player.LeftAt);
                        var currentChapter = ListeningViews.CurrentChapterIndex(chapters, book, place);
                        return ("AUDIOBOOK", book.Name, BookLine(book, chapters.Count),
                            [new LibraryRowGroup("", chapters.Select((chapter, index) => ChapterRow(book, chapter, index, currentChapter)))], false);
                    }

                    return ("AUTHOR", author.Name, Count(theirBooks.Count, "book") + " · " + Total(author.Entries),
                        [new LibraryRowGroup("", theirBooks.Select(BookRow))], true);
                }

                return ("LIBRARY", title, Count(authors.Count, "author"),
                    [new LibraryRowGroup("", authors.Select(author =>
                        new LibraryRow(author.Name, Count(ListeningViews.BooksByAuthor(authoredBooks, author.Name).Count, "book"),
                            "", author, hasPicture: false, picturePlaceholder: "✎")))], true);
            case LibrarySource.Audiobooks:
                var books = ListeningViews.Books(mine);
                if (_libraryGroup is { } openedBook)
                {
                    var book = LibraryViews.RefreshGroup(books, openedBook, detailIdentifiesGroup: true);
                    _libraryGroup = book;
                    var chapters = book.Entries.Count == 0 ? [] : ListeningViews.Chapters(book);
                    var place = book.Entries.Count == 0 ? default : ListeningViews.PlaceIn(book, _player.LeftAt);
                    var currentChapter = ListeningViews.CurrentChapterIndex(chapters, book, place);
                    return ("AUDIOBOOK", book.Name, BookLine(book, chapters.Count),
                        [new LibraryRowGroup("", chapters.Select((chapter, index) => ChapterRow(book, chapter, index, currentChapter)))], false);
                }

                var going = books.Where(book => ListeningViews.Started(book, _player.LeftAt) && !Audiobooks.Finished(book)).ToList();
                List<LibraryRowGroup> shelves = going.Count > 0
                    ? [new LibraryRowGroup("Continue listening", going.Select(BookRow)), new LibraryRowGroup("All books", books.Select(BookRow))]
                    : [new LibraryRowGroup("", books.Select(BookRow))];
                return ("LIBRARY", title, Count(books.Count, "book") + " · " + Total(mine), shelves, true);
            case LibrarySource.Podcasts:
                var shows = ListeningViews.Shows(mine);
                if (_libraryGroup is { } openedShow)
                {
                    var show = LibraryViews.RefreshGroup(shows, openedShow);
                    _libraryGroup = show;
                    var unplayed = ListeningViews.Unplayed(show, _player.LeftAt);
                    return ("PODCAST", show.Name, string.Join(" · ", new[] { show.Detail, Count(show.Entries.Count, "episode"), unplayed + " unplayed" }.Where(part => part is not null)),
                        [new LibraryRowGroup("", show.Entries.Select(PodcastEpisodeRow))], false);
                }

                return ("LIBRARY", title, Count(shows.Count, "show") + " · " + Count(mine.Count, "episode"), [new LibraryRowGroup("", shows.Select(ShowRow))], true);
            default:
                var recordings = mine.Where(entry => _audioShelfOf[entry.Path] == AudioShelf.Recordings).ToList();
                var sounds = mine.Where(entry => _audioShelfOf[entry.Path] == AudioShelf.Sounds).OrderBy(entry => entry.Title, NaturalOrder.Instance).ToList();
                var sections = ListeningViews.RecordingSections(recordings, sounds, DateTime.Now);
                return ("LIBRARY", title, string.Join(" · ", new[] { Count(recordings.Count, "recording"), sounds.Count > 0 ? Count(sounds.Count, "sound") : null, Total(mine) }.Where(part => part is not null)),
                    [.. sections.Select(section => new LibraryRowGroup(section.Header, section.Entries.Select(RecordingRow)))], false);
        }
    }

    /// <summary>A book's line under its name on its page: author, length, chapters and how far it is.</summary>
    private string BookLine(LibraryGroup book, int chapters) =>
        string.Join(" · ", new[]
        {
            book.Detail ?? "Unknown author",
            BookCredits.Narrator(book) is { } narrator ? "Narrated by " + narrator : null,
            book.Entries.Count > 0 && book.Entries.All(part => part.Duration is { Ticks: > 0 })
                ? Total(book.Entries) : "Length unknown",
            Count(chapters, "chapter"),
            BookProgressText(book),
        }.Where(part => !string.IsNullOrEmpty(part)));

    private string BookProgressText(LibraryGroup book)
    {
        if (book.Entries.Count == 0)
        {
            return "";
        }

        if (Audiobooks.Finished(book))
        {
            return "Finished";
        }

        var progress = ListeningViews.Progress(book, ListeningViews.PlaceIn(book, _player.LeftAt));
        if (!ListeningViews.Started(book, _player.LeftAt))
        {
            return "Not started";
        }

        var remaining = ListeningViews.Remaining(book, ListeningViews.PlaceIn(book, _player.LeftAt));
        var listened = string.Create(CultureInfo.CurrentCulture, $"{progress:P0} listened");
        return remaining is { } left ? listened + " · " + TimeText.Format(left) + " left" : listened;
    }

    /// <summary>A book's card: its cover, name and author, how far it is, and a ring of its progress.</summary>
    private LibraryRow BookRow(LibraryGroup book)
    {
        var progress = Audiobooks.Finished(book) ? 1 : ListeningViews.Progress(book, ListeningViews.PlaceIn(book, _player.LeftAt));
        return new LibraryRow(book.Name, book.Detail ?? "Unknown author", BookProgressText(book), book, hasPicture: true, picturePlaceholder: "▤",
            progress: ListeningViews.Started(book, _player.LeftAt) || Audiobooks.Finished(book) ? Math.Max(progress, 0.01) : 0);
    }

    /// <summary>One chapter of an open book; the one it is up to says so.</summary>
    private LibraryRow ChapterRow(LibraryGroup book, BookChapter chapter, int index, int currentChapter)
    {
        var detail = book.Entries.Count > 1 && chapter.Part.Chapters is { Count: > 0 } ? chapter.Part.Title : "";
        return new LibraryRow(chapter.Title, index == currentChapter && ListeningViews.Started(book, _player.LeftAt) ? (detail.Length > 0 ? detail + " · " : "") + "Up to here" : detail,
            TimeText.Format(chapter.Start, chapter.Part.Duration), chapter, badge: (index + 1).ToString(CultureInfo.CurrentCulture));
    }

    /// <summary>A show's card: its name, its newest episode's date and how many are unplayed.</summary>
    private LibraryRow ShowRow(LibraryGroup show)
    {
        var unplayed = ListeningViews.Unplayed(show, _player.LeftAt);
        var newest = ListeningViews.NewestEpisode(show);
        var detail = string.Join(" · ", new[]
        {
            show.Detail,
            newest == DateTime.MinValue ? null : "Newest " + newest.ToLocalTime().ToString("d", CultureInfo.CurrentCulture),
            unplayed == 0 ? "All played" : unplayed + " unplayed",
        }.Where(part => part is not null));
        return new LibraryRow(show.Name, detail, Count(show.Entries.Count, "episode"), show, hasPicture: true, picturePlaceholder: "◉");
    }

    /// <summary>An episode's line: new, played or where it was left, and its date.</summary>
    private LibraryRow PodcastEpisodeRow(LibraryEntry episode)
    {
        var left = _player.LeftAt(episode.Path);
        var state = left is { } at ? "Stopped at " + TimeText.Format(at, episode.Duration ?? TimeSpan.Zero) : episode.Plays > 0 ? "Played" : "New";
        var date = ListeningViews.EpisodeDate(episode);
        var detail = date == DateTime.MinValue ? state : state + " · " + date.ToLocalTime().ToString("d", CultureInfo.CurrentCulture);
        var progress = left is { } stopped && episode.Duration is { TotalSeconds: > 0 } length ? Math.Clamp(stopped / length, 0, 1) : 0;
        return new LibraryRow(episode.Title, detail, episode.Duration is { } duration ? TimeText.Format(duration, duration) : "", episode,
            hasPicture: true, picturePlaceholder: "◉", badge: episode.Track?.ToString(CultureInfo.CurrentCulture) ?? "", progress: progress,
            isPlaying: LibraryViews.SameLocation(episode.Path, _player.Playlist.Current?.Location));
    }

    /// <summary>A recording's line: when it was made and how long it runs.</summary>
    private LibraryRow RecordingRow(LibraryEntry recording)
    {
        var at = ListeningViews.RecordedAt(recording);
        var detail = at is { } made ? (made.Kind == DateTimeKind.Utc ? made.ToLocalTime() : made).ToString("g", CultureInfo.CurrentCulture) : "";
        return new LibraryRow(recording.Title, detail, recording.Duration is { } duration ? TimeText.Format(duration, duration) : "", recording,
            picturePlaceholder: "●", isPlaying: LibraryViews.SameLocation(recording.Path, _player.Playlist.Current?.Location));
    }

    /// <summary>
    /// The Songs view's sections: as its choices say, with the long mixes and sets
    /// (<see cref="AudioRules.IsLongMix"/>) apart at the foot while it is not grouped otherwise.
    /// </summary>
    private List<LibraryRowGroup> SongSections(IReadOnlyList<LibraryEntry> songs, LibraryViewChoice choice)
    {
        var mixes = songs.Where(AudioRules.IsLongMix).ToList();
        if (mixes.Count == 0 || choice.Grouping != LibraryGrouping.None)
        {
            return EntrySections(songs, choice);
        }

        return [.. EntrySections([.. songs.Except(mixes)], choice).Where(section => section.Count > 0), .. EntrySections(mixes, choice).Select(section => new LibraryRowGroup("Long mixes and sets", section))];
    }

    /// <summary>Home's listening shelves: books and episodes part-way, then new episodes; each empty, and so not shown, when there is none.</summary>
    private IEnumerable<(string Title, LibrarySource See, IReadOnlyList<LibraryRow> Rows, bool Wide)> ListeningShelvesForHome()
    {
        var shelves = Listening();
        var going = ListeningViews.ContinueListening(shelves, _player.LeftAt, Audiobooks.Finished).Take(20).ToList();
        yield return ("Continue listening", going.Any(item => item.Book is not null) ? LibrarySource.Audiobooks : LibrarySource.Podcasts,
            [.. going.Select(item => item.Book is { } book ? BookRow(book) : PodcastEpisodeRow(item.Entry))], false);
        yield return ("New episodes", LibrarySource.Podcasts, [.. ListeningViews.NewEpisodes(shelves, _player.LeftAt, DateTime.UtcNow).Take(20).Select(PodcastEpisodeRow)], false);
    }
}
