using Rex.Media.AppCore.Commands;
using Rex.Media.AppCore.Player;
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
                Forget(SavePlaylistAsync());
                return true;
            case CommandCatalog.OpenPlaylist:
                Forget(OpenPlaylistAsync());
                return true;
            default:
                return false;
        }
    }

    /// <summary>Adjusts the dwell time for subsequent pictures without restarting the current image or queue.</summary>
    private void OnPictureIntervalClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        // A flyout action queued before a stream switch must not mutate
        // picture-specific playback settings while a different medium plays.
        if (_stageView.Profile != MediaStageProfile.Picture
            || _player.Item?.Location is not { } location || !MediaFiles.IsPicture(location))
        {
            return;
        }

        if (sender is not Microsoft.UI.Xaml.Controls.MenuFlyoutItem { Tag: string choice }
            || !int.TryParse(choice, System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out var seconds))
        {
            return;
        }

        _settings = (_settings with { PictureSeconds = seconds }).Normalize();
        _player.Settings = _settings;
        PictureIntervalButton.Content = $"Slideshow · {_settings.PictureSeconds} s";
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(PictureIntervalButton, $"Slideshow interval, {_settings.PictureSeconds} seconds per picture");
        Say($"Pictures will show for {_settings.PictureSeconds} seconds each starting with the next picture");
        RememberLater();
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
