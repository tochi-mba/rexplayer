// Spec: The CDRWIN cue sheet format: FILE, TRACK, INDEX (minutes:seconds:frames at 75 frames a second), TITLE, PERFORMER and REM commands, with quoted arguments.
using System.Globalization;

namespace Rex.Media.Library.Playlists;

/// <summary>
/// Cue sheets (PLF-04): an album in one file (or several) cut into tracks, each a part of a file
/// from its INDEX 01 to the next track's in the same file.
/// </summary>
internal static class Cue
{
    private sealed class Track
    {
        public required string File { get; init; }

        public required int Number { get; init; }

        public string? Title { get; set; }

        public string? Performer { get; set; }

        public TimeSpan? Start { get; set; }
    }

    public static IReadOnlyList<PlaylistEntry> Parse(string text, string folder)
    {
        string? file = null;
        string? albumPerformer = null;
        var tracks = new List<Track>();
        Track? track = null;
        foreach (var raw in PlaylistText.Lines(text))
        {
            var words = Words(raw);
            if (words.Count == 0)
            {
                continue;
            }

            var argument = words.Count > 1 ? words[1] : "";
            switch (words[0].ToUpperInvariant())
            {
                case "FILE":
                    file = PlaylistPaths.Resolve(argument, folder);
                    track = null;
                    break;
                case "TRACK" when file is not null:
                    // Only sound: a data track on a mixed CD has nothing to play.
                    track = words.Count > 2 && words[2].Equals("AUDIO", StringComparison.OrdinalIgnoreCase)
                        ? new Track { File = file, Number = int.TryParse(argument, NumberStyles.None, CultureInfo.InvariantCulture, out var number) ? number : tracks.Count + 1 }
                        : null;
                    if (track is not null)
                    {
                        tracks.Add(track);
                    }

                    break;
                case "TITLE" when track is not null:
                    track.Title = argument;
                    break;
                case "PERFORMER":
                    if (track is null)
                    {
                        albumPerformer = argument;
                    }
                    else
                    {
                        track.Performer = argument;
                    }

                    break;
                case "INDEX" when track is not null && argument == "01" && words.Count > 2:
                    track.Start = Time(words[2]);
                    break;
            }
        }

        var played = tracks.Where(t => t.Start is not null).ToList();
        return
        [
            .. played.Select((t, i) => new PlaylistEntry(t.File)
            {
                Title = t.Title ?? $"Track {t.Number:00}",
                Artist = t.Performer ?? albumPerformer,
                Start = t.Start!.Value,
                End = i + 1 < played.Count && played[i + 1].File == t.File ? played[i + 1].Start : null,
                Duration = i + 1 < played.Count && played[i + 1].File == t.File ? played[i + 1].Start - t.Start : null,
            }),
        ];
    }

    /// <summary>The words of a line, a quoted argument counting as one.</summary>
    private static List<string> Words(string line)
    {
        var words = new List<string>();
        var at = 0;
        while (at < line.Length)
        {
            if (char.IsWhiteSpace(line[at]))
            {
                at++;
                continue;
            }

            if (line[at] == '"')
            {
                var close = line.IndexOf('"', at + 1);
                var end = close < 0 ? line.Length : close;
                words.Add(line[(at + 1)..end]);
                at = end + 1;
                continue;
            }

            var start = at;
            while (at < line.Length && !char.IsWhiteSpace(line[at]))
            {
                at++;
            }

            words.Add(line[start..at]);
        }

        return words;
    }

    /// <summary>mm:ss:ff, with 75 frames to the second; null when it is not that.</summary>
    private static TimeSpan? Time(string text)
    {
        var parts = text.Split(':');
        return parts.Length == 3
            && int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var minutes)
            && int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var seconds)
            && int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var frames)
            && seconds < 60 && frames < 75
            ? TimeSpan.FromSeconds((minutes * 60) + seconds) + TimeSpan.FromTicks(frames * TimeSpan.TicksPerSecond / 75)
            : null;
    }
}
