using System.Globalization;
using System.Text.Json;

namespace Rex.Media.AppCore.Metadata;

/// <summary>One artist a MusicBrainz search offered, before any is chosen.</summary>
public sealed record ArtistCandidate(string Id, string Name, int Score);

/// <summary>What MusicBrainz says of one release group beyond its title: its kind, first release, a release to ask for labels, its tags and its artists.</summary>
public sealed record ReleaseGroupFacts(string? PrimaryType, IReadOnlyList<string> SecondaryTypes, string? FirstReleased, string? ReleaseId, IReadOnlyList<string> Tags);

/// <summary>What MusicBrainz says of one artist: what they are, where from, when, their genres, their members and their Wikidata item.</summary>
public sealed record ArtistFacts(string Id, string Name, string? Type, string? Origin, string? Begin, string? End, bool Ended,
    IReadOnlyList<string> Genres, IReadOnlyList<string> Members, string? WikidataId);

/// <summary>
/// The rest of MusicBrainz a music page reads (free, no key, about one request a second): artist
/// search and lookup with their links, members and genres, and a release's labels; plus the facts
/// a release-group search already carries (type, first release date, tags). Only an artist's or
/// album's name and the catalogue's own identifiers ever leave the computer.
/// </summary>
public static class MusicBrainzFacts
{
    /// <summary>How many genres an artist or album keeps, the most voted first.</summary>
    public const int GenreLimit = 5;

    /// <summary>How many members a group keeps, current members first.</summary>
    public const int MemberLimit = 12;

    /// <summary>Searching artists by name.</summary>
    public static Uri ArtistSearchUri(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return new Uri("https://musicbrainz.org/ws/2/artist/?limit=5&fmt=json&query=" + Uri.EscapeDataString("artist:\"" + name.Replace("\"", "\\\"", StringComparison.Ordinal) + "\""));
    }

    /// <summary>One artist with their links (Wikidata among them), their relations to other artists (a group's members) and genres.</summary>
    public static Uri ArtistUri(string id) =>
        new("https://musicbrainz.org/ws/2/artist/" + Checked(id) + "?inc=url-rels%2Bartist-rels%2Bgenres&fmt=json");

    /// <summary>One release with its labels.</summary>
    public static Uri ReleaseUri(string id) =>
        new("https://musicbrainz.org/ws/2/release/" + Checked(id) + "?inc=labels&fmt=json");

    /// <summary>The artists a search answered with, best first; malformed JSON throws <see cref="JsonException"/>.</summary>
    public static IReadOnlyList<ArtistCandidate> ParseArtistSearch(string json)
    {
        using var doc = JsonDocument.Parse(json ?? throw new ArgumentNullException(nameof(json)));
        if (!TryArray(doc.RootElement, "artists", out var artists))
        {
            return [];
        }

        return [.. artists.EnumerateArray()
            .Where(artist => artist.ValueKind == JsonValueKind.Object)
            .Select(artist => (Id: Text(artist, "id"), Name: Text(artist, "name"), Score: Int(artist, "score") ?? 0))
            .Where(artist => !string.IsNullOrEmpty(artist.Id) && !string.IsNullOrEmpty(artist.Name))
            .Select(artist => new ArtistCandidate(artist.Id!, artist.Name!, artist.Score))];
    }

    /// <summary>The candidate that is the artist: the same name after normalizing, the catalogue's best score first; null when none is.</summary>
    public static ArtistCandidate? PickArtist(string name, IReadOnlyList<ArtistCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(candidates);
        var wanted = MetadataMatch.Normalize(name);
        return wanted.Length == 0 ? null : candidates
            .Where(candidate => MetadataMatch.Normalize(candidate.Name) == wanted)
            .OrderByDescending(candidate => candidate.Score)
            .FirstOrDefault();
    }

    /// <summary>
    /// One artist's facts from a lookup; null for an answer that is not an artist. The origin is
    /// where they began and the country around it ("Lagos, Nigeria"), the members are the artists
    /// related as "member of band" toward this one, current first, and the Wikidata item is read
    /// from the "wikidata" link. Malformed JSON throws <see cref="JsonException"/>.
    /// </summary>
    public static ArtistFacts? ParseArtist(string json)
    {
        using var doc = JsonDocument.Parse(json ?? throw new ArgumentNullException(nameof(json)));
        var artist = doc.RootElement;
        if (artist.ValueKind != JsonValueKind.Object || Text(artist, "id") is not { Length: > 0 } id || Text(artist, "name") is not { Length: > 0 } name)
        {
            return null;
        }

        var place = Place(artist, "begin-area");
        var country = Place(artist, "area");
        var origin = place is null ? country
            : country is null || string.Equals(place, country, StringComparison.OrdinalIgnoreCase) ? place
            : place + ", " + country;
        string? begin = null, end = null;
        var ended = false;
        if (artist.TryGetProperty("life-span", out var life) && life.ValueKind == JsonValueKind.Object)
        {
            begin = Text(life, "begin");
            end = Text(life, "end");
            ended = life.TryGetProperty("ended", out var over) && over.ValueKind == JsonValueKind.True;
        }

        var members = new List<(string Name, bool Ended)>();
        string? wikidata = null;
        if (TryArray(artist, "relations", out var relations))
        {
            foreach (var relation in relations.EnumerateArray().Where(relation => relation.ValueKind == JsonValueKind.Object))
            {
                var type = Text(relation, "type");
                if (type == "wikidata" && wikidata is null && relation.TryGetProperty("url", out var url) && url.ValueKind == JsonValueKind.Object)
                {
                    wikidata = WikidataItem(Text(url, "resource"));
                }
                else if (type == "member of band" && Text(relation, "direction") == "backward"
                    && relation.TryGetProperty("artist", out var member) && member.ValueKind == JsonValueKind.Object
                    && Text(member, "name") is { Length: > 0 } memberName)
                {
                    members.Add((memberName, relation.TryGetProperty("ended", out var left) && left.ValueKind == JsonValueKind.True));
                }
            }
        }

        return new ArtistFacts(id, name, NullIfBlank(Text(artist, "type")), origin, NullIfBlank(begin), NullIfBlank(end), ended, Voted(artist, "genres"),
            [.. members.OrderBy(member => member.Ended).Select(member => member.Name).Distinct(StringComparer.OrdinalIgnoreCase).Take(MemberLimit)], wikidata);
    }

