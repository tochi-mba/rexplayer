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
        panel.Begin(response =>
        {
            if (response != 1 || panel.Url?.Path is not { } parent) return;
            PublishMacLibraryBackup(parent);
        });
    }

    private void PublishMacLibraryBackup(string parent)
    {
        if (_mediaLibrary is null) return;
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
        panel.Begin(response =>
        {
            if (response != 1 || panel.Url?.Path is not { } path) return;
            ReadMacLibraryBackup(path);
        });
    }

    private void ReadMacLibraryBackup(string path)
    {
        if (_mediaLibrary is null) return;
        try
        {
            // This verifies the completed backup's digests before the first library write.
            var backup = LibraryBackup.ReadFromFile(path);
            var preview = LibraryBackup.Preview(backup, _mediaLibrary, _player);
            ConfirmInWindow(
                $"Restore {preview.Folders} folders, {preview.Entries} media entries and " +
                $"{preview.Playlists} playlists? Existing media is not deleted. " +
                "The two-store restore cannot automatically roll back an interrupted write.", () =>
                {
                    if (_mediaLibrary is null)
                    {
                        Say("The library is unavailable.");
                        return;
                    }

                    try
                    {
                        var restored = LibraryBackup.Restore(backup, _mediaLibrary, _player);
                        _libraryScanner?.Request();
                        RefreshLibrary(force: true);
                        Say($"Restored {restored.Folders} folders, {restored.Entries} files and {restored.Playlists} playlists.");
                    }
                    catch (Exception error) when (error is IOException or UnauthorizedAccessException or FormatException)
                    {
                        Say("Library restore failed: " + error.Message);
                    }
                });
        }
        catch (Exception ex) when (ex is FormatException or IOException or UnauthorizedAccessException)
        {
            _log.Warning(LogSource, "Could not restore library backup: " + ex.Message);
            Say("The backup could not be restored: " + ex.Message);
        }
    }
}
