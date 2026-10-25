using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Rex.Media.AppCore.Library;

/// <summary>
/// How the TV shows view tells show names apart. Two names are one show when they agree in their
/// words, whatever the case, accents or punctuation: "Pokémon", "pokemon" and "Pokemon." agree, as do
/// "Marvel's Agents" and "Marvels Agents". The words are kept apart (unlike an artist's key, which
/// squeezes them together), because the safety net below compares whole words: "Doctor Who" is a
/// prefix of "Doctor Who Confidential" by words, never of "Doctor Whom".
/// </summary>
public static partial class ShowNames
{
    /// <summary>
    /// The words of <paramref name="name"/>, lower case and without accents, one space between them;
    /// "&amp;" and "+" read "and", an apostrophe joins ("Ocean's" is "oceans"). A name with no letter or
    /// digit is matched as written.
    /// </summary>
    public static string Key(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        string decomposed;
        try
        {
            decomposed = name.Normalize(NormalizationForm.FormD);
        }
        catch (ArgumentException)
        {
            // Text with a broken character cannot be decomposed; it is matched without dropping accents.
            decomposed = name;
        }

        var key = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            if (char.IsLetterOrDigit(c))
            {
                key.Append(char.ToLowerInvariant(c));
            }
            else if (c is '&' or '+')
            {
                key.Append(" and ");
            }
            else if (c is not ('\'' or '\u2019') && CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                key.Append(' ');
            }
        }

        var words = key.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return words.Length > 0 ? string.Join(' ', words) : name.Trim().ToLowerInvariant();
    }

    /// <summary>
    /// Whether the show keyed <paramref name="candidate"/> is the show keyed <paramref name="host"/> with a
    /// season or episode mark after its name ("futurama s07x01 simpsorama" extends "futurama"), so a lone
    /// episode whose mark was not read as one still goes with its show. "Doctor Who Confidential" does not
    /// extend "Doctor Who": "confidential" is no mark. Both are <see cref="Key"/>s.
    /// </summary>
    public static bool Extends(string candidate, string host)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(host);
        if (host.Length == 0 || candidate.Length <= host.Length + 1 || !candidate.StartsWith(host + " ", StringComparison.Ordinal))
        {
            return false;
        }

        var next = candidate[(host.Length + 1)..];
        var space = next.IndexOf(' ', StringComparison.Ordinal);
        return MarkWordPattern().IsMatch(space < 0 ? next : next[..space]);
    }

    /// <summary>
    /// A word that starts a season or episode mark: "s07", "s07e01", "s07x01", "s07sp01", "7x01", "e05",
    /// "ep05", "season", "series", "episode", "special(s)", "sp01", "ova", "oad", "extras", "featurette(s)".
    /// </summary>
    [GeneratedRegex(@"^(?:s\d{1,3}(?:(?:[ex]|sp)\d{1,4})*|\d{1,2}x\d{1,3}|e\d{1,4}|ep\d{0,4}|season|series|staffel|saison|temporada|episodes?|specials?|sp\d{0,3}|ova|oad|extras?|featurettes?)$", RegexOptions.CultureInvariant)]
    private static partial Regex MarkWordPattern();
}
