using System.Diagnostics;

namespace Rex.Media.TestKit;

/// <summary>Finds the checkout a test is running from, and the files git knows about in it.</summary>
public static class RepoPaths
{
    private static readonly Lazy<string> RootPath = new(FindRoot);

    /// <summary>The folder holding rexplayer.slnx.</summary>
    public static string Root => RootPath.Value;

    /// <summary>"Release" or "Debug", read from the folder the tests were built into.</summary>
    public static string Configuration =>
        AppContext.BaseDirectory.Contains($"{Path.DirectorySeparatorChar}Release{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
            ? "Release"
            : "Debug";

    public static string Combine(params string[] parts) => Path.Combine([Root, .. parts]);

    /// <summary>
    /// Every file git tracks plus every new file it would add: the repository's files as a reviewer
    /// sees them. Rules run over this list rather than a directory walk so build output, editor
    /// folders and local tool caches can never leak into them. Paths use forward slashes.
    /// </summary>
    public static IReadOnlyList<string> SourceFiles()
    {
        var start = new ProcessStartInfo("git", "ls-files -z --cached --others --exclude-standard")
        {
            WorkingDirectory = Root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        using var git = Process.Start(start) ?? throw new InvalidOperationException("git could not be started.");
        var output = git.StandardOutput.ReadToEnd();
        git.WaitForExit();
        if (git.ExitCode != 0)
        {
            throw new InvalidOperationException("git ls-files failed: " + git.StandardError.ReadToEnd());
        }

        return output
            .Split('\0', StringSplitOptions.RemoveEmptyEntries)
            .Where(path => File.Exists(Path.Combine(Root, path)))
            .Order(StringComparer.Ordinal)
            .ToList();
    }

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "rexplayer.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("The tests are not running inside a rexplayer checkout.");
    }
}