    /// <summary>
    /// What a release-group search says of the group <paramref name="id"/>: its primary and
    /// secondary types, first release date, its first official release (else its first) and its
    /// tags, the most voted first; null when the answer does not hold it. Malformed JSON throws
    /// <see cref="JsonException"/>.
    /// </summary>
    public static ReleaseGroupFacts? ParseReleaseGroup(string json, string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        using var doc = JsonDocument.Parse(json ?? throw new ArgumentNullException(nameof(json)));
        if (!TryArray(doc.RootElement, "release-groups", out var groups))
        {
            return null;
        }

        foreach (var group in groups.EnumerateArray())
        {
            if (group.ValueKind != JsonValueKind.Object || Text(group, "id") != id)
            {
                continue;
            }

            string[] secondary = TryArray(group, "secondary-types", out var types)
                ? [.. types.EnumerateArray().Where(type => type.ValueKind == JsonValueKind.String).Select(type => type.GetString()!).Where(type => type.Length > 0)]
                : [];
            string? release = null;
            if (TryArray(group, "releases", out var releases))
            {
                var listed = releases.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.Object && !string.IsNullOrEmpty(Text(item, "id"))).ToList();
                release = Text(listed.FirstOrDefault(item => Text(item, "status") == "Official", listed.FirstOrDefault()), "id");
            }

            return new ReleaseGroupFacts(NullIfBlank(Text(group, "primary-type")), secondary, NullIfBlank(Text(group, "first-release-date")), release, Voted(group, "tags"));
        }

        return null;
    }

    /// <summary>The labels a release came out on, each once, in the catalogue's order; malformed JSON throws <see cref="JsonException"/>.</summary>
    public static IReadOnlyList<string> ParseLabels(string json)
    {
        using var doc = JsonDocument.Parse(json ?? throw new ArgumentNullException(nameof(json)));
        if (!TryArray(doc.RootElement, "label-info", out var info))
        {
            return [];
        }

        return [.. info.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.Object && item.TryGetProperty("label", out var label) && label.ValueKind == JsonValueKind.Object)
            .Select(item => NullIfBlank(Text(item.GetProperty("label"), "name")))
            .OfType<string>()
            .Where(label => !label.Equals("[no label]", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>The year a catalogue date starts with ("2024-08-08", "2018"), or null.</summary>
    public static int? YearOf(string? date) =>
        date is { Length: >= 4 } && int.TryParse(date.AsSpan(0, 4), NumberStyles.None, CultureInfo.InvariantCulture, out var year) && year > 0 ? year : null;

    /// <summary>The Wikidata item a link points at ("https://www.wikidata.org/wiki/Q112861798"), or null for any other link.</summary>
    public static string? WikidataItem(string? link)
    {
        if (!Uri.TryCreate(link, UriKind.Absolute, out var uri) || !uri.Host.EndsWith("wikidata.org", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var item = uri.Segments[^1];
        return item.Length > 1 && item[0] == 'Q' && item.Skip(1).All(char.IsAsciiDigit) ? item : null;
    }

    // Names, sorted by votes, as genres or tags carry them: [{"name":"afrobeats","count":3}].
    private static string[] Voted(JsonElement element, string property) => TryArray(element, property, out var list)
        ? [.. list.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.Object && !string.IsNullOrWhiteSpace(Text(item, "name")))
            .Select(item => (Name: Text(item, "name")!.Trim(), Votes: Int(item, "count") ?? 0))
            .OrderByDescending(item => item.Votes)
            .Select(item => item.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(GenreLimit)]
        : [];

    private static string? Place(JsonElement artist, string property) =>
        artist.TryGetProperty(property, out var area) && area.ValueKind == JsonValueKind.Object ? NullIfBlank(Text(area, "name")) : null;

    private static string Checked(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        return id.Length > 0 && id.All(c => char.IsAsciiHexDigit(c) || c == '-') ? id : throw new ArgumentException("Not a MusicBrainz identifier.", nameof(id));
    }

    private static bool TryArray(JsonElement element, string property, out JsonElement array)
    {
        array = default;
        return element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out array) && array.ValueKind == JsonValueKind.Array;
    }

    private static string? Text(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static int? Int(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) ? number : null;

    private static string? NullIfBlank(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}
