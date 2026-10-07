using System.IO.Compression;
using System.Text;

namespace Rex.Media.AppCore;

/// <summary>
/// The file a user attaches to a problem report (TOOL-10): the log, the settings and a description
/// of the PC, zipped. Nothing leaves the PC unless the user sends it, and their home folder is
/// replaced by %USERPROFILE% wherever it appears, so file names do not give away who they are.
/// </summary>
public static class DiagnosticsBundle
{
    /// <summary>
    /// Writes a zip of <paramref name="files"/> (name and text) to <paramref name="output"/>, with
    /// <paramref name="home"/> masked in every one. Files that are null are left out.
    /// </summary>
    public static void Write(Stream output, IEnumerable<(string Name, string? Text)> files, string? home)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(files);
        using var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);
        foreach (var (name, text) in files)
        {
            if (text is null)
            {
                continue;
            }

            var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
            using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            writer.Write(Mask(text, home));
        }
    }

    /// <summary><paramref name="text"/> with <paramref name="home"/> (in either slash style) written as %USERPROFILE%.</summary>
    public static string Mask(string text, string? home)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (string.IsNullOrEmpty(home))
        {
            return text;
        }

        var forward = home.Replace('\\', '/');
        return text.Replace(home, "%USERPROFILE%", StringComparison.OrdinalIgnoreCase)
            .Replace(forward, "%USERPROFILE%", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A name for the bundle, by the moment it was made: "rexplayer-diagnostics-20261007-0930.zip".</summary>
    public static string FileName(DateTimeOffset now) =>
        "rexplayer-diagnostics-" + now.ToString("yyyyMMdd-HHmm", System.Globalization.CultureInfo.InvariantCulture) + ".zip";
}
