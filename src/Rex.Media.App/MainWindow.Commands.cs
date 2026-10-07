using System.Globalization;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Rex.Media.AppCore;
using Rex.Media.AppCore.Automation;
using Rex.Media.AppCore.Commands;
using Rex.Media.Codecs.MediaFoundation;
using Rex.Media.IO;
using Rex.Media.AppCore.Player;
using Rex.Media.Interop.Power;
using Rex.Media.Primitives;
using Rex.Media.Settings;
using Rex.Media.Video;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Pickers;

namespace Rex.Media.App;

public sealed partial class MainWindow
{
    private const string HelpUrl = "https://tochi-mba.github.io/rexplayer/";

    /// <summary>The menus, by menu, in order; each entry is a command id, or null for a separator.</summary>
    private static readonly (string Menu, string?[] Commands)[] MenuLayout =
    [
        ("Media", [CommandCatalog.OpenFile, CommandCatalog.OpenFolder, CommandCatalog.OpenLocation, CommandCatalog.PasteLocation, null, CommandCatalog.Quit]),
        ("Playback", [CommandCatalog.PlayPause, CommandCatalog.Stop, CommandCatalog.Previous, CommandCatalog.Next, null,
            CommandCatalog.JumpForwardShort, CommandCatalog.JumpBackShort, CommandCatalog.JumpForwardMedium, CommandCatalog.JumpBackMedium, CommandCatalog.GoToTime, null,
            CommandCatalog.CycleRepeat, CommandCatalog.ToggleShuffle, CommandCatalog.ShowPosition]),
        ("Audio", [CommandCatalog.CycleAudioTrack, null, CommandCatalog.VolumeUp, CommandCatalog.VolumeDown, CommandCatalog.Mute]),
        ("Video", [CommandCatalog.ToggleFullScreen, null, CommandCatalog.CycleAspectRatio, CommandCatalog.CycleCrop, null, CommandCatalog.ScaleQuarter, CommandCatalog.ScaleHalf, CommandCatalog.ScaleOriginal, CommandCatalog.ScaleDouble, null,
            CommandCatalog.Snapshot, CommandCatalog.ToggleStats, CommandCatalog.ToggleAlwaysOnTop]),
        ("View", [CommandCatalog.TogglePlaylist, CommandCatalog.ClearPlaylist, CommandCatalog.MinimalInterface, null, CommandCatalog.MediaInformation]),
        ("Help", [CommandCatalog.ShortcutSheet, CommandCatalog.Help]),
    ];

    private bool IsFullScreen => AppWindow.Presenter.Kind == AppWindowPresenterKind.FullScreen;

    private void BuildMenus()
    {
        foreach (var (title, commands) in MenuLayout)
        {
            var menu = Menu.Items.First(item => item.Title == title);
            menu.Items.Clear();
            foreach (var id in commands)
            {
                if (id is null)
                {
                    menu.Items.Add(new MenuFlyoutSeparator());
                    continue;
                }

                var command = CommandCatalog.Find(id)!;
                var item = new MenuFlyoutItem { Text = command.Title, KeyboardAcceleratorTextOverride = _keymap.Label(id) };
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(item, "Command-" + id);
                item.Click += (_, _) => Run(id);
                menu.Items.Add(item);
            }
        }
    }

