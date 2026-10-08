// Spec: RFC 8216 sections 4.1 and 4.3.1-4.3.2.1 (the #EXTM3U header and #EXTINF duration and title), as extended M3U playlists of files use them; plain M3U is one location per line.
using System.Globalization;
using System.Text;

namespace Rex.Media.Library.Playlists;

/// <summary>M3U and M3U8 playlists (PLF-01): one location per line, with optional #EXTINF details.</summary>
internal static class M3u
{
    // Tags only a live stream's description carries: such a file is a stream, not a list of files.
    private static readonly string[] StreamTags = ["#EXT-X-TARGETDURATION", "#EXT-X-STREAM-INF", "#EXT-X-MEDIA-SEQUENCE"];

    public static IReadOnlyList<PlaylistEntry> Parse(string text, string folder)
    {
        var entries = new List<PlaylistEntry>();
        (string? Title, string? Artist, TimeSpan? Duration) pending = default;
        foreach (var raw in PlaylistText.Lines(text))
        {
            var line = raw.Trim();
            if (StreamTags.Any(tag => line.StartsWith(tag, StringComparison.OrdinalIgnoreCase)))
            {
                throw new FormatException("This is the description of a stream, not a playlist of files.");
            }

            if (line.StartsWith("#EXTINF:", StringComparison.OrdinalIgnoreCase))
            {
                pending = Info(line["#EXTINF:".Length..]);
                continue;
            }

            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            if (PlaylistPaths.Resolve(line, folder) is { } location)
            {
                entries.Add(new PlaylistEntry(location) { Title = pending.Title, Artist = pending.Artist, Duration = pending.Duration });
            }

            pending = default;
        }

        return entries;
    }

    public static string Write(IReadOnlyList<PlaylistEntry> entries, string folder)
    {
        var text = new StringBuilder("#EXTM3U\n");
        foreach (var entry in entries)
        {
            if (entry.Title is not null || entry.Duration is not null)
            {
                var seconds = entry.Duration is { } duration ? Math.Round(duration.TotalSeconds).ToString(CultureInfo.InvariantCulture) : "-1";
                var name = entry.Artist is { Length: > 0 } artist ? $"{artist} - {entry.Title}" : entry.Title;
                text.Append(CultureInfo.InvariantCulture, $"#EXTINF:{seconds},{name}\n");
            }

            text.Append(PlaylistPaths.Relative(entry.Location, folder)).Append('\n');
        }

        return text.ToString();
    }

    /// <summary>"#EXTINF:123 tvg-name=..,Artist - Title": the length (-1 for unknown), then the name.</summary>
    private static (string? Title, string? Artist, TimeSpan? Duration) Info(string value)
    {
        var comma = value.IndexOf(',', StringComparison.Ordinal);
        var head = comma < 0 ? value : value[..comma];
        var name = comma < 0 ? "" : value[(comma + 1)..].Trim();
        var number = head.Split(' ', 2)[0];
        TimeSpan? duration = double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) && seconds >= 0 ? TimeSpan.FromSeconds(seconds) : null;
        var dash = name.IndexOf(" - ", StringComparison.Ordinal);
        return name.Length == 0 ? (null, null, duration)
            : dash > 0 ? (name[(dash + 3)..].Trim(), name[..dash].Trim(), duration)
            : (name, null, duration);
    }
}
