using System.Diagnostics;

namespace Rex.Media.Updater;

/// <summary>
/// The tiny hand-off between rexplayer and its installer: it waits until the app has closed cleanly,
/// then starts the already downloaded and verified installer. It runs from a copy outside the
/// installed folder, so the installer can replace every installed file without racing the app that owns it.
/// </summary>
/// <remarks>
/// Arguments: the app's process id, the installer, the log, and optionally the mode and the app.
/// "relaunch" (the default) installs with a progress window and opens rexplayer again where it was;
/// if the installer fails or is cancelled it opens the installed rexplayer instead, so the user is
/// never left without it. "quiet" installs with no window and opens nothing: rexplayer was closing.
/// Either way it records how the installer ended ("installer-name exit-code", in
/// install-result.txt beside the installer) before reopening anything, so the next start can tell
/// a cancelled install from a failed one (UpdateFlow.InstallEndOf reads it).
/// </remarks>
internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length is < 3 or > 5 || !int.TryParse(args[0], out var parentId))
        {
            return 2;
        }

        var installer = args[1];
        var log = args[2];
        var quiet = args.Length > 3 && args[3] == "quiet";
        var app = args.Length > 4 ? args[4] : null;
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

            Append(log, quiet ? "Starting the verified installer quietly." : "Starting the verified installer.");
            var start = new ProcessStartInfo(installer) { UseShellExecute = true };
            foreach (var argument in quiet
                ? new[] { "/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART", "/NOCLOSEAPPLICATIONS" }
                : ["/SILENT", "/SUPPRESSMSGBOXES", "/NORESTART", "/NOCLOSEAPPLICATIONS", "/relaunch=1"])
            {
                start.ArgumentList.Add(argument);
            }

            using var process = Process.Start(start);
            if (process is null)
            {
                Record(installer, -1);
                Reopen(log, quiet, app);
                return 4;
            }

            process.WaitForExit();
            Append(log, $"The installer finished with exit code {process.ExitCode}.");
            Record(installer, process.ExitCode);
            if (process.ExitCode != 0)
            {
                Reopen(log, quiet, app);
            }

            return process.ExitCode == 0 ? 0 : 5;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            try
            {
                Append(log, "Update hand-off failed: " + ex.Message);
                Record(installer, -1);
                Reopen(log, quiet, app);
            }
            catch (Exception logError) when (logError is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or InvalidOperationException)
            {
            }

            return 1;
        }
    }

    /// <summary>After a failed or cancelled install that was meant to reopen rexplayer, opens the version still installed.</summary>
    private static void Reopen(string log, bool quiet, string? app)
    {
        if (quiet || app is null || !File.Exists(app))
        {
            return;
        }

        Append(log, "Opening the installed rexplayer again.");
        using var reopened = Process.Start(new ProcessStartInfo(app) { UseShellExecute = false });
    }

    /// <summary>Notes how the installer ended, for the next start of rexplayer; a record that cannot be written only loses the detail.</summary>
    private static void Record(string installer, int exitCode)
    {
        try
        {
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(installer))!, "install-result.txt"), $"{Path.GetFileName(installer)} {exitCode}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static void Append(string path, string message)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.AppendAllText(path, $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}");
    }
}
