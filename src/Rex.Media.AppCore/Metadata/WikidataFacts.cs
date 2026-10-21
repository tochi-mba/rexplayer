using System.Globalization;

namespace Rex.Media.AppCore.Metadata;

/// <summary>Which kind of work an item must be to match: a film, or a television series.</summary>
public enum WikidataWork
{
    Film,
    Series,
}

/// <summary>
/// Reading Wikidata items as films and shows: which search hits can be the work a file names,
/// which item to take, and what each property means for a card. The properties used are genre
/// (P136), director (P57), screenwriter (P58), cast member (P161, with character role P453 or
/// name of the character role P4633, in the order P1545 gives), publication date (P577), start
/// and end time (P580, P582), duration (P2047), production company (P272), original broadcaster
/// (P449), creator (P170), original language (P364), country of origin (P495), MPA film rating
/// (P1657), part of the series (P179), YouTube video ID (P1651), IMDb ID (P345) and image (P18).
/// </summary>
public static class WikidataFacts
{
    /// <summary>How many cast members a card keeps, in billing order.</summary>
    public const int CastLimit = 12;

    /// <summary>How many writers a card keeps.</summary>
    public const int WriterLimit = 5;

    /// <summary>How many genres a card keeps.</summary>
    public const int GenreLimit = 6;

    /// <summary>How many trailers a card keeps.</summary>
    public const int TrailerLimit = 3;

    /// <summary>How many search hits are fetched whole to be judged by their dates.</summary>
    public const int CandidateLimit = 5;

    private static readonly string[] FilmWords = [" film ", " movie "];

    /// <summary>Nouns that, straight after the first "film", make the item many films rather than one.</summary>
    private static readonly string[] Collections = ["series ", "franchise ", "trilogy "];

    private static readonly string[] SeriesWords =
        ["television series", "tv series", "web series", "miniseries", "television program", "television show", "sitcom", "anime series"];

    private const string Minute = "Q7727";
    private const string Hour = "Q25235";
    private const string Second = "Q11574";

    /// <summary>
    /// The hits that can be the work: the same title after normalizing, described as the right
    /// kind of work. At most
    /// <see cref="CandidateLimit"/>, in the search's order.
    /// </summary>
    public static IReadOnlyList<WikidataHit> Candidates(string title, WikidataWork work, IReadOnlyList<WikidataHit> hits)
    {
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(hits);
        var wanted = MetadataMatch.Normalize(title);
        if (wanted.Length == 0)
        {
            return [];
        }

        return [.. hits.Where(hit => MetadataMatch.Normalize(hit.Label) == wanted && Describes(hit.Description, work)).Take(CandidateLimit)];
    }

    /// <summary>Whether a search hit's description says it is the kind of work wanted ("1999 American science fiction film").</summary>
    public static bool Describes(string? description, WikidataWork work)
    {
        if (string.IsNullOrEmpty(description))
        {
            return false;
        }

        var text = " " + MetadataMatch.Normalize(description) + " ";
        if (text.StartsWith(" episode ", StringComparison.Ordinal) || text.Contains(" episode of ", StringComparison.Ordinal))
        {
            return false;
        }

        if (work == WikidataWork.Series)
        {
            return SeriesWords.Any(word => text.Contains(" " + word + " ", StringComparison.Ordinal));
        }

        // The first "film" names what the item is: "film series" and "film franchise" are not one film,
        // while "2003 film, second in the Matrix film series" is.
        var (index, word) = FilmWords
            .Select(word => (Index: text.IndexOf(word, StringComparison.Ordinal), Word: word))
            .Where(found => found.Index >= 0)
            .OrderBy(found => found.Index)
            .FirstOrDefault();
        if (word is null)
        {
            return false;
        }

        var after = text[(index + word.Length)..];
        return !Collections.Any(noun => after.StartsWith(noun, StringComparison.Ordinal));
    }

