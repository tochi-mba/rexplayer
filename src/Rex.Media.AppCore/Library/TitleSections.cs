using Rex.Media.AppCore.Metadata;

namespace Rex.Media.AppCore.Library;

/// <summary>Where a synopsis came from, which decides the credit shown with it.</summary>
public enum DescriptionSource
{
    /// <summary>English Wikipedia, CC BY-SA: credited "Description from Wikipedia" with a link to the article.</summary>
    Wikipedia,

    /// <summary>TVmaze: the show page carries a "Data from TVmaze" line.</summary>
    TvMaze,
}

/// <summary>A synopsis as a screen shows it: its words, where they came from and the page its credit links to.</summary>
public sealed record Description(string Text, DescriptionSource Source, Uri Link);

/// <summary>
/// What a movie's details page shows. Every part is null, empty or false when there is nothing
/// to put in it, and the page then leaves it out rather than showing it empty.
/// </summary>
public sealed record MovieSections(
    Description? Synopsis,
    string Crew,
    IReadOnlyList<CastMember> Cast,
    Uri? Trailer,
    Uri? Backdrop,
    bool MoreLikeThis,
    string? SeriesHeading);

/// <summary>
/// The details screens' rules for what is shown and what is hidden: a synopsis only with the
/// credit its licence asks for, a cast row only with people in it, a trailer, a backdrop, "More
/// like this" and a series only when there is one, and who appears in what, for a cast member's
/// own view. Pure decisions over the cached records; the window only draws them.
/// </summary>
public static class TitleSections
{
    /// <summary>The most people a cast row shows.</summary>
    public const int CastLimit = 20;

    /// <summary>The words of the TVmaze credit line on a show's page.</summary>
    public const string TvMazeCredit = "Data from TVmaze";

    /// <summary>
    /// A movie's page: <paramref name="alike"/> is how many library titles "More like this" found,
    /// <paramref name="seriesMates"/> how many other films of its series the library holds.
    /// </summary>
    public static MovieSections Movie(EnrichedMovie? facts, int alike, int seriesMates) => new(
        MovieSynopsis(facts),
        TitleFacts.Crew(facts),
        CastRow(facts?.Cast ?? []),
        Trailer(facts),
        AbsoluteUri(facts?.BackdropUrl),
        alike > 0,
        seriesMates > 0 && facts?.Collection is { } series && !string.IsNullOrWhiteSpace(series) ? SeriesHeading(series) : null);

    /// <summary>An artist's biography, shown only when the Wikipedia source can be credited and opened.</summary>
    public static Description? ArtistBiography(EnrichedArtist? facts) =>
        facts?.Bio is { } bio && !string.IsNullOrWhiteSpace(bio) && AbsoluteUri(facts.ArticleUrl) is { } link
            ? new Description(bio.Trim(), DescriptionSource.Wikipedia, link)
            : null;

    /// <summary>Where they come from, when they worked, and their genres, without blank separators.</summary>
    public static string ArtistFacts(EnrichedArtist? facts)
    {
        if (facts is null)
        {
            return "";
        }

        var years = facts.ActiveFrom is { } first
            ? facts.ActiveTo is { } last ? first.ToString(System.Globalization.CultureInfo.CurrentCulture) + "–" + last.ToString(System.Globalization.CultureInfo.CurrentCulture)
                : "Active since " + first.ToString(System.Globalization.CultureInfo.CurrentCulture)
            : null;
        return string.Join(" · ", new[] { facts.Origin, years, string.Join(", ", facts.Genres.Take(3)) }.Where(part => !string.IsNullOrWhiteSpace(part)));
    }

    /// <summary>A movie's synopsis, which is Wikipedia's and so shown only when its article can be credited and linked.</summary>
    public static Description? MovieSynopsis(EnrichedMovie? facts) =>
        facts?.Overview is { } text && !string.IsNullOrWhiteSpace(text) && AbsoluteUri(facts.ArticleUrl) is { } article
            ? new Description(text.Trim(), DescriptionSource.Wikipedia, article)
            : null;

