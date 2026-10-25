namespace Rex.Media.AppCore.Platform;

/// <summary>
/// Where rexplayer keeps what it remembers, decided once for every shell and tool (ADR-0020): the
/// same files under the folder each platform reserves for an application's data. Windows:
/// <c>%LocalAppData%\REX\rexplayer</c>; macOS: <c>~/Library/Application Support/REX/rexplayer</c>;
/// elsewhere <c>$XDG_DATA_HOME/REX/rexplayer</c>, or <c>~/.local/share/REX/rexplayer</c>.
/// <c>REXPLAYER_ROOT</c> replaces the root outright (tests point it at a scratch folder). Every
/// file's place is a property here, so no shell invents a path of its own.
/// </summary>
public sealed class DataRoots
{
    /// <summary>The environment variable that replaces the root.</summary>
    public const string RootVariable = "REXPLAYER_ROOT";

    private static readonly string[] Vendor = ["REX", "rexplayer"];

    public DataRoots(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        Root = root;
    }

    /// <summary>The roots for this process: the override when set, else the platform's folder.</summary>
    public static DataRoots ForCurrentProcess() => Resolve(
        Environment.GetEnvironmentVariable,
        OperatingSystem.IsWindows() ? PlatformKind.Windows : OperatingSystem.IsMacOS() ? PlatformKind.MacOS : PlatformKind.Other,
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));

    /// <summary>
    /// The roots for a platform, from what it offers: <paramref name="environment"/> reads a
    /// variable, <paramref name="localApplicationData"/> is Windows' per-user application folder,
    /// <paramref name="home"/> the user's home folder.
    /// </summary>
    public static DataRoots Resolve(Func<string, string?> environment, PlatformKind platform, string localApplicationData, string home)
    {
        ArgumentNullException.ThrowIfNull(environment);
        if (environment(RootVariable) is { Length: > 0 } root)
        {
            return new DataRoots(root);
        }

        var parent = platform switch
        {
            PlatformKind.Windows => localApplicationData,
            PlatformKind.MacOS => Path.Combine(home, "Library", "Application Support"),
            _ => environment("XDG_DATA_HOME") is { Length: > 0 } xdg ? xdg : Path.Combine(home, ".local", "share"),
        };
        return new DataRoots(Path.Combine([parent, .. Vendor]));
    }

    /// <summary>The folder everything below lives in.</summary>
    public string Root { get; }

    public string SettingsPath => Path.Combine(Root, "settings.json");

    /// <summary>The folder the logs are written in, which Help, Open the log folder shows.</summary>
    public string LogFolder => Path.Combine(Root, "logs");

    public string UpdateLog => Path.Combine(LogFolder, "update.log");

    /// <summary>Downloaded installers wait here until they are applied.</summary>
    public string UpdateFolder => Path.Combine(Root, "updates");

    /// <summary>The media library's store (ADR-0010).</summary>
    public string LibraryStore => Path.Combine(Root, "library.log");

    /// <summary>Bookmarks, history and resume points.</summary>
    public string MemoryStore => Path.Combine(Root, "store.log");

    /// <summary>Where the last run was when it did not close cleanly.</summary>
    public string ResumePath => Path.Combine(Root, "resume.json");

    /// <summary>Compiled shaders, so the second start skips the compiler.</summary>
    public string ShaderCache => Path.Combine(Root, "cache", "shaders");

    /// <summary>Intro and credits analyses, by file.</summary>
    public string SectionCache => Path.Combine(Root, "cache", "sections");

    /// <summary>Optional models the user chose to download (ADR-0019).</summary>
    public string Models => Path.Combine(Root, "models");

    /// <summary>Metadata fetched from the free catalogues, kept on this computer.</summary>
    public string Metadata => Path.Combine(Root, "metadata");

    /// <summary>A library picture's cached rendering, by its key.</summary>
    public string LibraryPicture(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return Path.Combine(Root, "cache", "library-pictures", key + ".png");
    }
}

/// <summary>The platforms whose data folders differ.</summary>
public enum PlatformKind
{
    Windows,
    MacOS,
    Other,
}