    /// <summary>
    /// The item the file names among the fetched candidates, or null when none can be taken with
    /// confidence: a year from the file must agree within one year (releases straddle premieres),
    /// the search's order breaks ties, and with a year a dated item beats an undated one.
    /// </summary>
    public static WikidataEntity? Pick(int? year, WikidataWork work, IReadOnlyList<WikidataHit> candidates, IReadOnlyList<WikidataEntity> entities)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(entities);
        var ordered = candidates.Select(hit => entities.FirstOrDefault(entity => entity.Id == hit.Id)).Where(entity => entity is not null).ToList();
        var fitting = ordered.Where(entity => year is null || Year(entity!, work) is not { } dated || Math.Abs(dated - year.Value) <= 1).ToList();
        return fitting.OrderBy(entity => year is not null && Year(entity!, work) is null).FirstOrDefault();
    }

    /// <summary>The work's first year: its earliest publication date (P577), or a series' start time (P580).</summary>
    public static int? Year(WikidataEntity entity, WikidataWork work)
    {
        ArgumentNullException.ThrowIfNull(entity);
        var dates = work == WikidataWork.Film ? entity.Statements("P577") : [.. entity.Statements("P580"), .. entity.Statements("P577")];
        var years = dates.Select(claim => YearOf(claim.Value.Time)).Where(found => found is not null).ToList();
        return years.Count == 0 ? null : years.Min();
    }

    /// <summary>The year a Wikidata time ("+1999-03-31T00:00:00Z") names, or null for none or a date before the common era.</summary>
    public static int? YearOf(string? time) =>
        time is ['+', .. var rest] && rest.IndexOf('-', StringComparison.Ordinal) is > 0 and var dash
            && int.TryParse(rest[..dash], NumberStyles.None, CultureInfo.InvariantCulture, out var year) && year > 0 ? year : null;

    /// <summary>The day a Wikidata time names, as ISO ("1999-03-31"); null when it is less exact than a day or not a time.</summary>
    public static string? DateOf(string? time) =>
        time is ['+', .. var rest] && rest.Length >= 10 && YearOf(time) is not null && !rest[5..10].Contains("00", StringComparison.Ordinal)
            ? rest[..10] : null;

    /// <summary>Every item the work points at whose name (or picture) a card wants, each once, for the label query.</summary>
    public static IReadOnlyList<string> Referenced(WikidataEntity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        var ids = new List<string>();
        void Add(string? id)
        {
            if (id is not null && !ids.Contains(id, StringComparer.Ordinal))
            {
                ids.Add(id);
            }
        }

        foreach (var claim in Billed(entity))
        {
            Add(claim.Value.Item);
            Add(claim.Qualifier("P453")?.Item);
        }

        foreach (var property in new[] { "P57", "P58", "P170" })
        {
            foreach (var claim in entity.Statements(property).Take(property == "P58" ? WriterLimit : int.MaxValue))
            {
                Add(claim.Value.Item);
            }
        }

        foreach (var claim in entity.Statements("P136").Take(GenreLimit * 2))
        {
            Add(claim.Value.Item);
        }

        foreach (var property in new[] { "P272", "P449", "P364", "P495", "P1657", "P179" })
        {
            Add(entity.Statements(property).Select(claim => claim.Value.Item).FirstOrDefault(item => item is not null));
        }

        return ids;
    }

    /// <summary>A film's record from its item, the names the label query found, and its article's summary and art.</summary>
    public static EnrichedMovie Movie(WikidataEntity entity, IReadOnlyDictionary<string, WikidataLabel> labels, ArticleSummary? summary, int? fileYear)
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(labels);
        var backdrop = Wikipedia.ChooseArt(summary, Picture(entity)).BackdropUrl;
        return new EnrichedMovie
        {
            Schema = EnrichedMovie.CurrentSchema,
            CatalogueId = Wikidata.Number(entity.Id),
            Title = entity.Label ?? summary?.Title ?? entity.Id,
            Year = Year(entity, WikidataWork.Film) ?? fileYear,
            Overview = summary?.Extract,
            ArticleUrl = summary?.Extract is null ? null : summary.PageUrl,
            Genres = Genres(entity, labels),
            RuntimeMinutes = RuntimeMinutes(entity),
            Studio = First(entity, "P272", labels),
            OriginalLanguage = First(entity, "P364", labels),
            Country = First(entity, "P495", labels),
            ContentRating = First(entity, "P1657", labels),
            Collection = First(entity, "P179", labels),
            BackdropUrl = backdrop,
            Cast = Cast(entity, labels),
            Directors = Names(entity, "P57", labels, int.MaxValue),
            Writers = Names(entity, "P58", labels, WriterLimit),
            TrailerKeys = [.. entity.Statements("P1651").Select(claim => claim.Value.Text).OfType<string>().Distinct(StringComparer.Ordinal).Take(TrailerLimit)],
            ImdbId = entity.Statements("P345").Select(claim => claim.Value.Text).FirstOrDefault(text => text is not null),
        };
    }

    /// <summary>A show's candidate from its item when TVmaze has no match: the facts a card wants, with no episodes to list.</summary>
    public static ShowCandidate Show(WikidataEntity entity, IReadOnlyDictionary<string, WikidataLabel> labels, ArticleSummary? summary)
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(labels);
        return new ShowCandidate(Wikidata.Number(entity.Id), entity.Label ?? summary?.Title ?? entity.Id, Year(entity, WikidataWork.Series),
            summary?.Extract, Genres(entity, labels), null, Wikipedia.ChooseArt(summary, null).PosterUrl)
        {
            Network = First(entity, "P449", labels),
            Language = First(entity, "P364", labels),
            RuntimeMinutes = RuntimeMinutes(entity),
            Premiered = entity.Statements("P580").Select(claim => DateOf(claim.Value.Time)).FirstOrDefault(date => date is not null),
            Ended = entity.Statements("P582").Select(claim => DateOf(claim.Value.Time)).FirstOrDefault(date => date is not null),
        };
    }

    /// <summary>The cast of a work in billing order (P1545 where the statements give it), with characters and photos.</summary>
    public static IReadOnlyList<CastMember> Cast(WikidataEntity entity, IReadOnlyDictionary<string, WikidataLabel> labels)
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(labels);
        var cast = new List<CastMember>();
        foreach (var claim in Billed(entity))
        {
            if (claim.Value.Item is not { } person || !labels.TryGetValue(person, out var actor)
                || cast.Any(member => member.Name == actor.Label))
            {
                continue;
            }

            var character = claim.Qualifier("P453")?.Item is { } role && labels.TryGetValue(role, out var played) ? played.Label : claim.Qualifier("P4633")?.Text;
            cast.Add(new CastMember(actor.Label, character, actor.Image is { } photo ? Sized(photo, 185) : null));
        }

        return [.. cast.Take(CastLimit)];
    }

    /// <summary>The people a series' creator statements (P170) name.</summary>
    public static IReadOnlyList<string> Creators(WikidataEntity entity, IReadOnlyDictionary<string, WikidataLabel> labels)
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(labels);
        return Names(entity, "P170", labels, int.MaxValue);
    }

    /// <summary>Genre names without the "film" every film genre's label ends with ("science fiction film" reads "science fiction").</summary>
    public static IReadOnlyList<string> Genres(WikidataEntity entity, IReadOnlyDictionary<string, WikidataLabel> labels)
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(labels);
        return [.. entity.Statements("P136")
            .Select(claim => claim.Value.Item is { } genre && labels.TryGetValue(genre, out var named) ? GenreName(named.Label) : null)
            .Where(name => name is { Length: > 0 })
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(GenreLimit)!];
    }

    /// <summary>One genre label as a card shows it: a trailing "film" or "television series" dropped, the first letter capitalised.</summary>
    public static string GenreName(string label)
    {
        ArgumentNullException.ThrowIfNull(label);
        var name = label.Trim();
        foreach (var suffix in new[] { " television series", " film", " movie" })
        {
            if (name.Length > suffix.Length && name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                name = name[..^suffix.Length];
                break;
            }
        }

        return name.Length == 0 ? name : char.ToUpperInvariant(name[0]) + name[1..];
    }

    /// <summary>The running time in whole minutes (P2047, given in minutes, hours or seconds); null when absent or not positive.</summary>
    public static int? RuntimeMinutes(WikidataEntity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        foreach (var claim in entity.Statements("P2047"))
        {
            decimal? minutes = claim.Value switch
            {
                { Amount: { } amount, Unit: Minute } => amount,
                { Amount: { } amount, Unit: Hour } => amount * 60,
                { Amount: { } amount, Unit: Second } => amount / 60,
                _ => null,
            };
            if (minutes is { } known && Math.Round(known) is > 0 and var whole)
            {
                return (int)whole;
            }
        }

        return null;
    }

    /// <summary>A Commons picture address with a width asked for, so a card fetches a small copy rather than the original.</summary>
    public static string Sized(string commonsUrl, int width)
    {
        ArgumentNullException.ThrowIfNull(commonsUrl);
        var bare = commonsUrl.Split('?')[0];
        return bare + "?width=" + width.ToString(CultureInfo.InvariantCulture);
    }

    private static IEnumerable<WikidataClaim> Billed(WikidataEntity entity) =>
        entity.Statements("P161")
            .Select((claim, index) => (claim, index, order: claim.Qualifier("P1545")?.Text is { } text
                && int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var ordinal) ? ordinal : int.MaxValue))
            .OrderBy(billed => billed.order).ThenBy(billed => billed.index)
            .Select(billed => billed.claim)
            .Take(CastLimit);

    private static string? Picture(WikidataEntity entity) =>
        entity.Statements("P18").Select(claim => claim.Value.Text).FirstOrDefault(file => file is not null) is { } file
            ? Wikidata.CommonsUrl(file, 1280) : null;

    private static string? First(WikidataEntity entity, string property, IReadOnlyDictionary<string, WikidataLabel> labels) =>
        entity.Statements(property).Select(claim => claim.Value.Item is { } item && labels.TryGetValue(item, out var named) ? named.Label : null)
            .FirstOrDefault(name => name is not null);

    private static IReadOnlyList<string> Names(WikidataEntity entity, string property, IReadOnlyDictionary<string, WikidataLabel> labels, int limit) =>
        [.. entity.Statements(property)
            .Select(claim => claim.Value.Item is { } item && labels.TryGetValue(item, out var named) ? named.Label : null)
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)
            .Take(limit)];
}
