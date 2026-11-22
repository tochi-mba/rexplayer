using Rex.Media.AppCore.Automation;

namespace Rex.Media.App.Mac;

/// <summary>
/// Adding folders to the library: the overflow menu of the usual places, rescanning and backups,
/// the folder picker, and the one path both the picker and automation register folders through.
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>The overflow menu: adding folders, the usual places, rescanning and backups.</summary>
    private void ShowLibraryMoreMenu()
    {
        if (_libraryMore is null)
        {
            return;
        }

        var menu = new NSMenu();
        menu.AddItem(new NSMenuItem("Add folder…", (_, _) => AddLibraryFolder()));
        foreach (var (title, folder, special) in new[]
                 {
                     ("Add your Music folder", "Music", Environment.SpecialFolder.MyMusic),
                     ("Add your Videos folder", "Videos", Environment.SpecialFolder.MyVideos),
                     ("Add your Pictures folder", "Pictures", Environment.SpecialFolder.MyPictures),
                 })
        {
            var path = Environment.GetFolderPath(special);
            var item = new NSMenuItem(title, (_, _) => AddSuggestedLibraryFolder(path, folder))
            {
                Enabled = !string.IsNullOrWhiteSpace(path) && Directory.Exists(path),
                ToolTip = path,
            };
            menu.AddItem(item);
        }

        menu.AutoEnablesItems = false;
        menu.AddItem(NSMenuItem.SeparatorItem);
        menu.AddItem(new NSMenuItem("Rescan folders", (_, _) => _libraryScanner?.Request()));
        menu.AddItem(new NSMenuItem("Back up…", (_, _) => BackUpLibrary()));
        menu.AddItem(new NSMenuItem("Restore backup…", (_, _) => RestoreLibraryBackup()));
        menu.PopUpMenu(null, new CGPoint(0, _libraryMore.Bounds.Height + 4), _libraryMore);
    }

    /// <summary>Watches one of the usual places (Music, Videos, Pictures) and says how it went.</summary>
    private void AddSuggestedLibraryFolder(string path, string folder)
    {
        if (!string.IsNullOrWhiteSpace(path))
        {
            var answer = AddLibraryFolders([path]);
            Say(answer.Ok ? folder + " is now watched." : answer.Error ?? "That folder could not be added.");
        }
    }

    private void AddLibraryFolder()
    {
        if (_mediaLibrary is null)
        {
            return;
        }

        var panel = NSOpenPanel.OpenPanel;
        panel.AllowsMultipleSelection = true;
        panel.CanChooseFiles = false;
        panel.CanChooseDirectories = true;
        panel.Begin(response =>
        {
            if (response != 1) return;
            var reply = AddLibraryFolders(panel.Urls.Select(url => url.Path!).ToList());
            if (!reply.Ok)
            {
                Say(reply.Error ?? "Could not add a library folder.");
            }
        });
    }

    /// <summary>
    /// The native folder picker and Mac automation both use the same registration path.
    /// Scan only after a folder has been saved in the persistent library store.
    /// </summary>
    private AutomationReply AddLibraryFolders(IReadOnlyCollection<string> folders)
    {
        if (_mediaLibrary is null || folders.Count == 0 || folders.Any(folder => !Directory.Exists(folder)))
        {
            return AutomationReply.Failed("Select one or more existing folders.");
        }

        var added = 0;
        var errors = new List<string>();
        foreach (var folder in folders)
        {
            try
            {
                if (_mediaLibrary.AddFolder(folder))
                {
                    added++;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                _log.Warning(LogSource, "Cannot add library folder: " + ex.Message);
                errors.Add(ex.Message);
            }
        }

        if (added > 0)
        {
            _libraryScanner?.Request();
        }

        RefreshLibrary();
        if (_libraryTemporaryStore)
        {
            return AutomationReply.Failed(
                "The library store could not be opened. Watched folders will be forgotten when the app quits.");
        }

        if (_libraryStore?.WriteProblem is { } writeProblem)
        {
            return AutomationReply.Failed("Library folder changes are only in memory: " + writeProblem);
        }

        return errors.Count == 0
            ? new AutomationReply { Ok = true, Text = $"Added {added} watched folder(s)." }
            : AutomationReply.Failed("Could not add one or more folders: " + string.Join("; ", errors));
    }
}
