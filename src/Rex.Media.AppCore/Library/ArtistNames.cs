using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Rex.Media.Library;

namespace Rex.Media.AppCore.Library;

/// <summary>
/// The artists a song is by, read from its tags the way people write credits. Tag formats carry
/// several artists as separate values (read as "; "), and taggers also write them into one string:
/// "beabadoobee/The Marías", "Drake feat. Rihanna", "Asake x Olamide", "Burna Boy &amp; Wizkid",
/// "Song (feat. Asake)" in a title. Separators that never occur inside a name ("/", ";", "feat.",
/// "ft.", "featuring" and a lowercase " x ") always split, except inside a name on the built-in
/// list ("AC/DC"). Separators that do occur inside names ("&amp;", "and", ",") split only when every
/// piece is an artist the library already knows on its own, so "Simon &amp; Garfunkel" and
/// "Of Monsters and Men" stay whole while "Asake &amp; Olamide" splits once both have songs of their own.
/// Names are matched by <see cref="Key"/>, which ignores case, accents and punctuation.
/// </summary>
public sealed partial class ArtistNames
{
    // One artist each, though they hold a separator. Both "&" and "and" spellings are matched.
    private static readonly string[] OneArtist =
    [
        "AC/DC", "Au/Ra", "Tyler, The Creator", "Earth, Wind & Fire", "Simon & Garfunkel", "Florence + The Machine",
        "Crosby, Stills, Nash & Young", "Crosby, Stills & Nash", "Mumford & Sons", "Of Monsters and Men", "Hall & Oates",
        "Daryl Hall & John Oates", "Kool & the Gang", "Above & Beyond", "Bob Marley & The Wailers", "Nick Cave & the Bad Seeds",
        "Prince & The Revolution", "Sly & the Family Stone", "Echo & the Bunnymen", "Iron & Wine", "Angus & Julia Stone",
        "Chase & Status", "Matt and Kim", "Marina and the Diamonds", "Hootie & the Blowfish", "Huey Lewis & the News",
        "Tom Petty and the Heartbreakers", "Peter, Paul and Mary", "Emerson, Lake & Palmer", "Blood, Sweat & Tears",
        "Brooks & Dunn", "Sam & Dave", "Ike & Tina Turner", "Sonny & Cher", "Belle and Sebastian", "Years & Years",
        "Smokey Robinson & the Miracles", "Martha and the Vandellas", "Diana Ross & the Supremes", "Gladys Knight & the Pips",
        "KC and the Sunshine Band", "Derek and the Dominos", "Of Mice & Men", "Coheed and Cambria", "Love and Rockets",
        "Dan + Shay", "Me First and the Gimme Gimmes", "Bruce Springsteen & The E Street Band", "Captain & Tennille",
    ];

    // The names in both spellings, longest first so "Crosby, Stills, Nash & Young" is found before
    // "Crosby, Stills & Nash", each with its key, which a text's key must hold for the name to be in it.
    private static readonly (string Key, string[] Spellings)[] Masks = [.. OneArtist
        .Select(name => (Key: Key(name), Spellings: new[] { name, name.Replace(" & ", " and ", StringComparison.Ordinal), name.Replace(" and ", " & ", StringComparison.Ordinal) }.Distinct(StringComparer.Ordinal).ToArray()))
        .OrderByDescending(mask => mask.Key.Length)];

    // Every built-in name holds one of these or " and ", so most credits need no masking at all.
    private static readonly char[] MaskMarks = ['/', ',', '&', '+'];

    // A masked name stands in the text as one private-use character, its index into the held names.
    private const char FirstHeld = (char)0xE000;
    private const char LastHeld = (char)0xF8FF;
    private const CompareOptions Loose = CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace;
    private static readonly CompareInfo Compare = CultureInfo.InvariantCulture.CompareInfo;

    private readonly HashSet<string> _known;

    // A library repeats its credits ("Various Artists", an album's artist on every track), so each
    // credit is taken apart and each name keyed once. An instance belongs to the one query using it.
    private readonly Dictionary<string, IReadOnlyList<string>> _splits = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _keys = new(StringComparer.Ordinal);

    private ArtistNames(HashSet<string> known) => _known = known;

