using System.Globalization;
using Rex.Media.AppCore.Player;
using Rex.Media.Library;

namespace Rex.Media.AppCore.Library;

/// <summary>
/// Honest, file-first facts for one persisted playlist. The saved list may mix music,
/// pictures, generic videos and unavailable URLs; a file extension is a fallback only.
/// A duration is counted only when a stored media duration or cue end proves it.
/// </summary>
public sealed record SavedPlaylistFacts(int Count, int Audio, int Video, int Pictures, int Other, int Timed, TimeSpan KnownDuration)
{
    public static SavedPlaylistFacts For(NamedPlaylist playlist, Func<string, LibraryEntry?> libraryEntry)
    {
        ArgumentNullException.ThrowIfNull(playlist);
        ArgumentNullException.ThrowIfNull(libraryEntry);
        var (audio, video, pictures, other, timed) = (0, 0, 0, 0, 0);
        var length = TimeSpan.Zero;
        foreach (var item in playlist.Items)
        {
            var entry = libraryEntry(item.Location);
            switch (entry?.Kind ?? MediaFiles.LibraryKindOf(item.Location))
            {
                case LibraryKind.Music:
                    audio++;
                    break;
                case LibraryKind.Video:
                    video++;
                    break;
                case LibraryKind.Picture:
                    pictures++;
                    break;
                default:
                    other++;
                    break;
            }

            var end = item.End is { } cueEnd && entry?.Duration is { } full
                ? (cueEnd < full ? cueEnd : full)
                : item.End ?? entry?.Duration;
            if (end is { } known && known > item.Start && known > TimeSpan.Zero)
            {
                length += known - item.Start;
                timed++;
            }
        }

        return new SavedPlaylistFacts(playlist.Items.Count, audio, video, pictures, other, timed, length);
    }

    /// <summary>A concise description that never calls a partial duration the total time.</summary>
    public string Label
    {
        get
        {
            if (Count == 0)
            {
                return "Empty playlist";
            }

            var parts = new List<string>();
            if (Audio > 0) parts.Add(CountOf(Audio, "audio item"));
            if (Video > 0) parts.Add(CountOf(Video, "video"));
            if (Pictures > 0) parts.Add(CountOf(Pictures, "picture"));
            if (Other > 0) parts.Add(CountOf(Other, "unclassified item"));
            if (Timed > 0)
            {
                var hours = (long)KnownDuration.TotalHours;
                var time = hours > 0
                    ? string.Create(CultureInfo.InvariantCulture, $"{hours}:{KnownDuration.Minutes:00}:{KnownDuration.Seconds:00}")
                    : string.Create(CultureInfo.InvariantCulture, $"{(int)KnownDuration.TotalMinutes}:{KnownDuration.Seconds:00}");
                parts.Add(time + (Timed == Count ? " duration" : " known duration"));
            }

            return string.Join(" · ", parts);
        }
    }

    private static string CountOf(int count, string noun) =>
        string.Create(CultureInfo.CurrentCulture, $"{count} {noun}{(count == 1 ? "" : "s")}");
}