    /// <summary>Runs a command: playback commands go to the player, the rest are the window's own.</summary>
    private void Run(string command)
    {
        if (_player.Execute(command))
        {
            return;
        }

        switch (command)
        {
            case CommandCatalog.ToggleFullScreen:
                SetFullScreen(!IsFullScreen);
                break;
            case CommandCatalog.LeaveFullScreen:
                SetFullScreen(false);
                break;
            case CommandCatalog.TogglePlaylist:
                var show = PlaylistPane.Visibility != Visibility.Visible;
                PlaylistPane.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
                PlaylistButton.IsChecked = show;
                RememberLater();
                break;
            case CommandCatalog.ClearPlaylist:
                _player.Stop();
                _player.Playlist.Clear();
                break;
            case CommandCatalog.MinimalInterface:
                _settings = _settings with { MinimalInterface = !_settings.MinimalInterface };
                ApplyMinimalInterface();
                RememberLater();
                break;
            case CommandCatalog.ToggleAlwaysOnTop:
                _settings = _settings with { AlwaysOnTop = _settings.AlwaysOnTop == AlwaysOnTop.Never ? AlwaysOnTop.Always : AlwaysOnTop.Never };
                ApplyAlwaysOnTop();
                Say(_settings.AlwaysOnTop == AlwaysOnTop.Never ? "Always on top: off" : "Always on top: on");
                RememberLater();
                break;
            case CommandCatalog.ScaleQuarter:
                ScaleToVideo(0.25);
                break;
            case CommandCatalog.ScaleHalf:
                ScaleToVideo(0.5);
                break;
            case CommandCatalog.ScaleOriginal:
                ScaleToVideo(1);
                break;
            case CommandCatalog.ScaleDouble:
                ScaleToVideo(2);
                break;
            case CommandCatalog.CycleAspectRatio:
                _aspect = VideoGeometry.Next(VideoGeometry.AspectRatios, _aspect);
                ApplyShape();
                Say("Aspect ratio: " + _aspect.Name);
                break;
            case CommandCatalog.CycleCrop:
                _crop = VideoGeometry.Next(VideoGeometry.Crops, _crop);
                ApplyShape();
                Say("Crop: " + _crop.Name);
                break;
            case CommandCatalog.Snapshot:
                _ = SnapshotAsync();
                break;
            case CommandCatalog.ToggleStats:
                _settings = _settings with { StatsOverlay = !_settings.StatsOverlay };
                ApplyStatsOverlay();
                RememberLater();
                break;
            case CommandCatalog.OpenFile:
                _ = OpenFilesAsync();
                break;
            case CommandCatalog.OpenFolder:
                _ = OpenFolderAsync();
                break;
            case CommandCatalog.OpenLocation:
                _ = OpenLocationAsync();
                break;
            case CommandCatalog.PasteLocation:
                _ = PasteAsync();
                break;
            case CommandCatalog.GoToTime:
                _ = GoToTimeAsync();
                break;
            case CommandCatalog.ShortcutSheet:
                _ = ShowShortcutsAsync();
                break;
            case CommandCatalog.MediaInformation:
                _ = ShowMediaInformationAsync();
                break;
            case CommandCatalog.Help:
                _ = Windows.System.Launcher.LaunchUriAsync(new Uri(HelpUrl));
                break;
            case CommandCatalog.Quit:
                Close();
                break;
        }
    }

