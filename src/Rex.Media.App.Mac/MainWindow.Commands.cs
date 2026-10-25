using Rex.Media.AppCore.Commands;
using Rex.Media.AppCore.Player;

namespace Rex.Media.App.Mac;

/// <summary>Commands requiring native panels, menus and window state.</summary>
public sealed partial class MainWindow
{
    private bool RunWindowCommand(string command)
    {
        switch (command)
        {
            case CommandCatalog.OpenFile or CommandCatalog.OpenFolder:
                OpenWithPanel(folders: command == CommandCatalog.OpenFolder);
                return true;
            case CommandCatalog.OpenLocation:
                OpenLocationDialog();
                return true;
            case CommandCatalog.PasteLocation:
                OpenPastedLocation();
                return true;
            case CommandCatalog.TogglePlaylist:
                if (_queueWindow?.IsVisible == true)
                {
                    _queueWindow.OrderOut(null);
                }
                else
                {
                    ShowQueue();
                }

                return true;
            case CommandCatalog.Quit:
                NSApplication.SharedApplication.Terminate(null);
                return true;
            case CommandCatalog.ToggleLibrary:
                if (_libraryWindow?.IsVisible == true)
                {
                    HideLibrary();
                }
                else
                {
                    ShowLibrary();
                }

                return true;
            case CommandCatalog.ToggleFullScreen:
                _window.ToggleFullScreen(null);
                return true;
            case CommandCatalog.LeaveFullScreen:
                if (IsFullScreen)
                {
                    _window.ToggleFullScreen(null);
                }

                return true;
            case CommandCatalog.ToggleAlwaysOnTop:
                _window.Level = _window.Level == NSWindowLevel.Floating ? NSWindowLevel.Normal : NSWindowLevel.Floating;
                return true;
            case CommandCatalog.CycleAspectRatio:
                CycleVideoAspect();
                return true;
            case CommandCatalog.CycleCrop:
                CycleVideoCrop();
                return true;
            case CommandCatalog.ZoomIn:
                ChangeVideoZoom(1.25);
                return true;
            case CommandCatalog.ZoomOut:
                ChangeVideoZoom(1 / 1.25);
                return true;
            case CommandCatalog.ResetZoom:
                ResetVideoZoom();
                return true;
            case CommandCatalog.PanLeft:
                PanVideo(-0.15, 0);
                return true;
            case CommandCatalog.PanRight:
                PanVideo(+0.15, 0);
                return true;
            case CommandCatalog.PanUp:
                PanVideo(0, -0.15);
                return true;
            case CommandCatalog.PanDown:
                PanVideo(0, +0.15);
                return true;
            case CommandCatalog.ScaleQuarter:
                SizeWindowToVideo(0.25);
                return true;
            case CommandCatalog.ScaleHalf:
                SizeWindowToVideo(0.5);
                return true;
            case CommandCatalog.ScaleOriginal:
                SizeWindowToVideo(1);
                return true;
            case CommandCatalog.ScaleDouble:
                SizeWindowToVideo(2);
                return true;
            case CommandCatalog.MinimalInterface:
                _minimalInterface = !_minimalInterface;
                if (_chrome is not null)
                {
                    _chrome.Hidden = _minimalInterface;
                }

                return true;
            case CommandCatalog.GoToTime:
                GoToTime();
                return true;
            case CommandCatalog.AddSubtitles:
                AddSubtitleWithPanel();
                return true;
            case CommandCatalog.SubtitlesBigger:
                ChangeSubtitleScale(+25);
                return true;
            case CommandCatalog.SubtitlesSmaller:
                ChangeSubtitleScale(-25);
                return true;
            case CommandCatalog.ResetSubtitleSize:
                ChangeSubtitleScale(0);
                return true;
            case CommandCatalog.Preferences:
                ShowPreferences();
                return true;
            case CommandCatalog.KeyboardAndMouse:
                ShowShortcutEditor();
                return true;
            case CommandCatalog.SavePlaylist:
                SavePlaylistWithPanel();
                return true;
            case CommandCatalog.OpenPlaylist:
                OpenPlaylistWithPanel();
                return true;
            case CommandCatalog.Effects:
                ShowEffects();
                return true;
            case CommandCatalog.Snapshot:
                SaveVideoSnapshot();
                return true;
            case CommandCatalog.ToggleStats:
                ToggleStatsOverlay();
                return true;
            case CommandCatalog.ToggleLyrics:
                ToggleMusicLyrics();
                return true;
            case CommandCatalog.CycleVisualizer:
                CycleMusicVisualizer();
                return true;
            case CommandCatalog.BackUpLibrary:
                BackUpLibrary();
                return true;
            case CommandCatalog.RestoreLibrary:
                RestoreLibraryBackup();
                return true;
            case CommandCatalog.ToggleKineticCaptions:
                ToggleMacKinetic();
                return true;
            case CommandCatalog.KineticCaptionSettings:
                ShowMacKineticSettings();
                return true;
            case CommandCatalog.KineticCaptionsLivelier:
                ChangeMacCaptionEnergy(+25);
                return true;
            case CommandCatalog.KineticCaptionsCalmer:
                ChangeMacCaptionEnergy(-25);
                return true;
            case CommandCatalog.CycleCaptionLayout:
                CycleMacCaptionLayout();
                return true;
            case CommandCatalog.CycleCaptionWords:
                CycleMacCaptionSource();
                return true;
            case CommandCatalog.CycleCaptionStyle:
                CycleMacCaptionPreset();
                return true;
            case CommandCatalog.Transcribe:
                AskMacSpeech();
                return true;
            case CommandCatalog.CheckForUpdates:
                ShowMacUpdates();
                CheckForMacUpdates();
                return true;
            case CommandCatalog.WhatsNew:
                ShowWhatsNew();
                return true;
            case CommandCatalog.MediaInformation:
                ShowMediaInformation();
                return true;
            case CommandCatalog.Help:
                NSWorkspace.SharedWorkspace.OpenUrl(new NSUrl(HelpUrl));
                return true;
            case CommandCatalog.ShowLog:
                ShowLogConsole();
                return true;
            case CommandCatalog.OpenLogFolder:
                Directory.CreateDirectory(_roots.LogFolder);
                NSWorkspace.SharedWorkspace.OpenUrl(NSUrl.FromFilename(_roots.LogFolder));
                return true;
            case CommandCatalog.SaveDiagnostics:
                SaveDiagnostics();
                return true;
            case CommandCatalog.About:
                NSApplication.SharedApplication.OrderFrontStandardAboutPanel(null);
                return true;
            default:
                return false;
        }
    }

