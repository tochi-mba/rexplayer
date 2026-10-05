namespace Rex.Media.Tests.Repository;

/// <summary>
/// Words this repository never contains. They are assembled from character codes so the file that
/// enforces the rule passes the rule itself, and so a search of the repository finds them nowhere.
/// </summary>
internal static class Lexicon
{
    internal static readonly string[] Banned =
    [
        Word(118, 108, 99),
        Word(118, 105, 100, 101, 111, 108, 97, 110),
    ];

    private static readonly HashSet<string> BinaryExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".wav", ".aif", ".aiff", ".mp3", ".mp4", ".m4a", ".mkv", ".webm", ".flac", ".ogg", ".opus",
        ".ts", ".m2ts", ".mpg", ".avi", ".wma", ".wmv", ".ac3", ".iso", ".sup", ".sub", ".bin",
        ".png", ".jpg", ".jpeg", ".gif", ".webp", ".ico", ".ttf", ".otf", ".woff", ".woff2", ".zip",
        ".nupkg", ".dll", ".exe",
    };

    /// <summary>
    /// Whether a file's text is checked. Binary media cannot carry prose, and the npm lockfile's
    /// base64 integrity hashes are random text that would eventually spell anything.
    /// </summary>
    internal static bool IsCheckedText(string path) =>
        !BinaryExtensions.Contains(Path.GetExtension(path))
        && !string.Equals(Path.GetFileName(path), "package-lock.json", StringComparison.OrdinalIgnoreCase);

    /// <summary>Every banned word found in <paramref name="text"/>, in any casing, inside identifiers too.</summary>
    internal static IEnumerable<string> Find(string text)
    {
        foreach (var word in Banned)
        {
            if (text.Contains(word, StringComparison.OrdinalIgnoreCase))
            {
                yield return word;
            }
        }
    }

    private static string Word(params int[] codes) => new(codes.Select(code => (char)code).ToArray());
}
