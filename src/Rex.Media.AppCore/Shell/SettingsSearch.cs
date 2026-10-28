using System.Globalization;

namespace Rex.Media.AppCore.Shell;

/// <summary>
/// Finding a setting in Preferences (UI-10): what a search matches, and which section it shows. A
/// section whose name matches outranks one that only mentions the words, so "subtitle" opens
/// Subtitles rather than an earlier section with a subtitle option; otherwise the section on show
/// stays while it still matches, and the first match is shown when it does not.
/// </summary>
public static class SettingsSearch
{
    private static readonly CompareInfo Comparer = CultureInfo.InvariantCulture.CompareInfo;

    /// <summary>Whether <paramref name="text"/> contains <paramref name="query"/>, whatever their case and accents.</summary>
    public static bool Says(string? text, string query)
    {
        ArgumentNullException.ThrowIfNull(query);
        return text is not null && Comparer.IndexOf(text, query, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0;
    }

    /// <summary>
    /// The section to show for <paramref name="query"/>, given the <paramref name="sections"/> in
    /// order, those <paramref name="matching"/> it, and the one <paramref name="shown"/> now: the
    /// one shown for an empty query, and null when nothing matches.
    /// </summary>
    public static string? Section(IReadOnlyList<string> sections, IReadOnlyCollection<string> matching, string shown, string query)
    {
        ArgumentNullException.ThrowIfNull(sections);
        ArgumentNullException.ThrowIfNull(matching);
        ArgumentNullException.ThrowIfNull(query);
        if (query.Trim().Length == 0)
        {
            return shown;
        }

        var trimmed = query.Trim();
        return sections.FirstOrDefault(name => matching.Contains(name) && Says(name, trimmed))
            ?? (matching.Contains(shown) ? shown : sections.FirstOrDefault(matching.Contains));
    }
}
