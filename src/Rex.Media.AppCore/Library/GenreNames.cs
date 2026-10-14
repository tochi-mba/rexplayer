using System.Text;
using Rex.Media.Containers.Tags;

namespace Rex.Media.AppCore.Library;

/// <summary>
/// The genres one genre tag names. Taggers put several genres in one field in different ways:
/// ID3v2.4 separates values with a NUL (read as "; "), others write "Dance;Pop", "Electro;Techno/House",
/// "Rap/Hip Hop", "Pop, Rock" or numbered ID3v1 genres such as "(13)". Each genre is spelled the way
/// it is usually spelled ("Hip Hop", "HipHop" and "hip-hop" are all Hip-Hop; "RnB" and "Rhythm and
/// Blues" are R&amp;B), so one genre does not appear as several groups in the library.
/// </summary>
public static class GenreNames
{
    // Always a separator between genres: no genre name contains one.
    private static readonly char[] Separators = [';', '|', '\0', '\\'];

    // Separators only when every piece is a known genre: "Rap/Hip Hop" is two genres, while
    // "AC/DC Tribute" or "Folk, World, & Country" is one name that happens to hold the character.
    private static readonly char[] Maybe = ['/', ','];

    // Common genres beyond the numbered ID3v1 list, as they are usually spelled.
    private static readonly string[] Modern =
    [
        "Afrobeat", "Afrobeats", "Afropop", "Afro House", "Alternative R&B", "Amapiano", "Chill", "Christmas",
        "Dancehall", "Drill", "Drum & Bass", "Dubstep", "EDM", "Electro", "Emo", "Folk", "Garage", "Grime",
        "Highlife", "Indie", "Indie Pop", "Indie Rock", "J-Pop", "K-Pop", "Latin", "Metalcore", "Neo Soul",
        "Opera", "Pop Rock", "Reggaeton", "Singer-Songwriter", "Synth-Pop", "Trap", "UK Garage", "World",
    ];

    // Other spellings of the same genre, by key.
    private static readonly Dictionary<string, string> Aliases = new(StringComparer.Ordinal)
    {
        ["rnb"] = "R&B",
        ["rhythmandblues"] = "R&B",
        ["electronica"] = "Electronic",
        ["alternativo"] = "Alternative",
        ["alternativa"] = "Alternative",
        ["latino"] = "Latin",
        ["latina"] = "Latin",
        ["singerandsongwriter"] = "Singer-Songwriter",
        ["drumnbass"] = "Drum & Bass",
        ["dnb"] = "Drum & Bass",
    };

    private static readonly Dictionary<string, string> Known = BuildKnown();

    /// <summary>
    /// The genres in <paramref name="field"/>, each once and in the order written; empty when the
    /// field names none.
    /// </summary>
    public static IReadOnlyList<string> Split(string? field)
    {
        var genres = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var part in (field ?? "").Split(Separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            foreach (var genre in Pieces(Id3v1.ExpandGenre(part)))
            {
                if (seen.Add(Key(genre)))
                {
                    genres.Add(genre);
                }
            }
        }

        return genres;
    }

    /// <summary>The first genre <paramref name="field"/> names, or null when it names none.</summary>
    public static string? Primary(string? field) => Split(field) is [var first, ..] ? first : null;

    private static IEnumerable<string> Pieces(string part)
    {
        // A whole known name wins before any splitting: "Singer/Songwriter" is one genre. A comma
        // never joins one, so "Pop, Rock" is not read as Pop Rock.
        if (!part.Contains(',', StringComparison.Ordinal) && Known.TryGetValue(Key(part), out var whole))
        {
            return [whole];
        }

        var pieces = part.Split(Maybe, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (pieces.Length > 1 && pieces.All(piece => Known.ContainsKey(Key(piece))))
        {
            return pieces.Select(piece => Known[Key(piece)]);
        }

        return [Tidy(part)];
    }

    /// <summary>Letters and digits only, lower case, with "&amp;" read as "and": "R&amp;B", "R and B" and "r-and-b" agree.</summary>
    private static string Key(string name)
    {
        var key = new StringBuilder(name.Length);
        foreach (var c in name.Replace("&", "and", StringComparison.Ordinal))
        {
            if (char.IsLetterOrDigit(c))
            {
                key.Append(char.ToLowerInvariant(c));
            }
        }

        return key.ToString();
    }

    /// <summary>An unknown genre as written, with runs of spaces closed up.</summary>
    private static string Tidy(string name) => string.Join(' ', name.Split(' ', StringSplitOptions.RemoveEmptyEntries));

    private static Dictionary<string, string> BuildKnown()
    {
        var known = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var name in Id3v1.GenreNames.Concat(Modern))
        {
            known.TryAdd(Key(name), name);
        }

        foreach (var (key, name) in Aliases)
        {
            known[key] = name;
        }

        return known;
    }
}
