namespace Rex.Media.Primitives;

/// <summary>
/// Languages as media and people write them: two-letter codes ("en"), three-letter ones in either
/// form ("fra" or "fre"), tags with a region ("en-US"), and English names ("English"). Used to pick
/// the audio and subtitle tracks a user prefers (AU-21).
/// </summary>
public static class Languages
{
    /// <summary>In a preference list, the language the media was made in: that of its first track that is not a commentary.</summary>
    public const string Original = "original";

    // Two-letter code, three-letter code (terminology form), the other three-letter form if there is one, English name.
    private static readonly (string Two, string Three, string? Other, string Name)[] Known =
    [
        ("ar", "ara", null, "Arabic"), ("bg", "bul", null, "Bulgarian"), ("bn", "ben", null, "Bengali"),
        ("ca", "cat", null, "Catalan"), ("cs", "ces", "cze", "Czech"), ("cy", "cym", "wel", "Welsh"),
        ("da", "dan", null, "Danish"), ("de", "deu", "ger", "German"), ("el", "ell", "gre", "Greek"),
        ("en", "eng", null, "English"), ("es", "spa", null, "Spanish"), ("et", "est", null, "Estonian"),
        ("eu", "eus", "baq", "Basque"), ("fa", "fas", "per", "Persian"), ("fi", "fin", null, "Finnish"),
        ("fil", "fil", null, "Filipino"), ("fr", "fra", "fre", "French"), ("ga", "gle", null, "Irish"),
        ("gl", "glg", null, "Galician"), ("ha", "hau", null, "Hausa"), ("he", "heb", null, "Hebrew"),
        ("hi", "hin", null, "Hindi"), ("hr", "hrv", null, "Croatian"), ("hu", "hun", null, "Hungarian"),
        ("id", "ind", null, "Indonesian"), ("ig", "ibo", null, "Igbo"), ("is", "isl", "ice", "Icelandic"),
        ("it", "ita", null, "Italian"), ("ja", "jpn", null, "Japanese"), ("ko", "kor", null, "Korean"),
        ("lt", "lit", null, "Lithuanian"), ("lv", "lav", null, "Latvian"), ("mk", "mkd", "mac", "Macedonian"),
        ("ms", "msa", "may", "Malay"), ("nl", "nld", "dut", "Dutch"), ("no", "nor", null, "Norwegian"),
        ("nb", "nob", null, "Norwegian Bokmal"), ("pl", "pol", null, "Polish"), ("pt", "por", null, "Portuguese"),
        ("ro", "ron", "rum", "Romanian"), ("ru", "rus", null, "Russian"), ("sk", "slk", "slo", "Slovak"),
        ("sl", "slv", null, "Slovenian"), ("sq", "sqi", "alb", "Albanian"), ("sr", "srp", null, "Serbian"),
        ("sv", "swe", null, "Swedish"), ("sw", "swa", null, "Swahili"), ("ta", "tam", null, "Tamil"),
        ("te", "tel", null, "Telugu"), ("th", "tha", null, "Thai"), ("tr", "tur", null, "Turkish"),
        ("uk", "ukr", null, "Ukrainian"), ("ur", "urd", null, "Urdu"), ("vi", "vie", null, "Vietnamese"),
        ("yo", "yor", null, "Yoruba"), ("zh", "zho", "chi", "Chinese"), ("zu", "zul", null, "Zulu"),
    ];

    private static readonly Dictionary<string, string> Codes = BuildCodes();

    private static Dictionary<string, string> BuildCodes()
    {
        var codes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (two, three, other, name) in Known)
        {
            codes[two] = three;
            codes[three] = three;
            codes[name] = three;
            if (other is not null)
            {
                codes[other] = three;
            }
        }

        return codes;
    }

    /// <summary>
    /// The three-letter code for <paramref name="language"/>; a code not in the table, lower-cased
    /// and without its region; or null for nothing, or for the codes that name no one language.
    /// </summary>
    public static string? Canonical(string? language)
    {
        if (string.IsNullOrWhiteSpace(language))
        {
            return null;
        }

        var text = language.Trim();
        var primary = text.Split('-', '_')[0];
        if (Codes.TryGetValue(text, out var code) || Codes.TryGetValue(primary, out code))
        {
            return code;
        }

        var lower = primary.ToLowerInvariant();
        return lower is "und" or "zxx" or "mis" or "mul" ? null : lower;
    }

    /// <summary>Whether two ways of writing a language name the same one; never for an unknown language.</summary>
    public static bool Same(string? first, string? second) => Canonical(first) is { } code && code == Canonical(second);

    /// <summary>The languages in a list written by a person, such as "en, fr" or "Japanese; original".</summary>
    public static IReadOnlyList<string> ParseList(string? list) =>
        string.IsNullOrWhiteSpace(list) ? [] : list.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>Whether a track's title says it is a commentary rather than the programme itself.</summary>
    public static bool IsCommentary(string? title) => title?.Contains("commentary", StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>
    /// The track that best suits <paramref name="preferences"/>, taken in order, or null when none
    /// is in any of those languages. Among tracks in the wanted language a commentary comes last
    /// and a default track first. <see cref="Original"/> stands for <paramref name="original"/>.
    /// </summary>
    public static T? Choose<T>(IReadOnlyList<T> tracks, IReadOnlyList<string> preferences, Func<T, string?> language, Func<T, string?> title, Func<T, bool> isDefault, string? original = null)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(tracks);
        ArgumentNullException.ThrowIfNull(preferences);
        foreach (var wanted in preferences)
        {
            var target = string.Equals(wanted, Original, StringComparison.OrdinalIgnoreCase) ? original : wanted;
            var best = tracks
                .Where(track => Same(language(track), target))
                .OrderBy(track => IsCommentary(title(track)))
                .ThenBy(track => !isDefault(track))
                .FirstOrDefault();
            if (best is not null)
            {
                return best;
            }
        }

        return null;
    }
}
