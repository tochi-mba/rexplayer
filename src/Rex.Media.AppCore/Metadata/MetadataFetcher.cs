using System.Text;
using System.Text.Json;

namespace Rex.Media.AppCore.Metadata;

/// <summary>
/// Brings one show's metadata into the cache: search (or the user's own pick), match, episodes and
/// poster, each kept on disk so nothing is asked twice and everything works offline afterwards. A
/// lookup that finds nothing is recorded too, so an unknown name is not searched again and again.
/// The network itself is behind <paramref name="fetch"/> (null for an answer that is no use), so
/// the window gives it a real HTTP client and tests give it canned answers.
/// </summary>
public sealed class MetadataFetcher(MetadataCache cache, Func<Uri, CancellationToken, Task<byte[]?>> fetch)
{
    /// <summary>The cache kind a show's record lives under.</summary>
    public const string ShowKind = "tv";

    /// <summary>The cache kind and extension a show's poster lives under.</summary>
    public const string ArtKind = "art";

    public const string ArtExtension = ".img";

    private readonly MetadataCache _cache = cache ?? throw new ArgumentNullException(nameof(cache));
    private readonly Func<Uri, CancellationToken, Task<byte[]?>> _fetch = fetch ?? throw new ArgumentNullException(nameof(fetch));

    /// <summary>The show's cached record, or null before its fetch (or after a forget).</summary>
    public EnrichedShow? Cached(string show) =>
        _cache.ReadText(ShowKind, MetadataMatch.Normalize(show)) is { } json ? EnrichedShow.FromJson(json) : null;

    /// <summary>Every show already in the cache, for filling a lookup table at launch without the network.</summary>
    public IReadOnlyList<EnrichedShow> CachedShows()
    {
        var folder = Path.Combine(_cache.Downloads, ShowKind);
        if (!Directory.Exists(folder))
        {
            return [];
        }

        return [.. Directory.EnumerateFiles(folder, "*.json")
            .Select(file => EnrichedShow.FromJson(File.ReadAllText(file)))
            .Where(show => show is not null)!];
    }

    /// <summary>Forgets the show's record and poster, for a refresh or a corrected match.</summary>
    public void Forget(string show)
    {
        _cache.Remove(ShowKind, MetadataMatch.Normalize(show), ".json");
        _cache.Remove(ArtKind, MetadataMatch.Normalize(show), ArtExtension);
    }

