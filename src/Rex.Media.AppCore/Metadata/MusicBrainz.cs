using System.Globalization;
using System.Text.Json;

namespace Rex.Media.AppCore.Metadata;

/// <summary>One album a music catalogue offered for a search, before any is chosen.</summary>
public sealed record AlbumCandidate(string Id, string Title, string? Artist, int Score, int? Year);

/// <summary>
/// The MusicBrainz catalogue and the Cover Art Archive (both free, no key): where to ask and how
/// to read the answers. MusicBrainz asks for at most one request a second and a meaningful
/// User-Agent; the window's fetch delegate enforces both. Only the album's and artist's names
/// ever leave the computer, and only once the user turned music lookups on.
/// </summary>
public static class MusicBrainz
{
    /// <summary>Searching release groups (albums as people mean them) by album and artist name.</summary>
    public static Uri SearchUri(string? artist, string album)
    {
        ArgumentNullException.ThrowIfNull(album);
        var query = "releasegroup:" + Quoted(album) + (string.IsNullOrWhiteSpace(artist) ? "" : " AND artist:" + Quoted(artist!));
        return new Uri("https://musicbrainz.org/ws/2/release-group/?limit=8&fmt=json&query=" + Uri.EscapeDataString(query));
    }

    /// <summary>The Cover Art Archive's front cover for a release group, already sized for a card.</summary>
    public static Uri CoverUri(string releaseGroupId) =>
        new($"https://coverartarchive.org/release-group/{releaseGroupId ?? throw new ArgumentNullException(nameof(releaseGroupId))}/front-500");

    /// <summary>The same front cover small, for a row of candidates to pick from.</summary>
    public static Uri CoverThumbUri(string releaseGroupId) =>
        new($"https://coverartarchive.org/release-group/{releaseGroupId ?? throw new ArgumentNullException(nameof(releaseGroupId))}/front-250");

    /// <summary>The albums a search answered with, best first; malformed JSON throws <see cref="JsonException"/>.</summary>
    public static IReadOnlyList<AlbumCandidate> ParseSearch(string json)
    {
        using var doc = JsonDocument.Parse(json ?? throw new ArgumentNullException(nameof(json)));
        if (doc.RootElement.ValueKind != JsonValueKind.Object
            || !doc.RootElement.TryGetProperty("release-groups", out var groups) || groups.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var candidates = new List<AlbumCandidate>();
        foreach (var group in groups.EnumerateArray())
        {
            if (group.ValueKind != JsonValueKind.Object
                || Text(group, "id") is not { Length: > 0 } id || Text(group, "title") is not { Length: > 0 } title)
            {
                continue;
            }

            string? artist = null;
            if (group.TryGetProperty("artist-credit", out var credit) && credit.ValueKind == JsonValueKind.Array)
            {
                var names = credit.EnumerateArray()
                    .Where(part => part.ValueKind == JsonValueKind.Object)
                    .Select(part => Text(part, "name"))
                    .Where(name => !string.IsNullOrEmpty(name));
                artist = string.Join(" ", names) is { Length: > 0 } joined ? joined : null;
            }

            var score = group.TryGetProperty("score", out var scored) && scored.ValueKind == JsonValueKind.Number && scored.TryGetInt32(out var given) ? given : 0;
            int? year = Text(group, "first-release-date") is { Length: >= 4 } date
                && int.TryParse(date[..4], NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
            candidates.Add(new AlbumCandidate(id, title, artist, score, year));
        }

        return candidates;
    }

    /// <summary>
    /// The candidate that is the album, or null when none can be taken with confidence: the title
    /// must match exactly after normalizing, the artist too when both sides name one, and the
    /// catalogue's own best score breaks ties.
    /// </summary>
    public static AlbumCandidate? Pick(string? artist, string album, IReadOnlyList<AlbumCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(album);
        ArgumentNullException.ThrowIfNull(candidates);
        var wantedTitle = MetadataMatch.Normalize(album);
        var wantedArtist = string.IsNullOrWhiteSpace(artist) ? "" : MetadataMatch.Normalize(artist!);
        if (wantedTitle.Length == 0)
        {
            return null;
        }

        return candidates
            .Where(candidate => MetadataMatch.Normalize(candidate.Title) == wantedTitle)
            .Where(candidate => wantedArtist.Length == 0 || candidate.Artist is null || MetadataMatch.Normalize(candidate.Artist) == wantedArtist)
            .OrderByDescending(candidate => candidate.Score)
            .ThenByDescending(candidate => candidate.Artist is not null)
            .FirstOrDefault();
    }

    /// <summary>One key for an album wherever it is named: the artist and the title, normalized.</summary>
    public static string AlbumKey(string? artist, string album) =>
        (string.IsNullOrWhiteSpace(artist) ? "" : MetadataMatch.Normalize(artist!)) + "\u001f" + MetadataMatch.Normalize(album ?? throw new ArgumentNullException(nameof(album)));

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static string Quoted(string value) => "\"" + value.Replace("\"", "\\\"") + "\"";
}
