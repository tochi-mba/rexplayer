using System.Globalization;
using System.Text;

namespace Rex.Media.AppCore.Metadata;

/// <summary>
/// Matching a show's name from the disk to a catalogue's candidates, carefully: an exact name (and
/// the year, when the file names one) is taken, a near-exact name is taken when it is the only
/// reading, and anything less is left unmatched - a card with no description is better than the
/// wrong show's, and the user can always pick by hand with "Wrong match...".
/// </summary>
public static class MetadataMatch
{
    /// <summary>A name reduced to what identifies it: case-folded letters, digits and single spaces, without a leading "the".</summary>
    public static string Normalize(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        var kept = new StringBuilder(name.Length);
        foreach (var letter in name)
        {
            if (char.IsLetterOrDigit(letter))
            {
                kept.Append(char.ToLowerInvariant(letter));
            }
            else if (kept.Length > 0 && kept[^1] != ' ')
            {
                kept.Append(' ');
            }
        }

        var flat = kept.ToString().Trim();
        return flat.StartsWith("the ", StringComparison.Ordinal) ? flat[4..] : flat;
    }

    /// <summary>
    /// The candidate <paramref name="wanted"/> names, or null when none can be taken with
    /// confidence. A year from the file ("Show (2019)") must agree when the candidate has one.
    /// </summary>
    public static ShowCandidate? Pick(string wanted, int? year, IReadOnlyList<ShowCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        var name = Normalize(wanted);
        if (name.Length == 0)
        {
            return null;
        }

        var sameName = candidates.Where(candidate => Normalize(candidate.Name) == name).ToList();
        var sameYear = year is null ? sameName : [.. sameName.Where(candidate => candidate.Year is null || candidate.Year == year)];
        if (sameYear.Count > 0)
        {
            // Exact names, best (first) first; with a year, the dated candidate beats an undated one.
            return sameYear.OrderBy(candidate => year is not null && candidate.Year is null).First();
        }

        // Near-exact: one candidate whose name only adds a year or a subtitle mark ("Show (2019)",
        // "Show 2019") is safe; two such readings are not.
        var near = candidates.Where(candidate => Normalize(candidate.Name) is { } offered
            && (offered.StartsWith(name + " ", StringComparison.Ordinal) || name.StartsWith(offered + " ", StringComparison.Ordinal))
            && (year is null || candidate.Year is null || candidate.Year == year)).ToList();
        return near.Count == 1 ? near[0] : null;
    }

    /// <summary>One key for a movie wherever it is named: the title and the year, normalized.</summary>
    public static string MovieKey(string title, int? year) =>
        Normalize(title ?? throw new ArgumentNullException(nameof(title)))
            + (year is { } dated ? " " + dated.ToString(CultureInfo.InvariantCulture) : "");

    /// <summary>How a candidate reads in a list to pick from: its name, year and the start of its summary.</summary>
    public static string Describe(ShowCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        var name = candidate.Year is { } year ? $"{candidate.Name} ({year.ToString(CultureInfo.InvariantCulture)})" : candidate.Name;
        var summary = candidate.Summary ?? "";
        return summary.Length switch
        {
            0 => name,
            <= 120 => name + " · " + summary,
            _ => name + " · " + summary[..119].TrimEnd() + "…",
        };
    }
}
