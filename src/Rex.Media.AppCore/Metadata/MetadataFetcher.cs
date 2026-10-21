using System.Text;
using System.Text.Json;

namespace Rex.Media.AppCore.Metadata;

/// <summary>
/// Brings one show's, album's or movie's metadata into the cache: search (or the user's own pick),
/// match, details and artwork, each kept on disk so nothing is asked twice and everything works offline afterwards. A
/// lookup that finds nothing is recorded too, so an unknown name is not searched again and again.
/// The network itself is behind <paramref name="fetch"/> (null for an answer that is no use), so
/// the window gives it a real HTTP client and tests give it canned answers.
/// </summary>
public sealed partial class MetadataFetcher(MetadataCache cache, Func<Uri, CancellationToken, Task<byte[]?>> fetch)
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
                    // A show TVmaze does not know may still be on Wikidata and Wikipedia.
                    return await FetchShowFromWikidataAsync(show, year, token).ConfigureAwait(false);
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
            var poster = Uri.TryCreate(pick.ImageUrl, UriKind.Absolute, out var art) && art.Scheme is "https" or "http"
                ? await _fetch(art, token).ConfigureAwait(false) : null;
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

    private async Task<string?> TextAsync(Uri uri, CancellationToken token) =>
        await _fetch(uri, token).ConfigureAwait(false) is { } bytes ? Encoding.UTF8.GetString(bytes) : null;
}
