using System.Globalization;
using Rex.Media.AppCore.Player;
using Rex.Media.Library;
using Rex.Media.Settings;

namespace Rex.Media.AppCore.Library;

/// <summary>A heading and what is under it, in a grouped library view.</summary>
public sealed record LibrarySection(string Header, IReadOnlyList<LibraryEntry> Entries);

/// <summary>
/// Orders and groups a library view as the user asks (LIB-05): by title, artist, album, year, when
/// it was added or last played, how often, how long; under its first letter, artist, album, genre,
/// year, decade, folder, when it was added, or its length. Sorting keeps ties in the view's own
/// order; headings come in the order that reads naturally (A to Z, newest first, shortest first),
/// and what falls under no heading ("Unknown year") comes last.
/// </summary>
public static class LibraryArrangement
{
    private static readonly StringComparer ByName = StringComparer.Create(CultureInfo.CurrentCulture, CompareOptions.IgnoreCase);

    /// <summary><paramref name="entries"/> in the order <paramref name="sort"/> gives, reversed when <paramref name="descending"/>.</summary>
    public static IReadOnlyList<LibraryEntry> Sort(IReadOnlyList<LibraryEntry> entries, LibrarySort sort, bool descending)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var order = entries.Select((entry, index) => (Entry: entry, Index: index));
        IEnumerable<(LibraryEntry Entry, int Index)> sorted = sort switch
        {
            LibrarySort.Title => order.OrderBy(pair => pair.Entry.Title, NaturalOrder.Instance),
            LibrarySort.Artist => order.OrderBy(pair => pair.Entry.FiledArtist is null).ThenBy(pair => pair.Entry.FiledArtist ?? "", ByName)
                .ThenBy(pair => pair.Entry.Album ?? "", ByName).ThenBy(pair => pair.Entry.Disc ?? 0).ThenBy(pair => pair.Entry.Track ?? int.MaxValue),
            LibrarySort.Album => order.OrderBy(pair => pair.Entry.Album is null).ThenBy(pair => pair.Entry.Album ?? "", ByName)
                .ThenBy(pair => pair.Entry.Disc ?? 0).ThenBy(pair => pair.Entry.Track ?? int.MaxValue),
            LibrarySort.Year => order.OrderBy(pair => pair.Entry.Year is null).ThenBy(pair => pair.Entry.Year ?? 0),

            // Newest, latest and most first: what a person sorting by these wants to see.
            LibrarySort.Added => order.OrderByDescending(pair => pair.Entry.Added),
            LibrarySort.Played => order.OrderBy(pair => pair.Entry.LastPlayed is null).ThenByDescending(pair => pair.Entry.LastPlayed ?? default),
            LibrarySort.MostPlayed => order.OrderByDescending(pair => pair.Entry.Plays),
            LibrarySort.Length => order.OrderBy(pair => pair.Entry.Duration is null).ThenBy(pair => pair.Entry.Duration ?? TimeSpan.Zero),
            _ => order,
        };
        var list = sorted is IOrderedEnumerable<(LibraryEntry Entry, int Index)> ordered ? ordered.ThenBy(pair => pair.Index) : sorted;
        var result = list.Select(pair => pair.Entry).ToList();
        if (descending)
        {
            result.Reverse();
        }

