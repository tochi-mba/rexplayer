using System.Globalization;
using Rex.Media.Library;

namespace Rex.Media.AppCore.Library;

/// <summary>
/// The library's searchable words, prepared once per library snapshot so a keystroke is a quick
/// pass over ready strings. Each entry's title, artist, album artist, album, genre and file name
/// are joined into one line (a line break between fields, so a word never matches across two),
/// and a line of plain printable ASCII also keeps its lowercase twin. A word is found the way the
/// library always found it: whatever the case, accents, kana type or width, by the current
/// culture. When both the word and the line are plain ASCII that is exactly an ordinal search of
/// the lowercase twins, which is taken instead; everything else goes to the culture's comparison.
/// </summary>
public sealed class LibrarySearchIndex
{
    private const CompareOptions Loose = CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace | CompareOptions.IgnoreKanaType | CompareOptions.IgnoreWidth;
    private static readonly CompareInfo Compare = CultureInfo.CurrentCulture.CompareInfo;
    // A culture where "i" and "I" differ ignoring case (Turkish) is not the ordinal lowercase, so it never takes the quick path.
    private static readonly bool AsciiIsOrdinal = Compare.Compare("i", "I", CompareOptions.IgnoreCase) == 0;

    private readonly IReadOnlyList<LibraryEntry> _entries;
    private readonly string[] _lines;
    private readonly string?[] _lower;

    private LibrarySearchIndex(IReadOnlyList<LibraryEntry> entries)
    {
        _entries = entries;
        _lines = new string[entries.Count];
        _lower = new string?[entries.Count];
        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            _lines[i] = string.Join('\n', new[] { entry.Title, entry.Artist, entry.AlbumArtist, entry.Album, entry.Genre, Path.GetFileName(entry.Path) }.OfType<string>());
            _lower[i] = Lower(_lines[i]);
        }
    }

    /// <summary>The index of <paramref name="entries"/>, made the first time it is asked for and kept beside the snapshot.</summary>
    public static LibrarySearchIndex For(IReadOnlyList<LibraryEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        return LibraryMemo.Of(entries, nameof(LibrarySearchIndex), () => new LibrarySearchIndex(entries));
    }

    /// <summary>
    /// The entries every word of <paramref name="text"/> is found in, in their own fields or in
    /// <paramref name="extraText"/> (asked for only when the fields miss), music first then videos
    /// then pictures, each in its view's order; nothing for blank text.
    /// </summary>
    /// <exception cref="OperationCanceledException">A newer search took this one's place.</exception>
    public IReadOnlyList<LibraryEntry> Search(string? text, Func<LibraryEntry, string?>? extraText = null, CancellationToken cancel = default)
    {
        var words = (text ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (words.Length == 0)
        {
            return [];
        }

        var lowerWords = Array.ConvertAll(words, Lower);
        var found = new List<LibraryEntry>();
        for (var i = 0; i < _entries.Count; i++)
        {
            if ((i & 511) == 0)
            {
                cancel.ThrowIfCancellationRequested();
            }

            if (Holds(i, words, lowerWords, extraText))
            {
                found.Add(_entries[i]);
            }
        }

        return [.. LibraryViews.Songs(found), .. LibraryViews.Videos(found), .. LibraryViews.Pictures(found)];
    }

    /// <summary>Whether <paramref name="word"/> is found in <paramref name="field"/>, as the library search finds it.</summary>
    public static bool Matches(string? field, string word)
    {
        ArgumentNullException.ThrowIfNull(word);
        return field is not null && Matches(field, Lower(field), word, Lower(word));
    }

    private bool Holds(int index, string[] words, string?[] lowerWords, Func<LibraryEntry, string?>? extraText)
    {
        string? extra = null;
        var extraAsked = false;
        for (var w = 0; w < words.Length; w++)
        {
            if (Matches(_lines[index], _lower[index], words[w], lowerWords[w]))
            {
                continue;
            }

            if (!extraAsked)
            {
                extra = extraText?.Invoke(_entries[index]);
                extraAsked = true;
            }

            if (extra is null || !Matches(extra, Lower(extra), words[w], lowerWords[w]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool Matches(string line, string? lowerLine, string word, string? lowerWord) =>
        lowerLine is not null && lowerWord is not null
            ? lowerLine.Contains(lowerWord, StringComparison.Ordinal)
            : Compare.IndexOf(line, word, Loose) >= 0;

    /// <summary>The lowercase of text that is plain printable ASCII (line breaks allowed), where it can be searched by ordinal; null otherwise.</summary>
    private static string? Lower(string text)
    {
        if (!AsciiIsOrdinal)
        {
            return null;
        }

        foreach (var c in text)
        {
            if (c is not ('\n' or (>= ' ' and <= '~')))
            {
                return null;
            }
        }

        return text.ToLowerInvariant();
    }
}
