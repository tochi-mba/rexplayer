using System.Text.Json;

namespace Rex.Media.AppCore.Metadata;

/// <summary>
/// Movies, and shows TVmaze does not know, from Wikidata and English Wikipedia, neither of which
/// needs a key or a sign-up: a search, the likely items fetched whole and judged by their dates,
/// one query for the names and photos they point at, then the article's summary and poster.
/// </summary>
public sealed partial class MetadataFetcher
{
    /// <summary>The cache kind a movie's record lives under.</summary>
    public const string MovieKind = "movie";

    /// <summary>The cache kind a movie's fetched poster lives under, with <see cref="ArtExtension"/>.</summary>
    public const string MovieArtKind = "movieart";

    /// <summary>The movie's cached record, or null before its fetch.</summary>
    public EnrichedMovie? CachedMovie(string title, int? year) =>
        _cache.ReadText(MovieKind, MetadataMatch.MovieKey(title, year)) is { } json ? EnrichedMovie.FromJson(json) : null;

    /// <summary>Where a movie's fetched poster lives, fetched or not.</summary>
    public string MoviePosterPath(string title, int? year) =>
        _cache.PathFor(MovieArtKind, MetadataMatch.MovieKey(title, year), ArtExtension);

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
        _cache.Remove(MovieKind, MetadataMatch.MovieKey(title, year), ".json");
        _cache.Remove(MovieArtKind, MetadataMatch.MovieKey(title, year), ArtExtension);
    }

    /// <summary>
    /// Fetches one movie into the cache: the best Wikidata match, the names its facts point at, its
    /// Wikipedia summary and poster. The movie already cached in this build's shape is left alone.
    /// False means a service could not answer and the fetch should be tried again later; a clean
    /// "nothing matched" is recorded and true.
    /// </summary>
    public async Task<bool> FetchMovieAsync(string title, int? year, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(title);
        var key = MetadataMatch.MovieKey(title, year);
        if (_cache.ReadText(MovieKind, key) is { } cached && EnrichedMovie.FromJson(cached) is { Current: true })
        {
            return true;
        }

        try
        {
            if (await FindAsync(title, year, WikidataWork.Film, token).ConfigureAwait(false) is not { } found)
            {
                return false;
            }

            if (found.Entity is not { } item)
            {
                _cache.WriteText(MovieKind, key, new EnrichedMovie { Schema = EnrichedMovie.CurrentSchema, CatalogueId = 0, Title = title, Year = year }.ToJson());
                return true;
            }

            if (await LabelsAsync(WikidataFacts.Referenced(item), token).ConfigureAwait(false) is not { } labels)
            {
                return false;
            }

            var summary = await SummaryAsync(item.EnwikiTitle, token).ConfigureAwait(false);
            var poster = await PosterAsync(summary, token).ConfigureAwait(false);
            if (poster is not null)
            {
                _cache.Write(MovieArtKind, key, ArtExtension, poster);
            }

            _cache.WriteText(MovieKind, key, (WikidataFacts.Movie(item, labels, summary, year) with { HasPoster = poster is not null }).ToJson());
            return true;
        }
        catch (JsonException)
        {
            // A service answered with something that is not its usual shape; try again later.
            return false;
        }
    }

    /// <summary>
    /// A show TVmaze has no match for, from Wikidata and Wikipedia: its facts, cast, creators and
    /// poster, with no episodes to list. A clean "nothing matched" here too is recorded and true.
    /// </summary>
    private async Task<bool> FetchShowFromWikidataAsync(string show, int? year, CancellationToken token)
    {
        var key = MetadataMatch.Normalize(show);
        if (await FindAsync(show, year, WikidataWork.Series, token).ConfigureAwait(false) is not { } found)
        {
            return false;
        }

        if (found.Entity is not { } item)
        {
            _cache.WriteText(ShowKind, key, new EnrichedShow { CatalogueId = 0, Name = show }.ToJson());
            return true;
        }

        if (await LabelsAsync(WikidataFacts.Referenced(item), token).ConfigureAwait(false) is not { } labels)
        {
            return false;
        }

        var summary = await SummaryAsync(item.EnwikiTitle, token).ConfigureAwait(false);
        var pick = WikidataFacts.Show(item, labels, summary);
        var poster = await PosterAsync(summary, token).ConfigureAwait(false);
        if (poster is not null)
        {
            _cache.Write(ArtKind, key, ArtExtension, poster);
        }

        _cache.WriteText(ShowKind, key, new EnrichedShow
        {
            Schema = EnrichedShow.CurrentSchema,
            CatalogueId = pick.Id,
            WikidataId = item.Id,
            Name = pick.Name,
            Summary = pick.Summary,
            ArticleUrl = pick.Summary is null ? null : summary?.PageUrl,
            Genres = pick.Genres,
            HasPoster = poster is not null,
            Network = pick.Network,
            RuntimeMinutes = pick.RuntimeMinutes,
            Language = pick.Language,
            Premiered = pick.Premiered,
            Ended = pick.Ended,
            Cast = WikidataFacts.Cast(item, labels),
            Creators = WikidataFacts.Creators(item, labels),
        }.ToJson());
        return true;
    }

    /// <summary>What a search found: the item taken, or null inside when nothing could be taken with confidence.</summary>
    private sealed record WikidataFind(WikidataEntity? Entity);

    /// <summary>The item a title names, judged by kind and year; null when Wikidata could not answer.</summary>
    private async Task<WikidataFind?> FindAsync(string title, int? year, WikidataWork work, CancellationToken token)
    {
        if (await TextAsync(Wikidata.SearchUri(title), token).ConfigureAwait(false) is not { } answer)
        {
            return null;
        }

        var candidates = WikidataFacts.Candidates(title, work, Wikidata.ParseSearch(answer));
        if (candidates.Count == 0)
        {
            return new WikidataFind(null);
        }

        return await TextAsync(Wikidata.EntitiesUri([.. candidates.Select(hit => hit.Id)]), token).ConfigureAwait(false) is { } items
            ? new WikidataFind(WikidataFacts.Pick(year, work, candidates, Wikidata.ParseEntities(items)))
            : null;
    }

    /// <summary>The English names and pictures of the items a work points at, in as few queries as the cap allows; null when the query service could not answer.</summary>
    private async Task<IReadOnlyDictionary<string, WikidataLabel>?> LabelsAsync(IReadOnlyList<string> ids, CancellationToken token)
    {
        var labels = new Dictionary<string, WikidataLabel>(StringComparer.Ordinal);
        foreach (var batch in ids.Chunk(Wikidata.BatchLimit))
        {
            if (await TextAsync(Wikidata.LabelsUri(batch), token).ConfigureAwait(false) is not { } answer)
            {
                return null;
            }

            foreach (var (id, label) in Wikidata.ParseLabels(answer))
            {
                labels[id] = label;
            }
        }

        return labels;
    }

    /// <summary>The article's summary, which only rounds a card out: none when there is no article or no usable answer.</summary>
    private async Task<ArticleSummary?> SummaryAsync(string? title, CancellationToken token)
    {
        if (title is null || await TextAsync(Wikipedia.SummaryUri(title), token).ConfigureAwait(false) is not { } answer)
        {
            return null;
        }

        try
        {
            return Wikipedia.ParseSummary(answer);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private async Task<byte[]?> PosterAsync(ArticleSummary? summary, CancellationToken token) =>
        Wikipedia.ChooseArt(summary, null).PosterUrl is { } url ? await _fetch(new Uri(url), token).ConfigureAwait(false) : null;
}
