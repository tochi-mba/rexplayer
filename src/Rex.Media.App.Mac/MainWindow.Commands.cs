using Rex.Media.AppCore.Commands;
using Rex.Media.AppCore.Player;
using Rex.Media.Primitives;

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

        using var box = new NSTextField(new CGRect(0, 0, 240, 24))
        {
            StringValue = TimeText.Format(_player.Position, _player.Duration),
        };
        using var dialog = new NSAlert
        {
            MessageText = "Go to a time",
            InformativeText = "Enter seconds, mm:ss or h:mm:ss.",
            AccessoryView = box,
        };
        dialog.AddButton("Go");
        dialog.AddButton("Cancel");
        _window.MakeFirstResponder(box);
        box.SelectText(null);
        if (dialog.RunModal() != 1000)
        {
            return;
        }

        if (TimeText.TryParse(box.StringValue) is { } time)
        {
            _player.Seek(time);
        }
        else
        {
            Say($"\"{box.StringValue}\" is not a time. Try 1:23 or 1:02:03.");
        }
    }

    private void ShowMediaInformation()
    {
        if (_player.Info is not { } info)
        {
            Say("Open something to see its media information.");
            return;
        }

        var lines = new List<string>
        {
            $"Title: {_player.Title}",
            $"Location: {_player.Item?.Location}",
            $"Format: {info.FormatName}",
            $"Length: {(_player.Duration > TimeSpan.Zero ? TimeText.Format(_player.Duration) : "unknown")}",
        };
        foreach (var track in info.Tracks)
        {
            var detail = track.Video is { } video ? $"{video.Width}×{video.Height}"
                : track.Audio is { } audio ? $"{audio.SampleRate} Hz, {audio.Channels} channel(s)"
                : "";
            lines.Add($"Track {track.Id}: {track.Kind} {track.Codec.DisplayName()} {detail}".TrimEnd());
        }

        lines.AddRange(info.Metadata.Select(metadata => $"{metadata.Key}: {metadata.Value}"));
        using var dialog = new NSAlert
        {
            MessageText = "Media information",
            InformativeText = string.Join(Environment.NewLine, lines),
        };
        dialog.AddButton("OK");
        dialog.RunModal();
    }

    private void OpenWithPanel(bool folders)
    {
        var panel = NSOpenPanel.OpenPanel;
        panel.AllowsMultipleSelection = true;
        panel.CanChooseFiles = !folders;
        panel.CanChooseDirectories = folders;
        if (panel.RunModal() == 1)
        {
            _player.Open(panel.Urls.Select(url => url.Path!).ToList());
        }
    }

    /// <summary>Opens a typed file or network stream address through the shared source factory.</summary>
    private void OpenLocationDialog()
    {
        using var entry = new NSTextField(new CGRect(0, 0, 380, 24))
        {
            PlaceholderString = "https://example.org/media.mp4 or file path",
        };
        using var dialog = new NSAlert
        {
            MessageText = "Open a network or file location",
            InformativeText = "Enter a file path or a stream URL. rexplayer opens the address only when you choose Open.",
            AccessoryView = entry,
        };
        dialog.AddButton("Open");
        dialog.AddButton("Cancel");
        if (dialog.RunModal() == 1000)
        {
            OpenTypedLocation(entry.StringValue);
        }
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
        if (panel.RunModal() == 1 && panel.Url?.Path is { } path)
        {
            _player.AddSubtitles(path);
        }
    }

}