    /// <summary>
    /// A show's synopsis: Wikipedia's (with its article) for a show TVmaze did not know, TVmaze's
    /// for the rest; null when there is none, or when it cannot be credited.
    /// </summary>
    public static Description? ShowSynopsis(EnrichedShow? show)
    {
        if (show is not { Found: true, Summary: { } text } || string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        if (AbsoluteUri(show.ArticleUrl) is { } article)
        {
            return new Description(text.Trim(), DescriptionSource.Wikipedia, article);
        }

        return show.WikidataId is null ? new Description(text.Trim(), DescriptionSource.TvMaze, TvMazePage(show.CatalogueId)) : null;
    }

    /// <summary>Whether a show's page carries the "Data from TVmaze" line: anything shown of it came from TVmaze.</summary>
    public static bool FromTvMaze(EnrichedShow? show) => show is { Found: true, WikidataId: null };

    /// <summary>The show's own page on TVmaze, which its credit line links to.</summary>
    public static Uri TvMazePage(int catalogueId) => new("https://www.tvmaze.com/shows/" + catalogueId.ToString(System.Globalization.CultureInfo.InvariantCulture));

    /// <summary>One season's overview, shown when the season picker changes; null when the catalogue has none.</summary>
    public static string? SeasonOverview(EnrichedShow? show, int? season) =>
        show is { Found: true } && season is { } number
            && show.Seasons.FirstOrDefault(details => details.Number == number)?.Summary is { } text && !string.IsNullOrWhiteSpace(text)
            ? text.Trim() : null;

    /// <summary>One episode's synopsis for its line; null when the catalogue has none.</summary>
    public static string? EpisodeSynopsis(EnrichedShow? show, VideoEpisode? episode) =>
        show is { Found: true } && episode is not null && show.EpisodeSummary(episode.Season, episode.Episode) is { } text && !string.IsNullOrWhiteSpace(text)
            ? text.Trim() : null;

    /// <summary>The people a cast row shows, in billing order: each once, nameless entries left out, at most <see cref="CastLimit"/>.</summary>
    public static IReadOnlyList<CastMember> CastRow(IReadOnlyList<CastMember> cast)
    {
        ArgumentNullException.ThrowIfNull(cast);
        return [.. cast.Where(member => !string.IsNullOrWhiteSpace(member.Name))
            .DistinctBy(member => member.Name.Trim(), StringComparer.OrdinalIgnoreCase)
            .Take(CastLimit)];
    }

    /// <summary>The page of a movie's first trailer, opened in the browser; null when it has none.</summary>
    public static Uri? Trailer(EnrichedMovie? facts) =>
        facts?.TrailerKeys.FirstOrDefault(key => !string.IsNullOrWhiteSpace(key)) is { } key
            ? new Uri("https://www.youtube.com/watch?v=" + Uri.EscapeDataString(key.Trim()))
            : null;

    /// <summary>"Part of The Matrix film series", "Part of the Toy Story series": the heading over a film's series.</summary>
    public static string SeriesHeading(string series)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(series);
        var name = series.Trim();
        if (name.EndsWith("series", StringComparison.OrdinalIgnoreCase))
        {
            return "Part of " + name;
        }

        return name.StartsWith("the ", StringComparison.OrdinalIgnoreCase) ? $"Part of {name} series" : $"Part of the {name} series";
    }

    /// <summary>
    /// The other films of a movie's series the library holds, oldest first, each once; none for a
    /// movie that is part of no series.
    /// </summary>
    public static IReadOnlyList<T> SeriesMates<T>(EnrichedMovie movie, IReadOnlyList<(T Item, EnrichedMovie Facts)> library)
    {
        ArgumentNullException.ThrowIfNull(movie);
        ArgumentNullException.ThrowIfNull(library);
        if (string.IsNullOrWhiteSpace(movie.Collection))
        {
            return [];
        }

        return [.. library
            .Where(other => other.Facts.Found && other.Facts.CatalogueId != movie.CatalogueId
                && string.Equals(other.Facts.Collection?.Trim(), movie.Collection.Trim(), StringComparison.OrdinalIgnoreCase))
            .DistinctBy(other => other.Facts.CatalogueId)
            .OrderBy(other => other.Facts.Year ?? int.MaxValue)
            .Select(other => other.Item)];
    }

    /// <summary>The titles <paramref name="person"/> appears in, in the order given: their cast names them.</summary>
    public static IReadOnlyList<T> AppearsIn<T>(string person, IEnumerable<(T Item, IReadOnlyList<CastMember> Cast)> titles)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(person);
        ArgumentNullException.ThrowIfNull(titles);
        var name = person.Trim();
        return [.. titles.Where(title => title.Cast.Any(member => string.Equals(member.Name.Trim(), name, StringComparison.OrdinalIgnoreCase)))
            .Select(title => title.Item)];
    }

    /// <summary>The address in a record as a web page's, or null for one that is blank or not absolute http(s).</summary>
    public static Uri? AbsoluteUri(string? address) =>
        Uri.TryCreate(address?.Trim(), UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp) ? uri : null;
}
