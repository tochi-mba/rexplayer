using Rex.Media.AppCore.Library;
using Rex.Media.AppCore.Metadata;
using Rex.Media.Library;
using Rex.Media.Settings;

namespace Rex.Media.App;

/// <summary>
/// Music's share of the metadata fetcher: albums (facts and, to fill a gap, covers) and then
/// artists (facts, a photo and a summary), all under the Music switch and its folder exclusions,
/// on the same one-at-a-time queue and per-host pace as everything else. Artists are queued after
/// the albums, the ones with the most songs first. The pages read the records from memory only.
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>Book jobs share the music metadata switch but are distinct from artists and albums.</summary>
    private const char BookJobMark = '\u001d';

    private readonly Dictionary<string, EnrichedBook> _metadataBooks = new(StringComparer.Ordinal);

    private EnrichedBook? MetadataBook(LibraryGroup book)
    {
        if (!_settings.Metadata.Music)
        {
            return null;
        }

        lock (_metadataGate)
        {
            if (_metadataBooks.TryGetValue(OpenLibraryBooks.BookKey(book.Name, book.Detail), out var found) && found.WorkId is not null)
            {
                return found;
            }

            // A catalogue-inferred author still gets the synopsis credited to that exact work.
            return _metadataBooks.TryGetValue(OpenLibraryBooks.BookKey(book.Name, null), out var inferred)
                && inferred.WorkId is not null && book.Detail is not null
                && MetadataMatch.Normalize(inferred.Author ?? "") == MetadataMatch.Normalize(book.Detail) ? inferred : null;
        }
    }

    private bool LoadCachedBooks(MetadataFetcher fetcher)
    {
        var cached = fetcher.CachedBooks();
        lock (_metadataGate)
        {
            foreach (var book in cached)
            {
                _metadataBooks[OpenLibraryBooks.BookKey(book.Title, book.InferredAuthor ? null : book.Author)] = book;
            }
        }

        return cached.Count > 0;
    }

    /// <summary>Look up known writers directly; unknown writers only by a complete, exact-title result.</summary>
    private void PlanBookWork(IReadOnlyList<LibraryEntry> entries, MetadataSettings settings)
    {
        foreach (var book in ListeningViews.Arrange(entries, MusicLookupOf).Audiobooks)
        {
            if (book.Name.Length < 3 || !book.Name.Any(char.IsLetter))
            {
                continue;
            }

            lock (_metadataGate)
            {
                if (_metadataBooks.TryGetValue(OpenLibraryBooks.BookKey(book.Name, book.Detail), out var known) && known.Current)
                {
                    continue;
                }
            }

            if (book.Entries.Any(entry => MetadataPolicy.Allows(settings, MetadataWork.Music, entry.Path)))
            {
                lock (_metadataGate)
                {
                    _metadataPlan.Add(new MetadataJob(MetadataWork.Music, BookJobMark + book.Name + "\u001f" + book.Detail));
                }
            }
        }
    }

    private static bool IsBookJob(MetadataJob job) => job.Kind == MetadataWork.Music
        && job.Key.Length > 1 && job.Key[0] == BookJobMark;

    private static (string Title, string? Author) BookFromJob(MetadataJob job)
    {
        var split = job.Key.IndexOf('\u001f', 1);
        var author = job.Key[(split + 1)..];
        return (job.Key[1..split], author.Length == 0 ? null : author);
    }

    private static Task<bool> FetchBookJobAsync(MetadataFetcher fetcher, MetadataJob job, CancellationToken token)
    {
        var (title, author) = BookFromJob(job);
        return author is null ? fetcher.FetchUnknownBookAsync(title, token) : fetcher.FetchBookAsync(title, author, token);
    }

    private void BookJobDone(MetadataFetcher fetcher, MetadataJob job)
    {
        var (title, author) = BookFromJob(job);
        if (fetcher.CachedBook(title, author) is not { } book)
        {
            return;
        }

        lock (_metadataGate)
        {
            _metadataBooks[OpenLibraryBooks.BookKey(title, author)] = book;
        }

        if (book.WorkId is not null)
        {
            RefreshLibraryFromMetadata();
        }
    }

    /// <summary>Marks an artist's job apart from an album's, whose key holds artist and album apart.</summary>
    private const char ArtistJobMark = '\u001e';

    private readonly Dictionary<string, EnrichedArtist> _metadataArtists = new(StringComparer.Ordinal);

    /// <summary>The fetched record for an artist, from memory only: safe on the window's thread; null with music lookups off.</summary>
    private EnrichedArtist? MetadataArtist(string name)
    {
        if (!_settings.Metadata.Music)
        {
            return null;
        }

        lock (_metadataGate)
        {
            return _metadataArtists.TryGetValue(MetadataFetcher.ArtistKey(name), out var artist) && artist.MusicBrainzId is not null ? artist : null;
        }
    }

    /// <summary>The fetched record for an album, from memory only; null with music lookups off or no match.</summary>
    private EnrichedAlbum? MetadataAlbum(string? artist, string album)
    {
        if (!_settings.Metadata.Music)
        {
            return null;
        }

        lock (_metadataGate)
        {
            return _metadataAlbums.TryGetValue(MusicBrainz.AlbumKey(artist, album), out var found) && found.ReleaseGroupId is not null ? found : null;
        }
    }

    /// <summary>An artist's fetched photo on disk, or null; reads the disk, so it is called off the window's thread.</summary>
    private string? ArtistPhotoPath(string name)
    {
        if (MetadataArtist(name) is not { HasPhoto: true })
        {
            return null;
        }

        var path = MetadataCacheNow.PathFor(MetadataFetcher.ArtistPhotoKind, MetadataFetcher.ArtistKey(name), MetadataFetcher.ArtExtension);
        return File.Exists(path) ? path : null;
    }

    /// <summary>Fills the artists' lookup table from the cache, on the fetcher's thread at its start.</summary>
    private void LoadCachedArtists(MetadataFetcher fetcher)
    {
        var known = fetcher.CachedArtists();
        lock (_metadataGate)
        {
            foreach (var artist in known)
            {
                _metadataArtists[MetadataFetcher.ArtistKey(artist.Name)] = artist;
            }
        }
    }

    /// <summary>
    /// Queues every artist the cache does not hold in this build's shape, the ones with the most
    /// songs first, when at least one of their songs lies outside the excluded folders.
    /// </summary>
    private void PlanArtistWork(IReadOnlyList<LibraryEntry> entries, MetadataSettings metadata)
    {
        foreach (var artist in LibraryViews.Artists(entries).Where(artist => artist.Name != LibraryViews.UnknownArtist).OrderByDescending(artist => artist.Entries.Count))
        {
            lock (_metadataGate)
            {
                if (_metadataArtists.TryGetValue(MetadataFetcher.ArtistKey(artist.Name), out var known) && known.Current)
                {
                    continue;
                }
            }

            if (artist.Entries.Any(entry => MetadataPolicy.Allows(metadata, MetadataWork.Music, entry.Path)))
            {
                lock (_metadataGate)
                {
                    _metadataPlan.Add(new MetadataJob(MetadataWork.Music, ArtistJobMark + artist.Name));
                }
            }
        }
    }

    private static bool IsArtistJob(MetadataJob job) => job.Kind == MetadataWork.Music && job.Key.Length > 1 && job.Key[0] == ArtistJobMark;

    /// <summary>Runs an artist's job; true when it settled.</summary>
    private static Task<bool> FetchArtistJobAsync(MetadataFetcher fetcher, MetadataJob job, CancellationToken token) =>
        fetcher.FetchArtistAsync(job.Key[1..], token);

    /// <summary>Takes a settled artist into memory and redraws the library when there is something new to show.</summary>
    private void ArtistJobDone(MetadataFetcher fetcher, MetadataJob job)
    {
        var name = job.Key[1..];
        if (fetcher.CachedArtist(name) is not { } artist)
        {
            return;
        }

        lock (_metadataGate)
        {
            _metadataArtists[MetadataFetcher.ArtistKey(name)] = artist;
        }

        if (artist.MusicBrainzId is not null)
        {
            RefreshLibraryFromMetadata();
        }
    }
}
