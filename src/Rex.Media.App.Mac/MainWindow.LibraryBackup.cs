using Rex.Media.AppCore.Library;

namespace Rex.Media.App.Mac;

/// <summary>Mac file pickers over the shared versioned library and named-playlist backup format.</summary>
public sealed partial class MainWindow
{
    private void BackUpLibrary()
    {
        if (_mediaLibrary is null)
        {
            Say("The library is unavailable in this session.");
            return;
        }

        var panel = NSOpenPanel.OpenPanel;
        panel.CanChooseDirectories = true;
        panel.CanChooseFiles = false;
        panel.AllowsMultipleSelection = false;
        panel.Title = "Choose where to save a new library backup";
        if (panel.RunModal() != 1 || panel.Url?.Path is not { } parent)
        {
            return;
        }

        IReadOnlyList<(string Path, byte[] Bytes)> files;
        try
        {
            // Capture one in-memory snapshot on the window thread, then publish away from it.
            files = LibraryBackup.Export(_mediaLibrary, _player.NamedPlaylists.ToArray());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException)
        {
            Say("The library snapshot could not be made: " + ex.Message);
            return;
        }

        Say("Saving a new library backup…");
        _ = Task.Run(() =>
        {
            try
            {
                var saved = LibraryBackup.PublishSnapshot(parent, files);
                OnWindowThread(() => Say("Library backup saved: " + Path.GetFileName(saved)));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException)
            {
                _log.Warning(LogSource, "Library backup failed: " + ex.Message);
                OnWindowThread(() => Say("Library backup failed: " + ex.Message));
            }
        });
    }

    private void RestoreLibraryBackup()
    {
        if (_mediaLibrary is null)
        {
            Say("The library is unavailable in this session.");
            return;
        }

        var panel = NSOpenPanel.OpenPanel;
        panel.CanChooseDirectories = false;
        panel.CanChooseFiles = true;
        panel.AllowsMultipleSelection = false;
        panel.Title = "Choose rexplayer library.json from a backup";
        if (panel.RunModal() != 1 || panel.Url?.Path is not { } path)
        {
            return;
        }

        try
        {
            // This verifies the completed backup's digests before the first library write.
            var backup = LibraryBackup.ReadFromFile(path);
            var preview = LibraryBackup.Preview(backup, _mediaLibrary, _player);
            using var confirm = new NSAlert
            {
                MessageText = "Merge this backup into your library?",
                InformativeText =
                    $"New folders: {preview.Folders}; media entries: {preview.Entries}; saved playlists: {preview.Playlists}. " +
                    "Existing files are not deleted. Restoring changes two stores and cannot yet roll " +
                    "back automatically after an interrupted write.",
            };
            confirm.AddButton("Restore backup");
            confirm.AddButton("Cancel");
            if (confirm.RunModal() != 1000)
            {
                return;
            }

            var restored = LibraryBackup.Restore(backup, _mediaLibrary, _player);
            _libraryScanner?.Request();
            RefreshLibrary(force: true);
            Say($"Restored {restored.Folders} folders, {restored.Entries} files and {restored.Playlists} playlists.");
        }
        catch (Exception ex) when (ex is FormatException or IOException or UnauthorizedAccessException)
        {
            _log.Warning(LogSource, "Could not restore library backup: " + ex.Message);
            Say("The backup could not be restored: " + ex.Message);
        }
    }
}
