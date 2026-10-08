// Spec: The PLS playlist format, version 2: an INI file with a [playlist] section of FileN, TitleN and LengthN keys, NumberOfEntries and Version.
using System.Globalization;
using System.Text;

namespace Rex.Media.Library.Playlists;

/// <summary>PLS playlists (PLF-02).</summary>
internal static class Pls
{
    public static IReadOnlyList<PlaylistEntry> Parse(string text, string folder)
    {
        var files = new SortedDictionary<int, string>();
        var titles = new Dictionary<int, string>();
        var lengths = new Dictionary<int, TimeSpan>();
        var inPlaylist = false;
        foreach (var raw in PlaylistText.Lines(text))
        {
            var line = raw.Trim();
            if (line.StartsWith('['))
            {
                inPlaylist = line.Equals("[playlist]", StringComparison.OrdinalIgnoreCase);
                continue;
            }

            var equals = line.IndexOf('=', StringComparison.Ordinal);
            if (!inPlaylist || equals <= 0)
            {
                continue;
            }

            var (key, value) = (line[..equals].Trim(), line[(equals + 1)..].Trim());
            if (Numbered(key, "File") is { } file)
            {
                files[file] = value;
            }
            else if (Numbered(key, "Title") is { } title)
            {
                titles[title] = value;
            }
            else if (Numbered(key, "Length") is { } length && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) && seconds >= 0)
            {
                lengths[length] = TimeSpan.FromSeconds(seconds);
            }
        }

        var entries = new List<PlaylistEntry>();
        foreach (var (number, reference) in files)
        {
            if (PlaylistPaths.Resolve(reference, folder) is { } location)
            {
                entries.Add(new PlaylistEntry(location)
                {
                    Title = titles.GetValueOrDefault(number) is { Length: > 0 } title ? title : null,
                    Duration = lengths.TryGetValue(number, out var duration) ? duration : null,
                });
            }
        }

        return entries;
    }

    public static string Write(IReadOnlyList<PlaylistEntry> entries, string folder)
    {
        var text = new StringBuilder("[playlist]\n");
        for (var i = 0; i < entries.Count; i++)
        {
            var (entry, number) = (entries[i], i + 1);
            text.Append(CultureInfo.InvariantCulture, $"File{number}={PlaylistPaths.Relative(entry.Location, folder)}\n");
            if (entry.Title is { } title)
            {
                text.Append(CultureInfo.InvariantCulture, $"Title{number}={(entry.Artist is { Length: > 0 } artist ? $"{artist} - {title}" : title)}\n");
            }

            var seconds = entry.Duration is { } duration ? (long)Math.Round(duration.TotalSeconds) : -1;
            text.Append(CultureInfo.InvariantCulture, $"Length{number}={seconds}\n");
        }

        text.Append(CultureInfo.InvariantCulture, $"NumberOfEntries={entries.Count}\nVersion=2\n");
        return text.ToString();
    }

    private static int? Numbered(string key, string name) =>
        key.Length > name.Length && key.StartsWith(name, StringComparison.OrdinalIgnoreCase)
        && int.TryParse(key.AsSpan(name.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var number) ? number : null;
}
