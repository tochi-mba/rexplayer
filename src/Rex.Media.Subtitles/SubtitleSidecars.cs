// Spec: rexplayer's own convention for subtitle files kept beside media (SUB-14): the media's folder and its Subs, subs, Subtitles and subtitles folders, files named after the media with an optional ISO 639 language code and "forced" or "sdh" marks ("Film.en.srt", "Film.fr.forced.srt").
namespace Rex.Media.Subtitles;

/// <summary>A subtitle file found beside media, how well its name matches, and what its name says about it.</summary>
public sealed record SubtitleSidecar(string Path, int Match, string? Language, bool Forced);

/// <summary>
/// Finds the subtitle files that belong to a piece of media: in its folder or the usual subtitle
/// folders beside it, named exactly after it, starting with its name, or (when there is only one
/// such file nearby) any name at all. Better matches come first.
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
        var candidates = Folders
            .Select(sub => sub.Length == 0 ? folder : Path.Combine(folder, sub))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .SelectMany(directory => Listed(list, directory))
            .Where(path => Extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
            .ToList();

        var found = new List<SubtitleSidecar>();
        foreach (var path in candidates)
        {
            var name = Path.GetFileNameWithoutExtension(path);
            var match = string.Equals(name, stem, StringComparison.OrdinalIgnoreCase) ? 3
                : name.StartsWith(stem + ".", StringComparison.OrdinalIgnoreCase) || name.StartsWith(stem + "_", StringComparison.OrdinalIgnoreCase) || name.StartsWith(stem + " ", StringComparison.OrdinalIgnoreCase) ? 2
                : name.Contains(stem, StringComparison.OrdinalIgnoreCase) ? 1
                : 0;
            var marks = match >= 2 ? name[stem.Length..].Split(['.', '_', ' ', '-'], StringSplitOptions.RemoveEmptyEntries) : [];
            var language = marks.FirstOrDefault(mark => mark.Length is 2 or 3 && mark.All(char.IsAsciiLetter) && !IsMark(mark))?.ToLowerInvariant();
            found.Add(new SubtitleSidecar(path, match, language, marks.Any(mark => mark.Equals("forced", StringComparison.OrdinalIgnoreCase))));
        }

        // A file that matches by name in no way is only taken when it is the only subtitle file nearby.
        return [.. found.Where(sidecar => sidecar.Match > 0 || found.Count == 1).OrderByDescending(sidecar => sidecar.Match).ThenBy(sidecar => sidecar.Path, StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>Whether a name is a subtitle file the player can read.</summary>
    public static bool IsSubtitle(string path) => Extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    private static bool IsMark(string mark) => mark.Equals("sdh", StringComparison.OrdinalIgnoreCase) || mark.Equals("cc", StringComparison.OrdinalIgnoreCase);

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
