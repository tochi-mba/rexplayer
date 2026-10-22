using System.Text.RegularExpressions;
using Rex.Media.Library;

namespace Rex.Media.AppCore.Library;

/// <summary>
/// Audiobook writing credits, distinct from the performer/album-artist credits used by music.
/// Prefer the actual book-author tag and an explicit byline; never turn a named narrator into
/// a writer. Folder inference only works for a recognizable Audiobooks/Author/Book layout.
/// </summary>
public static partial class BookCredits
{
    /// <summary>The best supported author credit, or null rather than a guessed or narrator credit.</summary>
    public static string? Author(LibraryEntry part)
    {
        ArgumentNullException.ThrowIfNull(part);

        // A person's edits take precedence over a downloaded file's tags.
        if (part.Override?.AlbumArtist is { } editedAlbumArtist)
        {
            return Credit(editedAlbumArtist, part.Narrator);
        }

        if (part.Override?.Artist is { } editedArtist)
        {
            return Credit(editedArtist, part.Narrator);
        }

        if (Credit(part.Author, part.Narrator) is { } tagged)
        {
            return tagged;
        }

        if ((Byline(part.Album) ?? Byline(part.Title)) is { } byline)
        {
            return byline;
        }

        if (Byline(Path.GetFileNameWithoutExtension(part.Path.Replace('\\', '/'))) is { } fileByline)
        {
            return fileByline;
        }

        if (Byline(Path.GetFileName(AudioRules.FolderOf(part.Path))) is { } folderByline)
        {
            return folderByline;
        }

        if (AuthorFolder(part) is { } fromFolders)
        {
            return fromFolders;
        }

        return Credit(part.AlbumArtist, part.Narrator) ?? Credit(part.Artist, part.Narrator);
    }

    /// <summary>Remove only a trailing, explicit writer byline from the displayed title.</summary>
    public static string Title(string title)
    {
        ArgumentNullException.ThrowIfNull(title);
        var match = BylinePattern().Match(title);
        return match.Success && CredibleName(match.Groups["name"].Value)
            ? title[..match.Index].TrimEnd(' ', '-', '–', (char)0x2014, '(', '[', ',', ':')
            : title;
    }

    /// <summary>
    /// The Author in Audiobooks/Author/Book/Chapter or Audiobooks/Author/Book.m4b.
    /// Disc/Part folders and the generic root are never considered author credits.
    /// </summary>
    public static string? AuthorFolder(LibraryEntry part)
    {
        ArgumentNullException.ThrowIfNull(part);
        var path = part.Path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        for (var index = path.Length - 3; index >= 0; index--)
        {
            if (path[index].Equals("Audiobooks", StringComparison.OrdinalIgnoreCase)
                || path[index].Equals("Audio Books", StringComparison.OrdinalIgnoreCase)
                || path[index].Equals("Audio-Books", StringComparison.OrdinalIgnoreCase)
                || path[index].Equals("Books", StringComparison.OrdinalIgnoreCase))
            {
                // Need an author directory and at least a separately named book file.
                if (index + 2 < path.Length && CredibleName(path[index + 1])
                    && !GenericFolder(path[index + 1]))
                {
                    // A book folder follows the author, or a whole book file follows it.
                    if (index + 3 < path.Length ||
                        (!PartFile(path[index + 2]) && !GenericFolder(Path.GetFileNameWithoutExtension(path[index + 2]))))
                    {
                        return path[index + 1].Trim();
                    }
                }

                return null;
            }
        }

        return null;
    }

    /// <summary>Reject placeholders and explicit narrator labels; compare against the narrator tag.</summary>
    private static string? Credit(string? raw, string? narrator)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var name = raw.Trim();
        if (name.Length > 180 || Placeholder(name) || NarratorLabelPattern().IsMatch(name)
            || (narrator is { Length: > 0 } && name.Equals(narrator.Trim(), StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }

        return name;
    }

    private static string? Byline(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var match = BylinePattern().Match(text.Trim());
        return match.Success && CredibleName(match.Groups["name"].Value)
            ? Credit(match.Groups["name"].Value, null)
            : null;
    }

    private static bool CredibleName(string name)
    {
        if (name.Length is < 4 or > 100 || Placeholder(name) || GenericFolder(name))
        {
            return false;
        }

        // Case-preserving first/last names (also non-Latin scripts) and initials.
        var words = name.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
        return words.Length is >= 2 and <= 6 && words.All(word =>
            word.Length > 0 && (char.IsUpper(word[0]) || char.GetUnicodeCategory(word[0]) == System.Globalization.UnicodeCategory.OtherLetter));
    }

    private static bool Placeholder(string text) =>
        text.Trim().ToLowerInvariant() is "unknown" or "unknown author" or "unknown artist"
            or "various" or "various artists" or "n/a" or "none" or "narrator"
            or "audiobook" or "audio book" or "books";

    private static bool GenericFolder(string name) =>
        name.Trim().ToLowerInvariant() is "audiobooks" or "audio books" or "books"
            or "disc 1" or "disc 2" or "cd1" or "cd2" or "part 1"
            or "collection" or "series" or "volume 1" or "volume 2";

    private static bool PartFile(string file) => PartFilePattern().IsMatch(Path.GetFileNameWithoutExtension(file));

    [GeneratedRegex(@"(?:\s+[-–\u2014]\s*|\s+|[\[(])(?:written\s+)?by\s+(?<name>[\p{L}\p{M}. '\-]+)[\])]?\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BylinePattern();

    [GeneratedRegex(@"^(?:(?:narrated|read|performed)\s+by\b|narrator\s*:)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NarratorLabelPattern();

    [GeneratedRegex(@"^(?:\d+|(?:disc|disk|cd|part|chapter|ch)\s*[-_.#]?\s*\d+(?:\s+of\s+\d+)?)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PartFilePattern();
}