    private void SetFullScreen(bool fullScreen)
    {
        if (fullScreen == IsFullScreen)
        {
            return;
        }

        AppWindow.SetPresenter(fullScreen ? AppWindowPresenterKind.FullScreen : AppWindowPresenterKind.Overlapped);
        FullScreenIcon.Glyph = fullScreen ? "\uE73F" : "\uE740";

        // In full screen the controls float over the bottom of the picture instead of taking a row of their own.
        Grid.SetRow(Controls, fullScreen ? 1 : 2);
        Controls.Background = fullScreen ? new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(0xC0, 0x08, 0x0A, 0x09)) : null;
        Controls.RequestedTheme = fullScreen ? ElementTheme.Dark : ElementTheme.Default;
        if (fullScreen)
        {
            Menu.Visibility = Visibility.Collapsed;
            ShowFullScreenControls();
        }
        else
        {
            _controlsTimer.Stop();
            ApplyMinimalInterface();
            ApplyAlwaysOnTop();
        }
    }

    private void ShowFullScreenControls()
    {
        Controls.Visibility = Visibility.Visible;
        _controlsTimer.Stop();
        _controlsTimer.Start();
    }

    private void HideFullScreenControls()
    {
        if (IsFullScreen && !Controls.FocusState.HasFlag(FocusState.Keyboard))
        {
            Controls.Visibility = Visibility.Collapsed;
        }
    }

    private void ApplyMinimalInterface()
    {
        var chrome = _settings.MinimalInterface || IsFullScreen ? Visibility.Collapsed : Visibility.Visible;
        Menu.Visibility = chrome;
        Controls.Visibility = chrome;
    }

    private void ApplyAlwaysOnTop()
    {
        if (AppWindow.Presenter is OverlappedPresenter overlapped)
        {
            overlapped.IsAlwaysOnTop = _settings.AlwaysOnTop == AlwaysOnTop.Always
                || (_settings.AlwaysOnTop == AlwaysOnTop.WhilePlaying && _player.IsPlaying);
        }
    }

    /// <summary>Sizes the window so the picture shows at <paramref name="factor"/> times its own size, pixel for pixel.</summary>
    private void ScaleToVideo(double factor)
    {
        if (_player.Info?.FirstTrack(MediaKind.Video)?.Video is not { } video || IsFullScreen)
        {
            return;
        }

        if (AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Maximized } overlapped)
        {
            overlapped.Restore();
        }

        // The picture's shape on screen: square pixels, turned the way the file says to show it.
        var (displayWidth, displayHeight) = (video.Width * video.PixelAspect.Value, (double)video.Height);
        if (video.Rotation is 90 or 270)
        {
            (displayWidth, displayHeight) = (displayHeight, displayWidth);
        }

        var scale = Video.CompositionScaleX;
        var chromeHeight = (Content.XamlRoot.Size.Height - Video.ActualHeight) * scale;
        var chromeWidth = (Content.XamlRoot.Size.Width - Video.ActualWidth) * scale;
        AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(
            (int)Math.Round((displayWidth * factor) + chromeWidth),
            (int)Math.Round((displayHeight * factor) + chromeHeight)));
        Say(string.Create(CultureInfo.InvariantCulture, $"{factor * 100:0} %"));
    }

    /// <summary>Video keeps the screen on; sound alone only keeps the PC awake (VID-29).</summary>
    private void KeepAwake(bool display, bool system)
    {
        if (OperatingSystem.IsWindows())
        {
            PowerRequests.Hold(display, system);
        }

        if (_settings.AlwaysOnTop == AlwaysOnTop.WhilePlaying)
        {
            ApplyAlwaysOnTop();
        }
    }

    /// <summary>Saves the picture on screen, at the source's own size and without the overlays, to Pictures\rexplayer.</summary>
    private async Task SnapshotAsync()
    {
        if (_player.Item is not { } item || !HasVideo)
        {
            Say("There is no picture to take.");
            return;
        }

        var at = _player.Position;
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "rexplayer");
        var output = Snapshot.FileFor(folder, _player.Title, at);
        try
        {
            var saved = await Task.Run(() =>
            {
                using var source = new FileByteSource(item.Location);
                return Snapshot.SaveAsPng(source, MediaRegistries.Decoders(new MfDecoderFactory()), new MediaTime(at.Ticks), output, CancellationToken.None);
            });
            Say("Snapshot saved: " + Path.GetFileName(saved.Path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or MediaFormatException or NotSupportedException)
        {
            Say("The snapshot could not be taken: " + ex.Message);
        }
    }

    /// <summary>Hands the chosen aspect ratio and crop to the presenter, redrawing at once when paused.</summary>
    private void ApplyShape()
    {
        var (aspect, crop, redraw) = (_aspect.Ratio, _crop.Ratio, !_player.IsPlaying);
        OnPresenterThread(presenter =>
        {
            presenter.SetShape(aspect, crop);
            if (redraw)
            {
                presenter.Redraw();
            }
        });
    }

    /// <summary>Shows or hides the statistics, refreshed every second while shown.</summary>
    private void ApplyStatsOverlay()
    {
        StatsBox.Visibility = _settings.StatsOverlay ? Visibility.Visible : Visibility.Collapsed;
        if (_settings.StatsOverlay)
        {
            _statsTimer.Start();
            _ = RefreshStatsAsync();
        }
        else
        {
            _statsTimer.Stop();
        }
    }

    private async Task RefreshStatsAsync()
    {
        var stats = await _player.StatsAsync();
        var now = DateTime.UtcNow;
        Stats.Text = stats is null ? "Nothing is playing." : StatsText.Describe(stats, _player.Info, _player.AudioTrack, _lastStats, now - _lastStatsAt);
        (_lastStats, _lastStatsAt) = (stats, now);
    }

    /// <summary>
    /// Answers the automation pipe (TOOL-03): a later launch's files, a command by id, or the state.
    /// Called on the pipe's thread, so the work is done on the window's.
    /// </summary>
    public Task<AutomationReply> AnswerAsync(AutomationRequest request)
    {
        var answered = new TaskCompletionSource<AutomationReply>(TaskCreationOptions.RunContinuationsAsynchronously);
        var queued = DispatcherQueue.TryEnqueue(() =>
        {
            switch (request.Command)
            {
                case "open":
                    if (request.Paths.Count > 0)
                    {
                        _player.Open(request.Paths, request.Enqueue);
                    }

                    ComeForward();
                    break;
                case "run" when request.Id is { } id && CommandCatalog.Find(id) is not null:
                    Run(id);
                    break;
                case "run":
                    answered.SetResult(AutomationReply.Failed($"There is no command \"{request.Id}\"."));
                    return;
                case "status":
                    break;
                default:
                    answered.SetResult(AutomationReply.Failed($"\"{request.Command}\" is not a request; try open, run or status."));
                    return;
            }

            answered.SetResult(new AutomationReply
            {
                Ok = true,
                State = _player.State.ToString(),
                Title = _player.Item is null ? null : _player.Title,
                Position = _player.Position.TotalSeconds,
                Duration = _player.Duration > TimeSpan.Zero ? _player.Duration.TotalSeconds : null,
            });
        });
        return queued ? answered.Task : Task.FromResult(AutomationReply.Failed("The player is closing."));
    }

    /// <summary>Brings the window back from the taskbar and to the front.</summary>
    private void ComeForward()
    {
        if (AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized } overlapped)
        {
            overlapped.Restore();
        }

        Activate();
    }

    private T Prepared<T>(T picker)
    {
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
        return picker;
    }

    private async Task OpenFilesAsync()
    {
        var picker = Prepared(new FileOpenPicker { SuggestedStartLocation = PickerLocationId.VideosLibrary, ViewMode = PickerViewMode.List });
        picker.FileTypeFilter.Add("*");
        var files = await picker.PickMultipleFilesAsync();
        if (files.Count > 0)
        {
            _player.Open(PathsOf(files));
        }
    }

    private async Task OpenFolderAsync()
    {
        var picker = Prepared(new FolderPicker { SuggestedStartLocation = PickerLocationId.MusicLibrary });
        picker.FileTypeFilter.Add("*");
        if (await picker.PickSingleFolderAsync() is { } folder)
        {
            _player.Open([folder.Path]);
        }
    }

    private async Task OpenLocationAsync()
    {
        var box = new TextBox { PlaceholderText = @"A file or folder, such as C:\Music\Album", MinWidth = 420 };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(box, "LocationBox");
        if (await Ask("Open a location", box, "Open") && box.Text.Trim().Trim('"') is { Length: > 0 } location)
        {
            _player.Open([location]);
        }
    }

    /// <summary>Plays files copied in Explorer, or a path copied as text.</summary>
    private async Task PasteAsync()
    {
        var content = Clipboard.GetContent();
        if (content.Contains(StandardDataFormats.StorageItems))
        {
            _player.Open(PathsOf(await content.GetStorageItemsAsync()));
        }
        else if (content.Contains(StandardDataFormats.Text) && (await content.GetTextAsync()).Trim().Trim('"') is { Length: > 0 } text)
        {
            _player.Open([text]);
        }
    }

    private async Task GoToTimeAsync()
    {
        if (!_player.CanSeek)
        {
            return;
        }

        var box = new TextBox { Text = TimeText.Format(_player.Position, _player.Duration), MinWidth = 200 };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(box, "GoToTimeBox");
        box.SelectAll();
        if (await Ask("Go to a time", box, "Go"))
        {
            if (TimeText.TryParse(box.Text) is { } time)
            {
                _player.Seek(time);
            }
            else
            {
                Say($"\"{box.Text}\" is not a time. Try 1:23 or 1:02:03.");
            }
        }
    }

    private async Task ShowShortcutsAsync()
    {
        var list = new StackPanel { Spacing = 2 };
        foreach (var group in CommandCatalog.All.GroupBy(command => command.Group))
        {
            list.Children.Add(new TextBlock { Text = group.Key, Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"], Margin = new Thickness(0, 8, 0, 2) });
            foreach (var command in group)
            {
                var keys = string.Join(", ", _keymap.ShortcutsFor(command.Id));
                list.Children.Add(new TextBlock { Text = keys.Length > 0 ? $"{command.Title}: {keys}" : command.Title });
            }
        }

        await Ask("Keyboard shortcuts", new ScrollViewer { Content = list, MaxHeight = 480 }, null);
    }

    private async Task ShowMediaInformationAsync()
    {
        if (_player.Info is not { } info)
        {
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
            var detail = track.Video is { } v ? $"{v.Width}\u00D7{v.Height}" : track.Audio is { } a ? $"{a.SampleRate} Hz, {a.Channels} channel(s)" : "";
            lines.Add($"Track {track.Id}: {track.Kind} {track.Codec.DisplayName()} {detail}".TrimEnd());
        }

        foreach (var (key, value) in info.Metadata)
        {
            lines.Add($"{key}: {value}");
        }

        var text = new TextBlock { Text = string.Join(Environment.NewLine, lines), IsTextSelectionEnabled = true, TextWrapping = TextWrapping.Wrap };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(text, "MediaInformationText");
        await Ask("Media information", new ScrollViewer { Content = text, MaxHeight = 480 }, null);
    }

    /// <summary>An in-window dialog; true when the user chose <paramref name="primary"/>.</summary>
    private async Task<bool> Ask(string title, UIElement content, string? primary)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = content,
            CloseButtonText = primary is null ? "Close" : "Cancel",
            XamlRoot = Content.XamlRoot,
        };
        if (primary is not null)
        {
            dialog.PrimaryButtonText = primary;
            dialog.DefaultButton = ContentDialogButton.Primary;
        }

        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }
}
