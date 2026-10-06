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
        ".ts", ".m2ts", ".mpg", ".avi", ".wma", ".wmv", ".ac3", ".iso", ".sup", ".sub", ".bin", ".mov", ".m4v",
        ".png", ".jpg", ".jpeg", ".gif", ".webp", ".ico", ".ttf", ".otf", ".woff", ".woff2", ".zip",
        ".nupkg", ".dll", ".exe",
    };

    /// <summary>
    /// Whether a file is checked as text. Binary media is checked differently (see
    /// <see cref="FindInBinary"/>), and the npm lockfile's base64 integrity hashes are random text
    /// that would eventually spell anything.
    /// </summary>
    internal static bool IsCheckedText(string path) =>
        !IsBinary(path)
        && !string.Equals(Path.GetFileName(path), "package-lock.json", StringComparison.OrdinalIgnoreCase);

    internal static bool IsBinary(string path) => BinaryExtensions.Contains(Path.GetExtension(path));

    /// <summary>
    /// The banned words inside binary media. Encoders write their names and web addresses into the
    /// files they make, so media can carry the words too. Only the long word is sought: compressed
    /// bytes spell any given three letters by chance every few tens of kilobytes, but never eight.
    /// </summary>
    internal static IEnumerable<string> FindInBinary(ReadOnlySpan<byte> bytes) =>
        Find(System.Text.Encoding.Latin1.GetString(bytes)).Where(word => word.Length >= 8).ToList();

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
