using System.Text;

namespace Rex.Media.AppCore.Library;

/// <summary>
/// Local show notes are plain, selectable speech companions, never executable markup. Only
/// explicit sidecars are considered; no feed fetches, remote links or HTML rendering occur.
/// </summary>
public static class PodcastNotes
{
    public const int MaxVisibleCharacters = 16_384;

    /// <summary>Explicit local Markdown/plain-text companions, most specific first.</summary>
    public static IReadOnlyList<string> Sidecars(string episodePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(episodePath);
        return [Path.ChangeExtension(episodePath, ".notes.md")!,
            Path.ChangeExtension(episodePath, ".notes.txt")!];
    }

    /// <summary>
    /// Normalizes plain text for long keyboard-readable notes while preserving Unicode and newlines.
    /// Invalid control characters cannot alter dialog behavior; any markup remains inert text.
    /// </summary>
    public static string Display(string source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var length = Math.Min(source.Length, MaxVisibleCharacters);
        var result = new StringBuilder(length + 40);
        for (var index = 0; index < length; index++)
        {
            var ch = source[index];
            if (ch == '\r')
            {
                if (index + 1 < length && source[index + 1] == '\n')
                {
                    index++;
                }

                result.Append('\n');
            }
            else if (ch == '\n' || ch == '\t' || !char.IsControl(ch))
            {
                result.Append(ch);
            }
        }

        if (source.Length > MaxVisibleCharacters)
        {
            result.Append("\n\n… Notes truncated for display.");
        }

        return result.ToString();
    }
}
