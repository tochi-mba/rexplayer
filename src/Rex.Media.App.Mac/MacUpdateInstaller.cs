// Spec: Apple Bundle Programming Guide, Bundle Structures; POSIX.1-2017 wait and rename semantics.
using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using Rex.Media.AppCore.Updates;

namespace Rex.Media.App.Mac;

/// <summary>Stages a verified release archive and hands replacement to a process that outlives the app.</summary>
internal static class MacUpdateInstaller
{
    private const string AppName = "rexplayer.app";

    internal static string Stage(string archive, string updateFolder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(archive);
        ArgumentException.ThrowIfNullOrWhiteSpace(updateFolder);
        var staging = Path.Combine(updateFolder, "staged-app-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        var root = Path.GetFullPath(staging) + Path.DirectorySeparatorChar;
        using var zip = ZipFile.OpenRead(archive);
        foreach (var entry in zip.Entries)
        {
            if (((entry.ExternalAttributes >> 16) & 0xf000) == 0xa000)
            {
                throw new InvalidDataException("The update archive contains an unsupported symbolic link.");
            }
            var destination = Path.GetFullPath(Path.Combine(staging, entry.FullName));
            if (!destination.StartsWith(root, StringComparison.Ordinal))
            {
                throw new InvalidDataException("The update archive contains a path outside its app bundle.");
            }

            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(destination);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            entry.ExtractToFile(destination, overwrite: true);
            var mode = (entry.ExternalAttributes >> 16) & 0x1ff;
            if (mode != 0 && !OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(destination, (UnixFileMode)mode);
            }
        }

        var apps = Directory.GetDirectories(staging, "*.app", SearchOption.AllDirectories)
            .Where(path => string.Equals(Path.GetFileName(path), AppName, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (apps.Length != 1 || !File.Exists(Path.Combine(apps[0], "Contents", "Info.plist"))
            || !File.Exists(Path.Combine(apps[0], "Contents", "MacOS", "rexplayer")))
        {
            throw new InvalidDataException("The update archive does not contain exactly one complete rexplayer app.");
        }

        return apps[0];
    }

    internal static void HandOff(string stagedApp, string currentApp, string updateFolder, bool relaunch)
    {
        if (!currentApp.EndsWith(".app", StringComparison.OrdinalIgnoreCase)
            || !Directory.Exists(stagedApp) || !Directory.Exists(currentApp))
        {
            throw new InvalidOperationException("rexplayer must be running from a writable app bundle to install this update.");
        }

        var helper = Path.Combine(updateFolder, "install-mac-update-" + Guid.NewGuid().ToString("N") + ".sh");
        var log = Path.Combine(updateFolder, "install-mac-update.log");
        var record = Path.Combine(updateFolder, UpdateFlow.InstallRecordName);
        File.WriteAllText(helper, Script, new UTF8Encoding(false));
        File.Delete(record);
        var start = new ProcessStartInfo("/bin/sh")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in new[]
        {
            helper,
            Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            stagedApp,
            currentApp,
            log,
            record,
            relaunch ? "relaunch" : "quiet",
        })
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start)
            ?? throw new InvalidOperationException("macOS did not start the update hand-off.");
    }

    private const string Script = """
#!/bin/sh
pid="$1"
staged="$2"
current="$3"
log="$4"
record="$5"
mode="$6"
backup="${current}.rexplayer-backup-$$"
while kill -0 "$pid" 2>/dev/null; do sleep 1; done
{
  if [ ! -d "$staged" ] || [ ! -d "$current" ]; then
    printf 'failed: missing staged or current app\n' > "$record"
    exit 1
  fi
  if [ -e "$backup" ]; then
    printf 'failed: backup already exists\n' > "$record"
    exit 1
  fi
  if ! mv "$current" "$backup"; then
    printf 'failed: could not move current app\n' > "$record"
    exit 1
  fi
  if ! mv "$staged" "$current"; then
    mv "$backup" "$current"
    printf 'failed: could not install staged app\n' > "$record"
    exit 1
  fi
  # Preserve the previous app so an interrupted launch remains recoverable.
  printf 'success\n' > "$record"
  if [ "$mode" = 'relaunch' ]; then /usr/bin/open "$current"; fi
} >> "$log" 2>&1
""";
}