    /// <summary>
    /// What <paramref name="songs"/> teach about who is an artist on their own: every name a credit
    /// holds once the separators that always split are taken apart.
    /// </summary>
    public static ArtistNames Learn(IEnumerable<LibraryEntry> songs)
    {
        ArgumentNullException.ThrowIfNull(songs);
        var known = new HashSet<string>(StringComparer.Ordinal);
        var credits = new HashSet<string>(StringComparer.Ordinal);
        foreach (var song in songs)
        {
            foreach (var credit in new[] { song.AlbumArtist, song.Artist, song.Artists }.Concat(Featured(song.Title)).OfType<string>().Where(credits.Add))
            {
                var held = new List<string>();
                foreach (var piece in Pieces(credit, held))
                {
                    known.Add(Key(Restore(piece, held)));
                }
            }
        }

        return new ArtistNames(known);
    }

    /// <summary>
    /// Everyone <paramref name="song"/> is by, each once: its album's artist, its own artists (the
    /// tag's list of artists when it has one, else its artist) and those its title features.
    /// </summary>
    public IReadOnlyList<string> Of(LibraryEntry song)
    {
        ArgumentNullException.ThrowIfNull(song);
        var names = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var credit in new[] { song.AlbumArtist, song.Artists ?? song.Artist }.Concat(Featured(song.Title)))
        {
            names.AddRange(Split(credit).Where(name => seen.Add(KeyOf(name))));
        }

