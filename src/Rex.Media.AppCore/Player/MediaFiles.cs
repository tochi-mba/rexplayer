namespace Rex.Media.AppCore.Player;

/// <summary>
/// Which files the player offers to play, and how a folder becomes a list of them. The extension
/// only decides what a folder contributes; once opened, every file is identified by its contents.
/// </summary>
public static class MediaFiles
{
    /// <summary>Video files, also the groups the installer offers to associate (§6.13).</summary>
    public static IReadOnlyList<string> Video { get; } =
    [
        ".mp4", ".m4v", ".mkv", ".webm", ".avi", ".mov", ".wmv", ".asf", ".ts", ".m2ts", ".mts", ".mpg", ".mpeg",
        ".vob", ".flv", ".3gp", ".ogv", ".y4m", ".264", ".h264", ".265", ".hevc",
    ];

    public static IReadOnlyList<string> Audio { get; } =
    [
        ".mp3", ".flac", ".m4a", ".aac", ".adts", ".wav", ".ogg", ".oga", ".opus", ".wma", ".aif", ".aiff", ".aifc",
        ".caf", ".ac3", ".eac3", ".dts", ".mka", ".mp2", ".rf64", ".w64",
    ];

    /// <summary>Pictures (FMT-C19): shown for a while each, so a folder of them plays as a slideshow.</summary>
    public static IReadOnlyList<string> Pictures { get; } =
    [
        ".jpg", ".jpeg", ".jpe", ".jfif", ".png", ".gif", ".webp", ".bmp", ".dib", ".tif", ".tiff", ".heic", ".heif", ".avif",
    ];

    private static readonly HashSet<string> Known = new([.. Video, .. Audio, .. Pictures], StringComparer.OrdinalIgnoreCase);

    /// <summary>Whether a file with this name is one the player offers to play.</summary>
    public static bool IsMedia(string path) => Known.Contains(Path.GetExtension(path));

    private static readonly HashSet<string> PictureSet = new(Pictures, StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> VideoSet = new(Video, StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> AudioSet = new(Audio, StringComparer.OrdinalIgnoreCase);

    public static bool IsPicture(string path) => PictureSet.Contains(Path.GetExtension(path));

    /// <summary>What the library files a file as, by its name; null for a file that is not media.</summary>
    public static Rex.Media.Library.LibraryKind? LibraryKindOf(string path)
    {
        var extension = Path.GetExtension(path);
        return VideoSet.Contains(extension) ? Rex.Media.Library.LibraryKind.Video : AudioSet.Contains(extension) ? Rex.Media.Library.LibraryKind.Music : null;
    }

    /// <summary>
    /// The media in <paramref name="folder"/> in natural order, files before the folders inside it,
    /// each of those (when <paramref name="recursive"/>) expanded the same way. The listings are
    /// passed in, so tests need no disk; a folder that cannot be listed contributes nothing. Its
    /// pictures count only when it holds no sound or video: a folder of photos is a slideshow, and
    /// an album's cover stays out of its playlist.
    /// </summary>
    public static IReadOnlyList<string> ExpandFolder(
        string folder,
        Func<string, IEnumerable<string>> files,
        Func<string, IEnumerable<string>> folders,
        bool recursive = true)
    {
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(folders);
        var found = new List<string>();
        Expand(folder, files, folders, recursive, found, depth: 0);
        return found.TrueForAll(IsPicture) ? found : found.FindAll(path => !IsPicture(path));
    }

    /// <summary>Folders on disk, through <see cref="ExpandFolder(string, Func{string, IEnumerable{string}}, Func{string, IEnumerable{string}}, bool)"/>.</summary>
    public static IReadOnlyList<string> ExpandFolder(string folder, bool recursive = true) =>
        ExpandFolder(folder, Directory.EnumerateFiles, Directory.EnumerateDirectories, recursive);

    private static void Expand(string folder, Func<string, IEnumerable<string>> files, Func<string, IEnumerable<string>> folders, bool recursive, List<string> found, int depth)
    {
        try
        {
            found.AddRange(files(folder).Where(IsMedia).Order(ByName));
            if (!recursive || depth >= MaxDepth)
            {
                return;
            }

            foreach (var inner in folders(folder).Order(ByName))
            {
                Expand(inner, files, folders, recursive, found, depth + 1);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A folder the user cannot read (or one removed while listing) is skipped, not fatal.
        }
    }

    /// <summary>Deep enough for any real library, shallow enough that a link loop ends.</summary>
    private const int MaxDepth = 32;

    private static readonly IComparer<string> ByName = Comparer<string>.Create((a, b) => NaturalOrder.Instance.Compare(Path.GetFileName(a), Path.GetFileName(b)));
}
