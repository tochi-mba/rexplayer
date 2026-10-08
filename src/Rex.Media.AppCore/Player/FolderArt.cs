namespace Rex.Media.AppCore.Player;

/// <summary>
/// The picture that stands for an album when its files carry none (META-06): cover, folder, front,
/// or one of Windows' AlbumArt pictures, in that order of preference, as JPEG, PNG or WebP beside
/// the music. Names are matched whatever their case.
/// </summary>
public static class FolderArt
{
    private static readonly string[] Names = ["cover", "folder", "front"];
    private static readonly string[] Extensions = [".jpg", ".jpeg", ".png", ".webp"];

    /// <summary>
    /// The picture for <paramref name="media"/>, from the files <paramref name="list"/> gives for its
    /// folder (tests pass their own); null when there is none or the folder cannot be read.
    /// </summary>
    public static string? Find(string media, Func<string, IEnumerable<string>>? list = null)
    {
        ArgumentNullException.ThrowIfNull(media);
        list ??= folder => Directory.Exists(folder) ? Directory.EnumerateFiles(folder) : [];
        var folder = Path.GetDirectoryName(media);
        if (string.IsNullOrEmpty(folder) || media.Contains("://", StringComparison.Ordinal))
        {
            return null;
        }

        List<string> pictures;
        try
        {
            pictures = [.. list(folder).Where(path => Extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        foreach (var name in Names)
        {
            if (pictures.FirstOrDefault(path => Path.GetFileNameWithoutExtension(path).Equals(name, StringComparison.OrdinalIgnoreCase)) is { } found)
            {
                return found;
            }
        }

        // AlbumArt_{...}_Large before AlbumArtSmall.
        return pictures
            .Where(path => Path.GetFileNameWithoutExtension(path).StartsWith("albumart", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(path => Path.GetFileNameWithoutExtension(path).EndsWith("large", StringComparison.OrdinalIgnoreCase))
            .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }
}
