namespace Rex.Media.Settings;

/// <summary>
/// Online metadata enrichment: posters, descriptions, episode titles, genres, ratings and album
/// art from free catalogues. Fetching sends the names of the user's files and folders to those
/// services, so every switch here is off until the user turns it on, and a folder on the excluded
/// list is never looked up at all. Everything fetched is kept on this computer, so the library
/// works offline once it is filled. No catalogue needs a key or a sign-up: a key stored by an
/// earlier version (for a catalogue no longer used) is ignored when the file is read and dropped at
/// the next save, and its Movies switch keeps its place.
/// </summary>
public sealed record MetadataSettings
{
    /// <summary>Fetch TV show details: posters, descriptions, episode titles and synopses.</summary>
    public bool TvShows { get; init; }

    /// <summary>Fetch movie details from Wikidata and Wikipedia; the switch alone turns it on.</summary>
    public bool Movies { get; init; }

    /// <summary>Fetch album art and music details.</summary>
    public bool Music { get; init; }

    /// <summary>
    /// Fetched covers replace the artwork albums already carry, on screen only; off, they only fill
    /// gaps. Nothing is ever written into the user's files either way.
    /// </summary>
    public bool ReplaceCovers { get; init; }

    /// <summary>Folders whose files are never looked up or enriched, whatever the switches say.</summary>
    public IReadOnlyList<string> Excluded { get; init; } = [];

    /// <summary>A copy with every value usable: the folder list without blanks or repeats.</summary>
    public MetadataSettings Normalize() => this with { Excluded = Clean(Excluded) };

    private static string[] Clean(IReadOnlyList<string>? folders) =>
        [.. (folders ?? []).Where(folder => !string.IsNullOrWhiteSpace(folder)).Select(folder => folder.Trim()).Distinct(StringComparer.OrdinalIgnoreCase)];
}
