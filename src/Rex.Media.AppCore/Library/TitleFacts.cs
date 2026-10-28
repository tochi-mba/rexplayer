using System.Globalization;
using Rex.Media.AppCore.Metadata;
using Rex.Media.Library;

namespace Rex.Media.AppCore.Library;

/// <summary>
/// The short lines of facts the details screens show under a title: a movie's year, length,
/// rating and genres, a show's network, years and status, and who made a film. Fetched facts win
/// where they exist; with none (metadata turned off, or nothing found) the lines come from the
/// file alone, so every screen still reads complete. A fact that is missing is left out, never
/// shown empty.
/// </summary>
public static class TitleFacts
{
    /// <summary>What sits between two facts on a line.</summary>
    public const string Separator = " · ";

    /// <summary>How many genres a facts line names at most.</summary>
    public const int Genres = 3;

    /// <summary>
    /// A movie's facts: "2023 · 1 h 52 min · R · Action · Drama". The fetched year, runtime, rating
    /// and genres where there are some, the file's own year, length and first genre otherwise.
    /// </summary>
    public static string Movie(LibraryEntry entry, EnrichedMovie? facts)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (facts is null)
        {
            return StreamingBrowse.MovieLine(entry);
        }

        var year = facts.Year ?? LibraryViews.MovieYearOf(entry);
        var length = facts.RuntimeMinutes is { } minutes && minutes > 0
            ? StreamingBrowse.Length(TimeSpan.FromMinutes(minutes))
            : StreamingBrowse.Length(entry.Duration);
        var genres = facts.Genres.Count > 0 ? facts.Genres.Take(Genres) : GenreNames.Split(entry.Genre).Take(1);
        return Join([year?.ToString(CultureInfo.CurrentCulture), length, facts.ContentRating, .. genres]);
    }

    /// <summary>
    /// A show's facts for the billboard, in the movie line's order: "2011 · 1 h · 8.9 / 10 · Drama";
    /// <paramref name="local"/> (what the library itself knows) when nothing was fetched.
    /// </summary>
    public static string ShowLine(EnrichedShow? show, string local)
    {
        ArgumentNullException.ThrowIfNull(local);
        if (show is not { Found: true })
        {
            return local;
        }

        var line = Join([
            YearOf(show.Premiered)?.ToString(CultureInfo.CurrentCulture),
            show.RuntimeMinutes is { } minutes && minutes > 0 ? StreamingBrowse.Length(TimeSpan.FromMinutes(minutes)) : null,
            show.Rating is { } rating ? rating.ToString("0.0", CultureInfo.InvariantCulture) + " / 10" : null,
            .. show.Genres.Take(Genres),
        ]);
        return line.Length > 0 ? line : local;
    }

    /// <summary>A show's facts under its title: "HBO · 2011-2019 · Ended"; empty when nothing was fetched.</summary>
    public static string Show(EnrichedShow? show) =>
        show is { Found: true } ? Join([show.Network, Years(show.Premiered, show.Ended), show.Status]) : "";

    /// <summary>
    /// The years a show ran, from the catalogue's ISO dates: "2011\u20132019" (an en dash), or one year when it began
    /// and ended in the same one or has not ended; null when the year it began is unknown.
    /// </summary>
    public static string? Years(string? premiered, string? ended)
    {
        if (YearOf(premiered) is not { } from)
        {
            return null;
        }

        var start = from.ToString(CultureInfo.InvariantCulture);
        return YearOf(ended) is { } to && to != from ? start + "\u2013" + to.ToString(CultureInfo.InvariantCulture) : start;
    }

    /// <summary>The year an ISO date ("2011-04-17") starts with, or null for one that does not.</summary>
    public static int? YearOf(string? date) =>
        date is { Length: >= 4 } && int.TryParse(date.AsSpan(0, 4), NumberStyles.None, CultureInfo.InvariantCulture, out var year) ? year : null;

    /// <summary>
    /// Who made a film, in words: "Directed by Lana Wachowski and Lilly Wachowski", "Written by A, B
    /// and C"; empty when nobody is known. Names repeated or blank are left out.
    /// </summary>
    public static string People(string lead, IReadOnlyList<string> names)
    {
        ArgumentNullException.ThrowIfNull(lead);
        ArgumentNullException.ThrowIfNull(names);
        var known = names.Where(name => !string.IsNullOrWhiteSpace(name)).Select(name => name.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return known switch
        {
            [] => "",
            [var one] => lead + " " + one,
            _ => lead + " " + string.Join(", ", known[..^1]) + " and " + known[^1],
        };
    }

    /// <summary>A movie's directors and writers on one line: "Directed by A · Written by B"; empty when neither is known.</summary>
    public static string Crew(EnrichedMovie? facts) =>
        facts is null ? "" : Join([People("Directed by", facts.Directors), People("Written by", facts.Writers)]);

    /// <summary>The facts given, in order, without the missing ones.</summary>
    public static string Join(IEnumerable<string?> parts)
    {
        ArgumentNullException.ThrowIfNull(parts);
        return string.Join(Separator, parts.Where(part => !string.IsNullOrWhiteSpace(part)));
    }
}
