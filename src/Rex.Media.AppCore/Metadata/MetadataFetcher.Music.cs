using System.Text.Json;

namespace Rex.Media.AppCore.Metadata;

/// <summary>
/// Albums and artists from MusicBrainz (with covers from the Cover Art Archive), artist photos
/// from Wikimedia Commons through Wikidata, and artist summaries from English Wikipedia; none
/// needs a key or a sign-up. Each record is kept on disk, so nothing is asked twice.
/// </summary>
public sealed partial class MetadataFetcher
{
    /// <summary>The cache kind an album's record lives under.</summary>
    public const string MusicKind = "music";

    /// <summary>The cache kind an album's fetched cover lives under, with <see cref="ArtExtension"/>.</summary>
    public const string CoverKind = "albumart";

    /// <summary>The cache kind an artist's record lives under.</summary>
    public const string ArtistKind = "artist";

    /// <summary>The cache kind an artist's fetched photo lives under, with <see cref="ArtExtension"/>.</summary>
    public const string ArtistPhotoKind = "artistphoto";

    /// <summary>Cached audiobook details: one exact title-and-author match from Open Library.</summary>
    public const string BookKind = "book";

    public EnrichedBook? CachedBook(string title, string? author) =>
        _cache.ReadText(BookKind, OpenLibraryBooks.BookKey(title, author)) is { } json ? EnrichedBook.FromJson(json) : null;

    public IReadOnlyList<EnrichedBook> CachedBooks() => CachedAll(BookKind, EnrichedBook.FromJson);