    /// <summary>The catalogue's candidates for a name, for the user to pick from; empty when it cannot answer.</summary>
    public async Task<IReadOnlyList<ShowCandidate>> SearchShowsAsync(string name, CancellationToken token)
    {
        var answer = await TextAsync(TvMaze.SearchUri(name), token).ConfigureAwait(false);
        try
        {
            return answer is null ? [] : TvMaze.ParseSearch(answer);
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary>
    /// Fetches one show into the cache: the user's pick when there is one, the best search match
    /// otherwise, then its episodes and poster. The show already cached is left alone. False means
    /// the catalogue could not answer and the fetch should be tried again later; a clean "nothing
    /// matched" is true, recorded, and not asked again.
    /// </summary>
    public async Task<bool> FetchShowAsync(string show, int? year, int? chosenId, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(show);
        var key = MetadataMatch.Normalize(show);
        if (_cache.ReadText(ShowKind, key) is { } cached && EnrichedShow.FromJson(cached) is { Current: true })
        {
            return true;
        }

        try
        {
            ShowCandidate? pick;
            if (chosenId is { } id)
            {
                pick = await TextAsync(TvMaze.ShowUri(id), token).ConfigureAwait(false) is { } chosen ? TvMaze.ParseShow(chosen) : null;
                if (pick is null)
                {
                    return false;
                }
            }
            else
            {
                if (await TextAsync(TvMaze.SearchUri(show), token).ConfigureAwait(false) is not { } answer)
                {
                    return false;
                }

                pick = MetadataMatch.Pick(show, year, TvMaze.ParseSearch(answer));
                if (pick is null)
                {
                    _cache.WriteText(ShowKind, key, new EnrichedShow { CatalogueId = 0, Name = show }.ToJson());
                    return true;
                }
            }

            if (await TextAsync(TvMaze.EpisodesUri(pick.Id), token).ConfigureAwait(false) is not { } listed)
            {
                return false;
            }

            var episodes = TvMaze.ParseEpisodes(listed);
            // The cast, creators and seasons round a page out but never fail the show.
            var cast = ParseOptional(await TextAsync(TvMaze.CastUri(pick.Id), token).ConfigureAwait(false), TvMaze.ParseCast);
            var creators = ParseOptional(await TextAsync(TvMaze.CrewUri(pick.Id), token).ConfigureAwait(false), TvMaze.ParseCreators);
            var seasons = ParseOptional(await TextAsync(TvMaze.SeasonsUri(pick.Id), token).ConfigureAwait(false), TvMaze.ParseSeasons);
            var poster = pick.ImageUrl is null ? null : await _fetch(new Uri(pick.ImageUrl), token).ConfigureAwait(false);
            if (poster is not null)
            {
                _cache.Write(ArtKind, key, ArtExtension, poster);
            }

            _cache.WriteText(ShowKind, key, new EnrichedShow
            {
                Schema = EnrichedShow.CurrentSchema,
                CatalogueId = pick.Id,
                Name = pick.Name,
                Summary = pick.Summary,
                Genres = pick.Genres,
                Rating = pick.Rating,
                HasPoster = poster is not null,
                Status = pick.Status,
                Network = pick.Network,
                RuntimeMinutes = pick.RuntimeMinutes,
                Language = pick.Language,
                Premiered = pick.Premiered,
                Ended = pick.Ended,
                Cast = cast,
                Creators = creators,
                Seasons = seasons,
                Episodes = episodes,
            }.ToJson());
            return true;
        }
        catch (JsonException)
        {
            // The catalogue answered with something that is not its usual shape; try again later.
            return false;
        }
    }

    private static IReadOnlyList<T> ParseOptional<T>(string? json, Func<string, IReadOnlyList<T>> parse)
    {
        if (json is null)
        {
            return [];
        }

        try
        {
            return parse(json);
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary>The cache kind an album's record lives under.</summary>
    public const string MusicKind = "music";

    /// <summary>The cache kind an album's fetched cover lives under, with <see cref="ArtExtension"/>.</summary>
    public const string CoverKind = "albumart";

    /// <summary>The album's cached record, or null before its fetch.</summary>
    public EnrichedAlbum? CachedAlbum(string? artist, string album) =>
        _cache.ReadText(MusicKind, MusicBrainz.AlbumKey(artist, album)) is { } json ? EnrichedAlbum.FromJson(json) : null;

    /// <summary>Where an album's fetched cover lives, fetched or not.</summary>
    public string CoverPath(string? artist, string album) =>
        _cache.PathFor(CoverKind, MusicBrainz.AlbumKey(artist, album), ArtExtension);

    /// <summary>
    /// Fetches one album's cover into the cache, only to fill a gap: with <paramref name="filesCarryArt"/>
    /// true nothing is looked up and the album is recorded as having its own art. The album already
    /// cached is left alone. False means the catalogue could not answer and the fetch should be tried
    /// again later; a clean "nothing matched" or a release with no cover is recorded and true.
    /// </summary>
    public async Task<bool> FetchAlbumAsync(string? artist, string album, bool filesCarryArt, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(album);
        var key = MusicBrainz.AlbumKey(artist, album);
        if (_cache.ReadText(MusicKind, key) is not null)
        {
            return true;
        }

        var record = new EnrichedAlbum { Album = album, Artist = artist };
        if (filesCarryArt)
        {
            _cache.WriteText(MusicKind, key, (record with { LocalArt = true }).ToJson());
            return true;
        }

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

            var cover = await _fetch(MusicBrainz.CoverUri(pick.Id), token).ConfigureAwait(false);
            if (cover is not null)
            {
                _cache.Write(CoverKind, key, ArtExtension, cover);
            }

            _cache.WriteText(MusicKind, key, (record with { ReleaseGroupId = pick.Id, HasCover = cover is not null }).ToJson());
            return true;
        }
        catch (JsonException)
        {
            // The catalogue answered with something that is not its usual shape; try again later.
            return false;
        }
    }

    /// <summary>Every album already settled in the cache, for filling a lookup table at launch without the network.</summary>
    public IReadOnlyList<EnrichedAlbum> CachedAlbums()
    {
        var folder = Path.Combine(_cache.Downloads, MusicKind);
        if (!Directory.Exists(folder))
        {
            return [];
        }

        return [.. Directory.EnumerateFiles(folder, "*.json")
            .Select(file => EnrichedAlbum.FromJson(File.ReadAllText(file)))
            .Where(album => album is not null)!];
    }

    /// <summary>One raw fetch through the same delegate, for candidate covers a picker shows; null when it cannot answer.</summary>
    public Task<byte[]?> FetchBytesAsync(Uri uri, CancellationToken token) => _fetch(uri, token);

    /// <summary>The catalogue's candidates for an album, for the user to pick a cover from; empty when it cannot answer.</summary>
    public async Task<IReadOnlyList<AlbumCandidate>> SearchAlbumsAsync(string? artist, string album, CancellationToken token)
    {
        var answer = await TextAsync(MusicBrainz.SearchUri(artist, album), token).ConfigureAwait(false);
        try
        {
            return answer is null ? [] : MusicBrainz.ParseSearch(answer);
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary>Forgets the album's record and cover, for a refresh.</summary>
    public void ForgetAlbum(string? artist, string album)
    {
        var key = MusicBrainz.AlbumKey(artist, album);
        _cache.Remove(MusicKind, key, ".json");
        _cache.Remove(CoverKind, key, ArtExtension);
    }

    /// <summary>The cache kind a movie's record lives under.</summary>
    public const string MovieKind = "movie";

    /// <summary>The cache kind a movie's fetched poster lives under, with <see cref="ArtExtension"/>.</summary>
    public const string MovieArtKind = "movieart";

    /// <summary>The movie's cached record, or null before its fetch.</summary>
    public EnrichedMovie? CachedMovie(string title, int? year) =>
        _cache.ReadText(MovieKind, Tmdb.MovieKey(title, year)) is { } json ? EnrichedMovie.FromJson(json) : null;

    /// <summary>Where a movie's fetched poster lives, fetched or not.</summary>
    public string MoviePosterPath(string title, int? year) =>
        _cache.PathFor(MovieArtKind, Tmdb.MovieKey(title, year), ArtExtension);

    /// <summary>Every movie already in the cache, for filling a lookup table at launch without the network.</summary>
    public IReadOnlyList<EnrichedMovie> CachedMovies()
    {
        var folder = Path.Combine(_cache.Downloads, MovieKind);
        if (!Directory.Exists(folder))
        {
            return [];
        }

        return [.. Directory.EnumerateFiles(folder, "*.json")
            .Select(file => EnrichedMovie.FromJson(File.ReadAllText(file)))
            .Where(movie => movie is not null)!];
    }

    /// <summary>Forgets the movie's record and poster, for a refresh.</summary>
    public void ForgetMovie(string title, int? year)
    {
        _cache.Remove(MovieKind, Tmdb.MovieKey(title, year), ".json");
        _cache.Remove(MovieArtKind, Tmdb.MovieKey(title, year), ArtExtension);
    }

    /// <summary>
    /// Fetches one movie into the cache with the user's own TMDB key: the best search match, its
    /// poster, and genre names from a map fetched once. The movie already cached is left alone.
    /// False means the catalogue could not answer and the fetch should be tried again later; a
    /// clean "nothing matched" is recorded and true.
    /// </summary>
    public async Task<bool> FetchMovieAsync(string apiKey, string title, int? year, CancellationToken token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        ArgumentNullException.ThrowIfNull(title);
        var key = Tmdb.MovieKey(title, year);
        if (_cache.ReadText(MovieKind, key) is { } cached && EnrichedMovie.FromJson(cached) is { Current: true })
        {
            return true;
        }

        try
        {
            if (await TextAsync(Tmdb.SearchUri(apiKey, title, year), token).ConfigureAwait(false) is not { } answer)
            {
                return false;
            }

            if (Tmdb.Pick(title, year, Tmdb.ParseSearch(answer)) is not { } pick)
            {
                _cache.WriteText(MovieKind, key, new EnrichedMovie { CatalogueId = 0, Title = title, Year = year }.ToJson());
                return true;
            }

            // One details answer carries the facts, credits, trailers and suggestions together.
            if (await TextAsync(Tmdb.DetailsUri(apiKey, pick.Id), token).ConfigureAwait(false) is not { } full
                || Tmdb.ParseDetails(full) is not { } details)
            {
                return false;
            }

            var poster = pick.PosterPath is null ? null : await _fetch(Tmdb.PosterUri(pick.PosterPath), token).ConfigureAwait(false);
            if (poster is not null)
            {
                _cache.Write(MovieArtKind, key, ArtExtension, poster);
            }

            _cache.WriteText(MovieKind, key, new EnrichedMovie
            {
                Schema = EnrichedMovie.CurrentSchema,
                CatalogueId = pick.Id,
                Title = pick.Title,
                Year = pick.Year ?? year,
                Overview = pick.Overview,
                Genres = details.Genres,
                Rating = pick.Rating,
                HasPoster = poster is not null,
                Tagline = details.Tagline,
                RuntimeMinutes = details.RuntimeMinutes,
                Status = details.Status,
                Studio = details.Studio,
                OriginalLanguage = details.OriginalLanguage,
                ContentRating = details.ContentRating,
                BackdropUrl = details.BackdropUrl,
                Cast = details.Cast,
                Directors = details.Directors,
                Writers = details.Writers,
                TrailerKeys = details.TrailerKeys,
                Recommendations = details.Recommendations,
                Collection = details.Collection,
            }.ToJson());
            return true;
        }
        catch (JsonException)
        {
            // The catalogue answered with something that is not its usual shape; try again later.
            return false;
        }
    }

    private async Task<string?> TextAsync(Uri uri, CancellationToken token) =>
        await _fetch(uri, token).ConfigureAwait(false) is { } bytes ? Encoding.UTF8.GetString(bytes) : null;
}