    private bool IsFullScreen => (_window.StyleMask & NSWindowStyle.FullScreenWindow) != 0;

    private void GoToTime()
    {
        if (!_player.CanSeek)
        {
            if (_player.Item is not null)
            {
                Say(PlayerController.CannotSeekNotice);
            }

            return;
        }

        RequestEntryInWindow("Go to a time", TimeText.Format(_player.Position, _player.Duration),
            "Seconds, mm:ss or h:mm:ss", value =>
            {
                if (TimeText.TryParse(value) is { } time) _player.Seek(time);
                else Say($"\"{value}\" is not a time. Try 1:23 or 1:02:03.");
            });
    }

    private void OpenWithPanel(bool folders)
    {
        var panel = NSOpenPanel.OpenPanel;
        panel.AllowsMultipleSelection = true;
        panel.CanChooseFiles = !folders;
        panel.CanChooseDirectories = folders;
        panel.Begin(response =>
        {
            if (response == 1) _player.Open(panel.Urls.Select(url => url.Path!).ToList());
        });
    }

    /// <summary>Opens a typed file or network stream address through the shared source factory.</summary>
    private void OpenLocationDialog()
    {
        RequestEntryInWindow("Open a local path or media URL", "",
            "File path or stream URL", OpenTypedLocation);
    }

    private void OpenPastedLocation()
    {
        var value = NSPasteboard.GeneralPasteboard.GetStringForType(NSPasteboardType.String.GetConstant()!.ToString());
        if (string.IsNullOrWhiteSpace(value))
        {
            Say("The clipboard does not contain a file path or address.");
            return;
        }

        OpenTypedLocation(value);
    }

    private void OpenTypedLocation(string value)
    {
        var address = value.Trim();
        if (string.IsNullOrWhiteSpace(address) || address.IndexOfAny([(char)13, (char)10, (char)0]) >= 0)
        {
            Say("Enter one file path or address.");
            return;
        }

        _player.Open([address]);
    }

    private void AddSubtitleWithPanel()
    {
        var panel = NSOpenPanel.OpenPanel;
        panel.AllowsMultipleSelection = false;
        panel.CanChooseFiles = true;
        panel.CanChooseDirectories = false;
        panel.Title = "Add a subtitle file";
        panel.Begin(response =>
        {
            if (response == 1 && panel.Url?.Path is { } path) _player.AddSubtitles(path);
        });
    }

}