        return names;
    }

    /// <summary>The artists one credit names, each once and in the order written; empty when it names none.</summary>
    public IReadOnlyList<string> Split(string? credit)
    {
        if (credit is null)
        {
            return [];
        }

        if (_splits.TryGetValue(credit, out var known))
        {
            return known;
        }

        var held = new List<string>();
        var names = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var piece in Pieces(credit, held))
        {
            names.AddRange(Apart(piece, held).Where(name => seen.Add(KeyOf(name))));
        }

        _splits[credit] = names;
        return names;
    }

    /// <summary><see cref="Key"/>, worked out once per name.</summary>
    public string KeyOf(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (!_keys.TryGetValue(name, out var key))
        {
            _keys[name] = key = Key(name);
        }

        return key;
    }

    /// <summary>
    /// The artists a title features: "Sungba (feat. Burna Boy)", "Song [ft. A &amp; B]" or a trailing
    /// "Song feat. A". The title itself is shown as tagged; these only add artists.
    /// </summary>
    public static IReadOnlyList<string> Featured(string? title)
    {
        // Most titles feature nobody; only one that could is searched.
        if (title is null || (!title.Contains("ft", StringComparison.OrdinalIgnoreCase) && !title.Contains("feat", StringComparison.OrdinalIgnoreCase)))
        {
            return [];
        }

        return [.. BracketedFeature().Matches(title).Concat(TrailingFeature().Matches(title))
            .Select(match => match.Groups["who"].Value.Trim())];
    }

    /// <summary>
    /// The name an artist is matched by: letters and digits only, lower case, without accents, with
    /// "&amp;" and "+" read as "and" and "$" as "s". "The Marías" and "the marias" agree, as do
    /// "A$AP Rocky" and "ASAP Rocky". A name with no letter or digit ("!!!") is matched as written.
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
            if (c == '$')
            {
                key.Append('s');
            }
            else if (c is '&' or '+')
            {
                key.Append("and");
            }
            else if (char.IsLetterOrDigit(c))
            {
                key.Append(char.ToLowerInvariant(c));
            }
        }

        return key.Length > 0 ? key.ToString() : name.Trim().ToLowerInvariant();
    }

    /// <summary>
    /// A song's artist line: the credit as tagged, with separate values and "/" between artists
    /// shown as a list ("beabadoobee/The Marías" reads "beabadoobee, The Marías"); null for none.
    /// </summary>
    public static string? Display(string? credit)
    {
        if (credit is null)
        {
            return null;
        }

        var held = new List<string>();
        var names = Listed().Split(Mask(credit, held))
            .Select(part => Restore(part, held).Trim())
            .Where(part => part.Length > 0)
            .DistinctBy(Key)
            .ToList();
        return names.Count == 0 ? credit : string.Join(", ", names);
    }

    /// <summary>A credit taken apart at the separators that always split, the built-in names masked.</summary>
    private static IEnumerable<string> Pieces(string? credit, List<string> held) =>
        string.IsNullOrWhiteSpace(credit) ? [] : Always().Split(Mask(credit, held)).Select(Clean).Where(piece => piece.Length > 0);

    /// <summary>One piece taken apart at "&amp;", "and" and "," when every part is an artist known on their own.</summary>
    private IEnumerable<string> Apart(string piece, List<string> held)
    {
        var parts = Sometimes().Split(piece);
        if (parts.Length > 1 && parts.All(part => IsHeld(part) || (part.Length > 0 && !Suffix().IsMatch(part) && _known.Contains(KeyOf(Restore(part, held))))))
        {
            return parts.Select(part => Restore(part, held));
        }

        return [Restore(piece, held)];
    }

    private static bool IsHeld(string part) => part.Length == 1 && part[0] is >= FirstHeld and <= LastHeld;

    /// <summary>A piece without the spaces or stray commas around it, or a bracket left over from "(feat. A)".</summary>
    private static string Clean(string piece)
    {
        piece = piece.Trim().Trim(',').Trim();
        return piece.IndexOfAny(['(', '[']) < 0 ? piece.TrimEnd(')', ']', ' ') : piece;
    }

    /// <summary>
    /// The text with every built-in name that stands on its own (at the start or end, or next to a
    /// separator) replaced by one held character, so no separator inside it splits it.
    /// </summary>
    private static string Mask(string text, List<string> held)
    {
        if (text.Any(c => c is >= FirstHeld and <= LastHeld))
        {
            // Text that already holds such a character is left alone rather than misread.
            return text;
        }

        if (text.IndexOfAny(MaskMarks) < 0 && !text.Contains(" and ", StringComparison.OrdinalIgnoreCase))
        {
            return text;
        }

        var textKey = Key(text);
        foreach (var name in Masks.Where(mask => textKey.Contains(mask.Key, StringComparison.Ordinal)).SelectMany(mask => mask.Spellings))
        {
            for (var start = 0; start < text.Length;)
            {
                var at = Compare.IndexOf(text.AsSpan(start), name, Loose, out var length);
                if (at < 0)
                {
                    break;
                }

                at += start;
                start = at + 1;
                if (StandsBefore().IsMatch(text[..at]) && StandsAfter().IsMatch(text[(at + length)..]))
                {
                    held.Add(text.Substring(at, length));
                    text = text[..at] + (char)(FirstHeld + held.Count - 1) + text[(at + length)..];
                }
            }
        }

        return text;
    }

    private static string Restore(string text, List<string> held)
    {
        var restored = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            if (c is >= FirstHeld and <= LastHeld && c - FirstHeld < held.Count)
            {
                restored.Append(held[c - FirstHeld]);
            }
            else
            {
                restored.Append(c);
            }
        }

        return restored.ToString();
    }

    [GeneratedRegex(@"\s*[;/\x00]\s*|\s+(?:(?:feat|ft)\.\s*|(?:feat|ft|featuring)\s+|(?-i:x)\s+)|\s*[(\[]\s*(?:(?:feat|ft)\.\s*|(?:feat|ft|featuring)\s+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Always();

    [GeneratedRegex(@"\s*,\s*|\s+(?:&|and)\s+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Sometimes();

    [GeneratedRegex(@"\s*[;/\x00]\s*")]
    private static partial Regex Listed();

    // "Harry Connick, Jr." is one person: a comma before a suffix never splits.
    [GeneratedRegex(@"^(?:jr|sr|ii|iii|iv|inc|esq)\.?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Suffix();

    [GeneratedRegex(@"(?:^|[/;,&+(\[\x00]|\s(?:and|x|feat\.?|ft\.?|featuring|vs\.?))\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex StandsBefore();

    [GeneratedRegex(@"^\s*(?:$|[/;,&+)\]\x00]|(?:feat|ft)\.\s*|(?:and|x|feat|ft|featuring|vs\.?)\s)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex StandsAfter();

    [GeneratedRegex(@"[(\[]\s*(?:(?:feat|ft)\.\s*|(?:feat|ft|featuring)\s+)(?<who>[^)\]]+)[)\]]", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BracketedFeature();

    [GeneratedRegex(@"\s(?:(?:feat|ft)\.\s*|(?:feat|ft|featuring)\s+)(?<who>[^()\[\]]+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TrailingFeature();
}
