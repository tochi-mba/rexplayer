using Microsoft.UI.Xaml;
using Rex.Media.AppCore.Library;
using Windows.Storage.Pickers;

namespace Rex.Media.App;

public sealed partial class MainWindow
{
    private void OnLibraryBackUp(object sender, RoutedEventArgs e) => Forget(BackUpLibraryAsync());

    private void OnLibraryRestore(object sender, RoutedEventArgs e) => Forget(RestoreLibraryAsync());

    /// <summary>Keeps the library and the named playlists in a folder the user chooses (LIB-10).</summary>
    private async Task BackUpLibraryAsync()
    {
        var picker = Prepared(new FolderPicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary });
        picker.FileTypeFilter.Add("*");
        if (await picker.PickSingleFolderAsync() is not { } folder)
        {
            return;
        }

        try
        {
            var playlists = _player.NamedPlaylists.ToArray();
            await Task.Run(() =>
            {
                foreach (var (path, bytes) in LibraryBackup.Export(_library!, playlists))
                {
                    var target = Path.Combine(folder.Path, path);
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    Rex.Media.IO.AtomicFile.Write(target, bytes);
                }
            });

            App.Log.Info(LogSource, "Backed the library up to " + folder.Path);
            Say("The library is backed up in " + folder.Path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            App.Log.Warning(LogSource, "The library could not be backed up: " + ex.Message);
            Say("The library could not be backed up there: " + ex.Message);
        }
    }

    /// <summary>Adds a backup's folders, play counts and playlists to what the library has (LIB-10).</summary>
    private async Task RestoreLibraryAsync()
    {
        var picker = Prepared(new FileOpenPicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary });
        picker.FileTypeFilter.Add(".json");
        if (await picker.PickSingleFileAsync() is not { } file)
        {
            return;
        }

        try
        {
            var summary = LibraryBackup.Restore(LibraryBackup.Read(await File.ReadAllBytesAsync(file.Path)), _library!, _player);
            WatchLibraryFolders();
            _scanner!.Request();
            App.Log.Info(LogSource, $"Brought back {file.Path}: {summary}.");
            Say($"Brought back {LibraryActions.Count(summary.Folders, "folder")}, {LibraryActions.Count(summary.Entries, "file")} and {LibraryActions.Count(summary.Playlists, "playlist")}");
        }
        catch (Exception ex) when (ex is FormatException or IOException or UnauthorizedAccessException)
        {
            Say(ex.Message);
        }
    }
}
