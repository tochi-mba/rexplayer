using System.Diagnostics;

namespace Rex.Media.Updater;

/// <summary>
/// The tiny hand-off between rexplayer and its installer: it waits until the app has closed cleanly,
/// then starts the already downloaded and verified installer. It runs from the temporary update
/// folder, so the installer can replace every installed file without racing the app that owns it.
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length != 3 || !int.TryParse(args[0], out var parentId))
        {
            return 2;
        }

        var installer = args[1];
        var log = args[2];
        try
        {
            Append(log, $"Waiting for rexplayer process {parentId} to close.");
            try
            {
                using var parent = Process.GetProcessById(parentId);
                if (!parent.WaitForExit(TimeSpan.FromSeconds(30)))
                {
                    Append(log, "rexplayer did not close within 30 seconds; the update was not started.");
                    return 3;
                }
            }
            catch (ArgumentException)
            {
                // It closed before this helper reached it.
            }

            Append(log, "Starting the verified installer.");
            using var process = Process.Start(new ProcessStartInfo(installer)
            {
                UseShellExecute = true,
                ArgumentList = { "/SILENT", "/SUPPRESSMSGBOXES", "/NORESTART", "/NOCLOSEAPPLICATIONS", "/relaunch=1" },
            });
            return process is null ? 4 : 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            try
            {
                Append(log, "Update hand-off failed: " + ex.Message);
            }
            catch (Exception logError) when (logError is IOException or UnauthorizedAccessException)
            {
            }

            return 1;
        }
    }

    private static void Append(string path, string message)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.AppendAllText(path, $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}");
    }
}
