// Spec: rexplayer's own convention for subtitle files kept beside media (SUB-14): the media's folder and its Subs, subs, Subtitles and subtitles folders, including a subfolder named exactly after the media; files may be named after the media with an optional ISO 639 language code and "forced" or "sdh" marks ("Film.en.srt", "Film.fr.forced.srt"), or numbered by language in that media-named subfolder ("2_English.srt").
using Rex.Media.Primitives;
namespace Rex.Media.Subtitles;

/// <summary>A subtitle file found beside media, how well its name matches, and what its name says about it.</summary>
public sealed record SubtitleSidecar(string Path, int Match, string? Language, bool Forced);

/// <summary>
/// Finds the subtitle files that belong to a piece of media: in its folder or the usual subtitle
/// folders beside it or a media-named folder inside one of them, named exactly after it, starting
/// with its name, or (when there is only one such file nearby) any name at all. Better matches come
/// first.
/// </summary>
public static class SubtitleSidecars
{
    public static IReadOnlyList<string> Extensions { get; } = [".srt", ".vtt", ".ass", ".ssa", ".sub", ".smi", ".sami", ".txt", ".mpl"];

    private static readonly string[] Folders = ["", "Subs", "subs", "Subtitles", "subtitles"];

    /// <summary>
    /// The sidecars for <paramref name="media"/>. <paramref name="list"/> gives a folder's files
    /// (tests pass their own); a folder that does not exist or cannot be read gives none.
    /// </summary>
    public static IReadOnlyList<SubtitleSidecar> Find(string media, Func<string, IEnumerable<string>>? list = null)
    {
        ArgumentNullException.ThrowIfNull(media);
        list ??= folder => Directory.Exists(folder) ? Directory.EnumerateFiles(folder) : [];
        var folder = Path.GetDirectoryName(media) ?? "";
        var stem = Path.GetFileNameWithoutExtension(media);
        var locations = Folders
            .Select(sub => (Directory: sub.Length == 0 ? folder : Path.Combine(folder, sub), OwnFolder: false))
            .Concat(Folders.Where(sub => sub.Length > 0).Select(sub => (Directory: Path.Combine(folder, sub, stem), OwnFolder: true)))
            .DistinctBy(location => location.Directory, StringComparer.OrdinalIgnoreCase);
        var candidates = locations
            .SelectMany(location => Listed(list, location.Directory).Select(path => (Path: path, location.OwnFolder)))
            .Where(candidate => Extensions.Contains(Path.GetExtension(candidate.Path), StringComparer.OrdinalIgnoreCase))
            .ToList();

        var found = new List<SubtitleSidecar>();
        foreach (var (path, ownFolder) in candidates)
        {
            var name = Path.GetFileNameWithoutExtension(path);
            var match = ownFolder || string.Equals(name, stem, StringComparison.OrdinalIgnoreCase) ? 3
                : name.StartsWith(stem + ".", StringComparison.OrdinalIgnoreCase) || name.StartsWith(stem + "_", StringComparison.OrdinalIgnoreCase) || name.StartsWith(stem + " ", StringComparison.OrdinalIgnoreCase) ? 2
                : name.Contains(stem, StringComparison.OrdinalIgnoreCase) ? 1
                : 0;
            var marks = ownFolder ? name.Split(['.', '_', ' ', '-'], StringSplitOptions.RemoveEmptyEntries)
                : match >= 2 ? name[stem.Length..].Split(['.', '_', ' ', '-'], StringSplitOptions.RemoveEmptyEntries)
                : [];
            var language = marks.Select(Language).FirstOrDefault(value => value is not null);
            found.Add(new SubtitleSidecar(path, match, language, marks.Any(mark => mark.Equals("forced", StringComparison.OrdinalIgnoreCase))));
        }

        // A file that matches by name in no way is only taken when it is the only subtitle file nearby.
        return [.. found.Where(sidecar => sidecar.Match > 0 || found.Count == 1)
            .OrderByDescending(sidecar => sidecar.Match)
            .ThenBy(sidecar => LeadingNumber(sidecar.Path))
            .ThenBy(sidecar => sidecar.Path, StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>Whether a name is a subtitle file the player can read.</summary>
    public static bool IsSubtitle(string path) => Extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    private static bool IsMark(string mark) => mark.Equals("sdh", StringComparison.OrdinalIgnoreCase) || mark.Equals("cc", StringComparison.OrdinalIgnoreCase);

    private static int LeadingNumber(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        var digits = name.TakeWhile(char.IsAsciiDigit).Count();
        return digits > 0 && int.TryParse(name.AsSpan(0, digits), out var number) ? number : int.MaxValue;
    }

    /// <summary>An ISO code, or a recognised full English language name, from one filename mark.</summary>
    private static string? Language(string mark)
    {
        if (!mark.All(char.IsAsciiLetter) || IsMark(mark) || mark.Equals("forced", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var lower = mark.ToLowerInvariant();
        var canonical = Languages.Canonical(mark);
        return mark.Length is 2 or 3 ? lower
            : canonical is not null && !canonical.Equals(lower, StringComparison.Ordinal) ? canonical
            : null;
    }

    private static IEnumerable<string> Listed(Func<string, IEnumerable<string>> list, string directory)
    {
        try
        {
            return [.. list(directory)];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A folder that cannot be read has nothing to offer.
            return [];
        }
    }
}
