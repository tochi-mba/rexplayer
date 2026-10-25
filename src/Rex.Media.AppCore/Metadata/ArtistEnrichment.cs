namespace Rex.Media.AppCore.Metadata;

/// <summary>
/// Puts one artist's record together from what each free source knows: MusicBrainz's facts, the
/// Wikidata item its "wikidata" link names (the photo, image P18 on Wikimedia Commons, and a
/// person's work period, P2031 to P2032) and the Wikipedia summary that item's sitelink leads to.
/// A group's years are its own life-span; a person's life-span is their life, so their years
/// active come from Wikidata alone. Pure; the fetcher does the asking.
/// </summary>
public static class ArtistEnrichment
{
    /// <summary>How wide the photo is fetched: enough for the artist page's wide header, through Commons' own scaling.</summary>
    public const int PhotoWidth = 1280;

    private static readonly string[] Ensembles = ["Group", "Orchestra", "Choir"];

    /// <summary>Whether MusicBrainz's type is a group of people, whose life-span is the years it was active.</summary>
    public static bool IsGroup(string? type) => type is not null && Ensembles.Contains(type, StringComparer.OrdinalIgnoreCase);

    /// <summary>The Commons file the item's photo is (its first image, P18), or null.</summary>
    public static string? PhotoFile(WikidataEntity? item) =>
        item?.Statements("P18").Select(claim => claim.Value.Text).FirstOrDefault(file => !string.IsNullOrWhiteSpace(file));

    /// <summary>Where the photo is fetched from, scaled by Commons to <see cref="PhotoWidth"/>; null with no photo.</summary>
    public static Uri? PhotoUri(WikidataEntity? item) =>
        PhotoFile(item) is { } file ? new Uri(Wikidata.CommonsUrl(file, PhotoWidth)) : null;

    /// <summary>The record for <paramref name="name"/> from the facts found; null facts means nothing matched.</summary>
    public static EnrichedArtist Build(string name, ArtistFacts? facts, WikidataEntity? item, ArticleSummary? summary, bool hasPhoto)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (facts is null)
        {
            return new EnrichedArtist { Schema = EnrichedArtist.CurrentSchema, Name = name };
        }

        var group = IsGroup(facts.Type);
        var from = group ? MusicBrainzFacts.YearOf(facts.Begin) : Year(item, "P2031");
        var to = group ? (facts.Ended ? MusicBrainzFacts.YearOf(facts.End) : null) : Year(item, "P2032");
        return new EnrichedArtist
        {
            Schema = EnrichedArtist.CurrentSchema,
            Name = name,
            MusicBrainzId = facts.Id,
            Type = facts.Type,
            Origin = facts.Origin,
            ActiveFrom = from,
            ActiveTo = from is null ? null : to,
            Genres = facts.Genres,
            Members = group ? facts.Members : [],
            WikidataId = item?.Id ?? facts.WikidataId,
            Bio = summary?.Extract,
            ArticleUrl = summary?.Extract is null ? null : summary.PageUrl,
            PhotoFile = hasPhoto ? PhotoFile(item) : null,
            HasPhoto = hasPhoto,
        };
    }

    private static int? Year(WikidataEntity? item, string property) =>
        item?.Statements(property).Select(claim => WikidataFacts.YearOf(claim.Value.Time)).FirstOrDefault(year => year is not null);
}