    /// <summary>
    /// Fetches an audiobook's work description only when both its title and author match.
    /// A confirmed miss is cached too; a failed service response retries later.
    /// </summary>
    public async Task<bool> FetchBookAsync(string title, string author, CancellationToken token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(author);
        var key = OpenLibraryBooks.BookKey(title, author);
        if (CachedBook(title, author) is { Current: true })
        {
            return true;
        }

        try
        {
            if (await TextAsync(OpenLibraryBooks.SearchUri(title, author), token).ConfigureAwait(false) is not { } answer)
            {
                return false;
            }

            var id = OpenLibraryBooks.Match(title, author, answer);
            string? synopsis = null;
            if (id is not null)
            {
                if (await TextAsync(OpenLibraryBooks.WorkUri(id), token).ConfigureAwait(false) is not { } work)
                {
                    return false;
                }

                synopsis = OpenLibraryBooks.Description(work);
            }

            _cache.WriteText(BookKind, key, new EnrichedBook
            {
                Schema = EnrichedBook.CurrentSchema,
                Title = title,
                Author = author,
                WorkId = id,
                Synopsis = synopsis,
            }.ToJson());
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>
    /// A title-only lookup is allowed only when the complete catalogue result points to one
    /// author and one work. It never overwrites a local writer tag or guesses from a popular hit.
    /// </summary>
    public async Task<bool> FetchUnknownBookAsync(string title, CancellationToken token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        if (CachedBook(title, null) is { Current: true })
        {
            return true;
        }

        try
        {
            if (await TextAsync(OpenLibraryBooks.UnknownAuthorUri(title), token).ConfigureAwait(false) is not { } answer)
            {
                return false;
            }

            var match = OpenLibraryBooks.MatchUnknownAuthor(title, answer);
            string? synopsis = null;
            if (match is { } work)
            {
                if (await TextAsync(OpenLibraryBooks.WorkUri(work.WorkId), token).ConfigureAwait(false) is not { } details)
                {
                    return false;
                }

                synopsis = OpenLibraryBooks.Description(details);
            }

            _cache.WriteText(BookKind, OpenLibraryBooks.BookKey(title, null), new EnrichedBook
            {
                Schema = EnrichedBook.CurrentSchema,
                Title = title,
                Author = match?.Author,
                WorkId = match?.WorkId,
                Synopsis = synopsis,
            }.ToJson());
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>The album's cached record, or null before its fetch.</summary>
    public EnrichedAlbum? CachedAlbum(string? artist, string album) =>
        _cache.ReadText(MusicKind, MusicBrainz.AlbumKey(artist, album)) is { } json ? EnrichedAlbum.FromJson(json) : null;

    /// <summary>Where an album's fetched cover lives, fetched or not.</summary>
    public string CoverPath(string? artist, string album) =>
        _cache.PathFor(CoverKind, MusicBrainz.AlbumKey(artist, album), ArtExtension);

    /// <summary>
    /// Fetches one album's facts and, only to fill a gap, its cover into the cache: with
    /// <paramref name="filesCarryArt"/> true no cover is fetched and the album is recorded as having
    /// its own art. The album already cached in this build's shape is left alone, and a cover
    /// fetched before is kept. False means the catalogue could not answer and the fetch should be
    /// tried again later; a clean "nothing matched" or a release with no cover is recorded and true.
    /// The labels only round the page out, so a release that cannot be asked about costs only them.
    /// </summary>
    public async Task<bool> FetchAlbumAsync(string? artist, string album, bool filesCarryArt, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(album);
        var key = MusicBrainz.AlbumKey(artist, album);
        var before = _cache.ReadText(MusicKind, key) is { } cached ? EnrichedAlbum.FromJson(cached) : null;
        if (before is { Current: true })
        {
            return true;
        }

        var record = new EnrichedAlbum { Schema = EnrichedAlbum.CurrentSchema, Album = album, Artist = artist, LocalArt = filesCarryArt };
        try
        {
            if (await TextAsync(MusicBrainz.SearchUri(artist, album), token).ConfigureAwait(false) is not { } answer)
            {
                return false;
            }

            if (MusicBrainz.Pick(artist, album, MusicBrainz.ParseSearch(answer)) is not { } pick)
            {
                _cache.WriteText(MusicKind, key, record.ToJson());
                return true;
            }

            var facts = MusicBrainzFacts.ParseReleaseGroup(answer, pick.Id);
            IReadOnlyList<string> labels = facts?.ReleaseId is { } release
                ? ParseOptional(await TextAsync(MusicBrainzFacts.ReleaseUri(release), token).ConfigureAwait(false), MusicBrainzFacts.ParseLabels)
                : [];
            var hasCover = before is { HasCover: true } && before.ReleaseGroupId == pick.Id && File.Exists(CoverPath(artist, album));
            if (!filesCarryArt && !hasCover && await _fetch(MusicBrainz.CoverUri(pick.Id), token).ConfigureAwait(false) is { } cover)
            {
                _cache.Write(CoverKind, key, ArtExtension, cover);
                hasCover = true;
            }

            _cache.WriteText(MusicKind, key, (record with
            {
                ReleaseGroupId = pick.Id,
                HasCover = !filesCarryArt && hasCover,
                Type = facts?.PrimaryType,
                SecondaryTypes = facts?.SecondaryTypes ?? [],
                FirstReleased = facts?.FirstReleased,
                Labels = labels,
                Genres = facts?.Tags ?? [],
            }).ToJson());
            return true;
        }
        catch (JsonException)
        {
            // The catalogue answered with something that is not its usual shape; try again later.
            return false;
        }
    }

    /// <summary>Every album already settled in the cache, for filling a lookup table at launch without the network.</summary>
    public IReadOnlyList<EnrichedAlbum> CachedAlbums() => CachedAll(MusicKind, EnrichedAlbum.FromJson);

    /// <summary>One raw fetch through the same delegate, for candidate covers a picker shows; null when it cannot answer.</summary>
    public Task<byte[]?> FetchBytesAsync(Uri uri, CancellationToken token) => _fetch(uri, token);

    /// <summary>The catalogue's candidates for an album, for the user to pick a cover from; empty when it cannot answer.</summary>
    public async Task<IReadOnlyList<AlbumCandidate>> SearchAlbumsAsync(string? artist, string album, CancellationToken token) =>
        ParseOptional(await TextAsync(MusicBrainz.SearchUri(artist, album), token).ConfigureAwait(false), MusicBrainz.ParseSearch);

    /// <summary>Forgets the album's record and cover, for a refresh.</summary>
    public void ForgetAlbum(string? artist, string album)
    {
        var key = MusicBrainz.AlbumKey(artist, album);
        _cache.Remove(MusicKind, key, ".json");
        _cache.Remove(CoverKind, key, ArtExtension);
    }

    /// <summary>The key an artist's record and photo live under: their name, normalized.</summary>
    public static string ArtistKey(string name) => MetadataMatch.Normalize(name ?? throw new ArgumentNullException(nameof(name)));

    /// <summary>The artist's cached record, or null before their fetch.</summary>
    public EnrichedArtist? CachedArtist(string name) =>
        _cache.ReadText(ArtistKind, ArtistKey(name)) is { } json ? EnrichedArtist.FromJson(json) : null;

    /// <summary>Where an artist's fetched photo lives, fetched or not.</summary>
    public string ArtistPhotoPath(string name) => _cache.PathFor(ArtistPhotoKind, ArtistKey(name), ArtExtension);

    /// <summary>Every artist already settled in the cache, for filling a lookup table at launch without the network.</summary>
    public IReadOnlyList<EnrichedArtist> CachedArtists() => CachedAll(ArtistKind, EnrichedArtist.FromJson);

    /// <summary>Forgets the artist's record and photo, for a refresh.</summary>
    public void ForgetArtist(string name)
    {
        _cache.Remove(ArtistKind, ArtistKey(name), ".json");
        _cache.Remove(ArtistPhotoKind, ArtistKey(name), ArtExtension);
    }

    /// <summary>
    /// Fetches one artist into the cache: MusicBrainz's best exact match and its facts, then the
    /// Wikidata item it links to for the photo (fetched from Wikimedia Commons) and the Wikipedia
    /// summary. The artist already cached in this build's shape is left alone. False means
    /// MusicBrainz could not answer and the fetch should be tried again later; a clean "nothing
    /// matched" is recorded and true. Wikidata, Commons and Wikipedia only round the page out: when
    /// they cannot answer, the artist is recorded with what MusicBrainz said.
    /// </summary>
    public async Task<bool> FetchArtistAsync(string name, CancellationToken token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var key = ArtistKey(name);
        if (_cache.ReadText(ArtistKind, key) is { } cached && EnrichedArtist.FromJson(cached) is { Current: true })
        {
            return true;
        }

        try
        {
            if (await TextAsync(MusicBrainzFacts.ArtistSearchUri(name), token).ConfigureAwait(false) is not { } answer)
            {
                return false;
            }

            if (MusicBrainzFacts.PickArtist(name, MusicBrainzFacts.ParseArtistSearch(answer)) is not { } pick)
            {
                _cache.WriteText(ArtistKind, key, ArtistEnrichment.Build(name, null, null, null, false).ToJson());
                return true;
            }

            if (await TextAsync(MusicBrainzFacts.ArtistUri(pick.Id), token).ConfigureAwait(false) is not { } looked
                || MusicBrainzFacts.ParseArtist(looked) is not { } facts)
            {
                return false;
            }

            var item = facts.WikidataId is { } id
                ? ParseOptional(await TextAsync(Wikidata.EntitiesUri([id]), token).ConfigureAwait(false), Wikidata.ParseEntities).FirstOrDefault()
                : null;
            var summary = await SummaryAsync(item?.EnwikiTitle, token).ConfigureAwait(false);
            var photo = ArtistEnrichment.PhotoUri(item) is { } uri ? await _fetch(uri, token).ConfigureAwait(false) : null;
            if (photo is not null)
            {
                _cache.Write(ArtistPhotoKind, key, ArtExtension, photo);
            }

            _cache.WriteText(ArtistKind, key, ArtistEnrichment.Build(name, facts, item, summary, photo is not null).ToJson());
            return true;
        }
        catch (JsonException)
        {
            // MusicBrainz answered with something that is not its usual shape; try again later.
            return false;
        }
    }

    private List<T> CachedAll<T>(string kind, Func<string, T?> read)
        where T : class
    {
        var folder = Path.Combine(_cache.Downloads, kind);
        return Directory.Exists(folder)
            ? [.. Directory.EnumerateFiles(folder, "*.json").Select(file => read(File.ReadAllText(file))).OfType<T>()]
            : [];
    }
}
