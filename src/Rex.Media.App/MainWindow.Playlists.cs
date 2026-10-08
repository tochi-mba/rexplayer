using Rex.Media.AppCore.Commands;
using Rex.Media.Library.Playlists;
using Windows.Storage.Pickers;

namespace Rex.Media.App;

/// <summary>Saving the playlist as a file, and opening one (LIB-03).</summary>
public sealed partial class MainWindow
{
    private bool RunPlaylistCommand(string command)
    {
        switch (command)
        {
            case CommandCatalog.SavePlaylist:
                _ = SavePlaylistAsync();
                return true;
            case CommandCatalog.OpenPlaylist:
                _ = OpenPlaylistAsync();
                return true;
            default:
                return false;
        }
    }

    private async Task SavePlaylistAsync()
    {
        if (_player.Playlist.Items.Count == 0)
        {
            Say("The playlist is empty.");
            return;
        }

        var picker = Prepared(new FileSavePicker { SuggestedStartLocation = PickerLocationId.MusicLibrary, SuggestedFileName = "Playlist" });
        picker.FileTypeChoices.Add("M3U8 playlist", [".m3u8"]);
        picker.FileTypeChoices.Add("XSPF playlist", [".xspf"]);
        picker.FileTypeChoices.Add("PLS playlist", [".pls"]);
        if (await picker.PickSaveFileAsync() is { } file)
        {
            _player.SavePlaylist(file.Path);
        }
    }

    private async Task OpenPlaylistAsync()
    {
        var picker = Prepared(new FileOpenPicker { SuggestedStartLocation = PickerLocationId.MusicLibrary, ViewMode = PickerViewMode.List });
        foreach (var extension in PlaylistFiles.Extensions)
        {
            picker.FileTypeFilter.Add(extension);
        }

        if (await picker.PickSingleFileAsync() is { } file)
        {
            _player.Open([file.Path]);
        }
    }
}