        return result;
    }

    /// <summary>
    /// <paramref name="sorted"/> under headings by <paramref name="grouping"/>, keeping their order
    /// within each; one section with no heading when not grouped. <paramref name="now"/> (UTC) places
    /// "Today" and "This week".
    /// </summary>
    public static IReadOnlyList<LibrarySection> Group(IReadOnlyList<LibraryEntry> sorted, LibraryGrouping grouping, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(sorted);
        if (grouping == LibraryGrouping.None)
        {
            return sorted.Count == 0 ? [] : [new LibrarySection("", sorted)];
        }

        return [.. sorted.Select((entry, index) => (Entry: entry, Index: index, Key: Key(entry, grouping, now)))
            .GroupBy(item => item.Key.Header, ByName)
            .Select(group => (group.First().Key, Section: new LibrarySection(group.First().Key.Header, [.. group.Select(item => item.Entry)])))
            .OrderBy(pair => pair.Key.Last)
            .ThenBy(pair => pair.Key.Rank)
            .ThenBy(pair => pair.Key.Header, ByName)
            .Select(pair => pair.Section)];
    }

    /// <summary>The heading for an entry, where it ranks among headings, and whether it goes after them all.</summary>
    private static (string Header, double Rank, bool Last) Key(LibraryEntry entry, LibraryGrouping grouping, DateTime now)
    {
        switch (grouping)
        {
            case LibraryGrouping.Letter:
                var first = entry.Title.TrimStart().FirstOrDefault();
                return char.IsLetter(first) ? (char.ToUpper(first, CultureInfo.CurrentCulture).ToString(), 0, false) : ("#", 0, true);
            case LibraryGrouping.Artist:
                return entry.FiledArtist is { } artist ? (artist, 0, false) : (LibraryViews.UnknownArtist, 0, true);
            case LibraryGrouping.Album:
                return entry.Album is { } album ? (album, 0, false) : (LibraryViews.UnknownAlbum, 0, true);
            case LibraryGrouping.Genre:
                return entry.Genre is { } genre ? (genre, 0, false) : (LibraryViews.UnknownGenre, 0, true);
            case LibraryGrouping.Year:
                return entry.Year is { } year ? (year.ToString(CultureInfo.InvariantCulture), -year, false) : ("Unknown year", 0, true);
            case LibraryGrouping.Decade:
                return entry.Year is { } when ? ($"{when / 10 * 10}s", -(when / 10), false) : ("Unknown year", 0, true);
            case LibraryGrouping.Folder:
                var folder = Path.GetDirectoryName(entry.Path) ?? "";
                return (Path.GetFileName(folder) is { Length: > 0 } name ? name : folder, 0, false);
            case LibraryGrouping.Added:
                return Added(entry.Added, now);
            default:
                return Length(entry.Duration);
        }
    }

    /// <summary>When it was added, as a person would say it: today, this week, this month, this year, or the year.</summary>
    private static (string Header, double Rank, bool Last) Added(DateTime added, DateTime now)
    {
        var age = now - added;
        return age.TotalDays switch
        {
            < 1 => ("Today", 0, false),
            < 7 => ("This week", 1, false),
            < 31 => ("This month", 2, false),
            _ when added.Year == now.Year => ("Earlier this year", 3, false),
            _ => (added.Year.ToString(CultureInfo.InvariantCulture), 10_000 - added.Year, false),
        };
    }

    /// <summary>How long it lasts, in the spans songs and videos fall into.</summary>
    private static (string Header, double Rank, bool Last) Length(TimeSpan? duration) => duration?.TotalMinutes switch
    {
        null => ("Unknown length", 0, true),
        < 3 => ("Under 3 minutes", 0, false),
        < 6 => ("3 to 6 minutes", 1, false),
        < 20 => ("6 to 20 minutes", 2, false),
        < 60 => ("20 minutes to an hour", 3, false),
        _ => ("Over an hour", 4, false),
    };

    /// <summary>
    /// Groups of entries (albums, artists, genres) in the order <paramref name="sort"/> gives, judged
    /// by what is in each: its newest addition, its latest play, its plays together, its year.
    /// </summary>
    public static IReadOnlyList<LibraryGroup> Sort(IReadOnlyList<LibraryGroup> groups, LibrarySort sort, bool descending)
    {
        ArgumentNullException.ThrowIfNull(groups);
        var order = groups.Select((group, index) => (Group: group, Index: index));
        IEnumerable<(LibraryGroup Group, int Index)> sorted = sort switch
        {
            LibrarySort.Title or LibrarySort.Album => order.OrderBy(pair => pair.Group.Name, NaturalOrder.Instance),
            LibrarySort.Artist => order.OrderBy(pair => pair.Group.Detail is null).ThenBy(pair => pair.Group.Detail ?? pair.Group.Name, ByName),
            LibrarySort.Year => order.OrderBy(pair => pair.Group.Entries.Max(entry => entry.Year) is null).ThenBy(pair => pair.Group.Entries.Max(entry => entry.Year) ?? 0),
            LibrarySort.Added => order.OrderByDescending(pair => pair.Group.Entries.Max(entry => entry.Added)),
            LibrarySort.Played => order.OrderByDescending(pair => pair.Group.Entries.Max(entry => entry.LastPlayed) ?? default),
            LibrarySort.MostPlayed => order.OrderByDescending(pair => pair.Group.Entries.Sum(entry => entry.Plays)),
            LibrarySort.Length => order.OrderBy(pair => pair.Group.Entries.Sum(entry => entry.Duration?.Ticks ?? 0)),
            _ => order,
        };
        var list = (sorted is IOrderedEnumerable<(LibraryGroup Group, int Index)> ordered ? ordered.ThenBy(pair => pair.Index) : sorted).Select(pair => pair.Group).ToList();
        if (descending)
        {
            list.Reverse();
        }

        return list;
    }
}
