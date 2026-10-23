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

    private bool IsFullScreen => AppWindow.Presenter.Kind == AppWindowPresenterKind.FullScreen;

    /// <summary>The current rate remains visible while the player is paused or changing items.</summary>
    private void ShowSpeed()
    {
        var label = SpeedLabel(_player.Speed);
        SpeedButton.Content = label;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(SpeedButton, "Playback speed, " + label);
        var keys = string.Join(" and ", new[] { _keymap.Label(CommandCatalog.Faster), _keymap.Label(CommandCatalog.Slower) }.Where(key => key.Length > 0));
        ToolTipService.SetToolTip(SpeedButton, "Playback speed: " + label + ". Choose a rate" + (keys.Length > 0 ? ", or use " + keys + " to change it." : "."));
    }

    private static string SpeedLabel(double speed) => speed.ToString("0.##", CultureInfo.InvariantCulture) + "\u00D7";

    /// <summary>Offer slower and faster speeds in one place, including one-touch return to normal.</summary>
    private void OnSpeedButton(object sender, RoutedEventArgs e)
    {
        var menu = new MenuFlyout();

        void AddCommand(string name, string id, string command)
        {
            var item = new MenuFlyoutItem { Text = name };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(item, id);
            item.Click += (_, _) => Run(command);
            menu.Items.Add(item);
        }

        AddCommand("Slower", "SpeedSlower", CommandCatalog.Slower);
        AddCommand("Normal speed (1\u00D7)", "SpeedNormal", CommandCatalog.NormalSpeed);
        AddCommand("Faster", "SpeedFaster", CommandCatalog.Faster);
        menu.Items.Add(new MenuFlyoutSeparator());

        foreach (var rate in PlayerController.SpeedSteps)
        {
            var label = SpeedLabel(rate);
            var chosen = Math.Abs(rate - _player.Speed) < 0.005;
            var item = new RadioMenuFlyoutItem { Text = label, GroupName = "SpeedRates", IsChecked = chosen };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(item, "SpeedRate-" + rate.ToString("0.##", CultureInfo.InvariantCulture));
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(item, chosen ? label + ", current speed" : label);
            item.Click += (_, _) => _player.SetSpeed(rate);
            menu.Items.Add(item);
        }

        menu.ShowAt(SpeedButton);
    }

    private void BuildMenus()
    {
        _menuChecks.Clear();
        _videoLookItems.Clear();
        _videoEffectItems.Clear();
        _sectionMenuUpdates.Clear();
        foreach (var (title, commands) in MenuBarLayout.Menus)
        {
            var menu = Menu.Items.First(item => item.Title == title);
            menu.Items.Clear();
            Fill(menu.Items, commands, "Command-");
        }

        var context = new MenuFlyout();
        foreach (var (title, commands) in ContextMenuLayout.Groups)
        {
            if (title.Length == 0)
            {
                Fill(context.Items, commands, "Context-");
                continue;
            }

            var sub = new MenuFlyoutSubItem { Text = title };
            Fill(sub.Items, commands, "Context-");
            if (title == "Video")
            {
                AddPictureLooks(sub.Items, "ContextVideoLook-");
                AddPictureEffects(sub.Items, "ContextVideoEffect-");
                AddSubjectTools(sub.Items, "Context");
                AddEpisodeSectionMenus(sub.Items, "Context");
            }

            context.Items.Add(sub);
        }

        Stage.ContextFlyout = context;
        AddPictureLooks(Menu.Items.First(item => item.Title == "Video").Items, "VideoLook-");
        AddPictureEffects(Menu.Items.First(item => item.Title == "Video").Items, "VideoEffect-");
        AddSubjectTools(Menu.Items.First(item => item.Title == "Video").Items, "Video");
        AddEpisodeSectionMenus(Menu.Items.First(item => item.Title == "Video").Items, "Video");
        BuildMemoryMenus();
        BuildNamedPlaylistMenus();
        BuildTranscriptMenus();
    }

    private void Fill(IList<MenuFlyoutItemBase> items, string?[] commands, string prefix)
    {
        foreach (var id in commands)
        {
            if (id is null)
            {
                items.Add(new MenuFlyoutSeparator());
                continue;
            }

            if (MenuBarLayout.SubMenus.TryGetValue(id, out var group))
            {
                var sub = new MenuFlyoutSubItem { Text = group.Title };
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(sub, "Submenu-" + MenuBarLayout.SubMenuId(id));
                Fill(sub.Items, group.Commands, prefix);
                items.Add(sub);
                continue;
            }

            var command = CommandCatalog.Find(id)!;
            MenuFlyoutItem item = MenuBarLayout.IsOnOff(id) ? new ToggleMenuFlyoutItem { IsChecked = IsOn(id) } : new MenuFlyoutItem();
            (item.Text, item.KeyboardAcceleratorTextOverride) = (CommandCatalog.TitleOf(command, _settings), _keymap.Label(id));
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(item, prefix + id);
            item.Click += (_, _) => Run(id);
            items.Add(item);
            if (item is ToggleMenuFlyoutItem check)
            {
                _menuChecks.Add((check, id));
            }
        }
    }

    // Menu items that are on or off, ticked to match.
    private readonly List<(ToggleMenuFlyoutItem Item, string Command)> _menuChecks = [];

    /// <summary>Whether what <paramref name="command"/> switches is on now.</summary>
    private bool IsOn(string command) => command switch
    {
        CommandCatalog.ToggleShuffle => _player.Playlist.Shuffle,
        CommandCatalog.CycleRepeat => _player.Playlist.Repeat != RepeatMode.Off,
        CommandCatalog.Mute => _player.Muted,
        CommandCatalog.ToggleSubtitles => _player.Subtitles is not null,
        CommandCatalog.ToggleNavigator => _settings.ShowNavigator,
        CommandCatalog.StopAfterCurrent => _player.AfterCurrent == AfterItem.Stop,
        CommandCatalog.PauseAfterCurrent => _player.AfterCurrent == AfterItem.Pause,
        CommandCatalog.SleepAtEndOfItem => _player.SleepAtEndOfItem,
        CommandCatalog.ToggleFullScreen => IsFullScreen,
        CommandCatalog.ToggleAlwaysOnTop => _settings.AlwaysOnTop != AlwaysOnTop.Never,
        CommandCatalog.ToggleStats => _settings.StatsOverlay,
        CommandCatalog.TogglePlaylist => PlaylistPane.Visibility == Visibility.Visible,
        CommandCatalog.ToggleLyrics => _settings.ShowLyrics,
        CommandCatalog.ToggleLibrary => LibraryOpen,
        _ => _settings.MinimalInterface,
    };

    /// <summary>Ticks every on-and-off menu item to match what is on now: after every command and every change of the player.</summary>
    private void ShowMenuChecks()
    {
        foreach (var (item, command) in _menuChecks)
        {
            item.IsChecked = IsOn(command);
        }
    }

    /// <summary>Runs a command: playback commands go to the player, the rest are the window's own.</summary>
    private void Run(string command)
    {
        RunCommand(command);
        ShowMenuChecks();
        if (command == CommandCatalog.ClearHistory)
        {
            RefreshJumpListLater();
        }
    }

    private void RunCommand(string command)
    {
        if (_player.Execute(command))
        {
            return;
        }

        // Z follows the media in front of the user: pictures cycle picture effects, while
        // audio-only playback keeps cycling its visualisations.
        if (command == CommandCatalog.CycleVisualizer && HasVideo)
        {
            CycleVideoEffect();
            return;
        }

        App.Log.Debug(LogSource, "Command " + command);
        if (RunSubtitleCommand(command) || RunKineticCaptionCommand(command) || RunVisualizerCommand(command) || RunPlaylistCommand(command) || RunZoomCommand(command))
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
            case CommandCatalog.ToggleLibrary:
                SetLibraryOpen(!LibraryOpen);
                break;
            case CommandCatalog.BackUpLibrary:
                Forget(BackUpLibraryAsync());
                break;
            case CommandCatalog.RestoreLibrary:
                Forget(RestoreLibraryAsync());
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
                Forget(SnapshotAsync());
                break;
            case CommandCatalog.ToggleStats:
                _settings = _settings with { StatsOverlay = !_settings.StatsOverlay };
                ApplyStatsOverlay();
                RememberLater();
                break;
            case CommandCatalog.OpenFile:
                Forget(OpenFilesAsync());
                break;
            case CommandCatalog.OpenFolder:
                Forget(OpenFolderAsync());
                break;
            case CommandCatalog.OpenLocation:
                Forget(OpenLocationAsync());
                break;
            case CommandCatalog.PasteLocation:
                Forget(PasteAsync());
                break;
            case CommandCatalog.GoToTime:
                Forget(GoToTimeAsync());
                break;
            case CommandCatalog.ShortcutSheet:
                Forget(ShowShortcutsAsync());
                break;
            case CommandCatalog.MediaInformation:
                Forget(ShowMediaInformationAsync());
                break;
            case CommandCatalog.CheckForUpdates:
                Forget(CheckForUpdatesAsync(asked: true));
                break;
            case CommandCatalog.WhatsNew:
                Forget(ShowWhatsNewAsync());
                break;
            case CommandCatalog.About:
                Forget(ShowAboutAsync());
                break;
            case CommandCatalog.Effects:
                Forget(ShowEffectsAsync());
                break;
            case CommandCatalog.Preferences:
                Forget(ShowPreferencesAsync());
                break;
            case CommandCatalog.KeyboardAndMouse:
                Forget(ShowKeyboardAndMouseAsync());
                break;
            case CommandCatalog.ShowLog:
                Forget(ShowLogAsync());
                break;
            case CommandCatalog.OpenLogFolder:
                App.Log.Flush();
                Directory.CreateDirectory(App.LogFolder);
                _ = Windows.System.Launcher.LaunchFolderPathAsync(App.LogFolder);
                break;
            case CommandCatalog.SaveDiagnostics:
                Forget(SaveDiagnosticsAsync());
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
            // Full-screen uses its own timed transport, not the compact window controls.
            ApplyMinimalInterface();
            ShowFullScreenControls();
        }
        else
        {
            _controlsTimer.Stop();
            _pointerTimer.Stop();
            ShowPointer(true);
            ApplyMinimalInterface();
            ApplyAlwaysOnTop();
        }

        // The update card waits out full-screen playback and comes back on leaving it.
        ShowUpdates();
    }

    private void ShowFullScreenControls()
    {
        _pointerTimer.Stop();
        ShowPointer(true);
        Controls.Visibility = Visibility.Visible;
        _controlsTimer.Stop();
        _controlsTimer.Start();
    }

    private void HideFullScreenControls()
    {
        if (IsFullScreen && !FocusIsInside(Controls))
        {
            Controls.Visibility = Visibility.Collapsed;
            _pointerTimer.Stop();
            _pointerTimer.Start();
        }
    }

    /// <summary>Whether keyboard focus is on <paramref name="bar"/> or on anything inside it, such as one of its buttons.</summary>
    private bool FocusIsInside(UIElement bar)
    {
        if (Content?.XamlRoot is not { } root || Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(root) is not Control { FocusState: FocusState.Keyboard } focused)
        {
            return false;
        }

        return Rex.Media.AppCore.Shell.FocusWithin.IsInside<DependencyObject>(focused, bar, Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent);
    }

    /// <summary>The pointer is the last piece of full-screen chrome to leave.</summary>
    private void HideFullScreenPointer()
    {
        if (IsFullScreen && Controls.Visibility == Visibility.Collapsed)
        {
            ShowPointer(false);
            App.Log.Debug(LogSource, "Hid the pointer over the full-screen picture.");
        }
    }

    /// <summary>The pointer follows full-screen activity without obscuring a settled picture.</summary>
    private static void ShowPointer(bool shown)
    {
        if (OperatingSystem.IsWindows())
        {
            Rex.Media.Interop.Windowing.PointerVisibility.SetHidden(!shown);
        }
    }

    /// <summary>
    /// Compact window chrome without hiding the timeline, time labels, Play/Pause or volume.
    /// Full-screen controls still follow the separate auto-hide timer.
    /// </summary>
    private void ApplyMinimalInterface()
    {
        var minimal = _settings.MinimalInterface && !IsFullScreen;
        Menu.Visibility = minimal || IsFullScreen ? Visibility.Collapsed : Visibility.Visible;
        MinimalInterfaceButton.Visibility = minimal ? Visibility.Visible : Visibility.Collapsed;
        foreach (var secondary in new UIElement[]
        {
            StartOverButton, PreviousButton, StopButton, NextButton, ShuffleButton,
            RepeatButton, PlaylistButton, TransportSeparator,
        })
        {
            secondary.Visibility = minimal ? Visibility.Collapsed : Visibility.Visible;
        }

        Controls.Padding = minimal ? new Thickness(12, 3, 12, 5) : new Thickness(16, 10, 16, 12);
        Controls.RowSpacing = minimal ? 1 : 8;
        VolumeBar.Width = minimal ? 88 : 120;
        if (!IsFullScreen)
        {
            Controls.Visibility = Visibility.Visible;
        }
    }

    /// <summary>Restore the controls and keep keyboard focus on a visible playback action.</summary>
    private void OnRestoreFullInterface(object sender, RoutedEventArgs e)
    {
        Run(CommandCatalog.MinimalInterface);
        PlayPauseButton.Focus(FocusState.Programmatic);
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
                return Snapshot.SaveAsPng(source, MediaRegistries.Decoders(new MfDecoderFactory(), new WicDecoderFactory()), new MediaTime(at.Ticks), output, CancellationToken.None);
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
        ShowNavigator();
        var (aspect, crop, redraw) = (_aspect.Ratio, _crop.Ratio, !_player.IsPlaying);
        LayOutSubtitles();
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
            Forget(RefreshStatsAsync());
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
        if ((AnswerMeasurement(request) ?? AnswerFault(request)) is { } measured)
        {
            return measured;
        }

        var answered = new TaskCompletionSource<AutomationReply>(TaskCreationOptions.RunContinuationsAsynchronously);
        var queued = DispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                if (ShellAutomation.IsUiRequest(request.Command))
                {
                    Forget(AnswerUiAsync(request, answered));
                    return;
                }

                switch (request.Command)
                {
                    case "open":
                        if (request.Paths.Count > 0)
                        {
                            _player.OpenHandedOver(request.Paths, request.Enqueue, request.LaunchedAt ?? DateTimeOffset.Now);
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
                        answered.SetResult(AutomationReply.Failed($"\"{request.Command}\" is not a request; try open, run, seek, status, metrics, measure or a ui. command such as ui.tree."));
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
            }
            catch (Exception ex)
            {
                App.Log.Critical(LogSource, $"The automation request \"{request.Command}\" failed; the window carries on: {ex}");
                answered.SetResult(AutomationReply.Failed(ex.Message));
            }
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

    private void OnWelcomeOpen(object sender, Microsoft.UI.Xaml.RoutedEventArgs e) => Forget(OpenFilesAsync());

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
        var box = new TextBox { PlaceholderText = @"A local file or folder, such as C:\Music\Album", MinWidth = 420 };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(box, "LocationBox");
        if (await Ask("Open a local location", box, "Open") && box.Text.Trim().Trim('"') is { Length: > 0 } location)
        {
            var (supported, _) = MediaFiles.FilterClipboardPaths([location], Directory.Exists);
            if (supported.Count == 0)
            {
                Say("Only local media files and folders can be opened. Network streams and remote locations are not supported.");
                return;
            }

            if (!File.Exists(location) && !Directory.Exists(location))
            {
                Say("That local file or folder could not be found.");
                return;
            }

            _player.Open(supported);
        }
    }

    /// <summary>Plays files copied in Explorer, or a path copied as text.</summary>
    private async Task PasteAsync()
    {
        var content = Clipboard.GetContent();
        if (content.Contains(StandardDataFormats.StorageItems))
        {
            var (supported, skipped) = MediaFiles.FilterClipboardPaths(PathsOf(await content.GetStorageItemsAsync()), Directory.Exists);
            if (supported.Count == 0)
            {
                Say("The clipboard has no media rexplayer can open.");
                return;
            }

            if (skipped > 0)
            {
                Say($"Skipped {skipped} clipboard item{(skipped == 1 ? "" : "s")} that rexplayer cannot open.");
            }

            _player.Open(supported);
        }
        else if (content.Contains(StandardDataFormats.Text))
        {
            var (supported, skipped) = MediaFiles.FilterClipboardText(await content.GetTextAsync(), Directory.Exists);
            if (supported.Count == 0)
            {
                Say("The clipboard has no media rexplayer can open.");
                return;
            }

            if (skipped > 0)
            {
                Say($"Skipped {skipped} clipboard item{(skipped == 1 ? "" : "s")} that rexplayer cannot open.");
            }

            _player.Open(supported);
        }
    }

    private async Task GoToTimeAsync()
    {
        if (!_player.CanSeek)
        {
            if (_player.Item is not null)
            {
                Say(PlayerController.CannotSeekNotice);
            }

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
                var title = CommandCatalog.TitleOf(command, _settings);
                list.Children.Add(new TextBlock { Text = keys.Length > 0 ? $"{title}: {keys}" : title });
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

    /// <summary>The log console (UI-09): the latest entries, to copy or to save with the rest of the diagnostics.</summary>
    private async Task ShowLogAsync()
    {
        var text = string.Join(Environment.NewLine, App.Log.Tail(500).Select(entry => entry.Format()));
        var box = new TextBox
        {
            Text = text,
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Cascadia Mono, Consolas"),
            FontSize = 12,
            Height = 420,
            MinWidth = 720,
        };
        ScrollViewer.SetHorizontalScrollBarVisibility(box, ScrollBarVisibility.Auto);
        ScrollViewer.SetVerticalScrollBarVisibility(box, ScrollBarVisibility.Auto);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(box, "LogText");
        var dialog = new ContentDialog
        {
            Title = "Log",
            Content = box,
            PrimaryButtonText = "Copy",
            SecondaryButtonText = "Save diagnostics\u2026",
            CloseButtonText = "Close",
            XamlRoot = Content.XamlRoot,
        };
        switch (await ShowDialogAsync(dialog))
        {
            case ContentDialogResult.Primary:
                var package = new DataPackage();
                package.SetText(text);
                Clipboard.SetContent(package);
                Say("The log is on the clipboard.");
                break;
            case ContentDialogResult.Secondary:
                await SaveDiagnosticsAsync();
                break;
        }
    }

    /// <summary>
    /// Saves the log, the settings and a description of this PC in one zip for a problem report
    /// (TOOL-10). Nothing is sent anywhere; the user decides what to do with the file.
    /// </summary>
    private async Task SaveDiagnosticsAsync()
    {
        var explanation = new TextBlock
        {
            Text = "This saves a ZIP on your computer; rexplayer never uploads it. The bundle includes the application log, your settings and this PC's codec/graphics details. Your Windows user-profile path is masked, but filenames, other local folders and addresses may still appear. Review it before sharing.",
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 480,
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(explanation, "DiagnosticsPrivacyNotice");
        if (!await Ask("Save private diagnostics locally?", explanation, "Choose ZIP location"))
        {
            return;
        }

        var picker = Prepared(new FileSavePicker { SuggestedStartLocation = PickerLocationId.Desktop, SuggestedFileName = DiagnosticsBundle.FileName(DateTimeOffset.Now) });
        picker.FileTypeChoices.Add("Zip archive", [".zip"]);
        if (await picker.PickSaveFileAsync() is not { } file)
        {
            return;
        }

        try
        {
            await Task.Run(() =>
            {
                using var output = File.Create(file.Path);
                App.Log.Flush();
                DiagnosticsBundle.Write(
                    output,
                    [("rexplayer.log", ReadShared(App.Log.FilePath)), ("settings.json", ReadShared(App.SettingsPath)), ("system.txt", DescribeSystem())],
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
            });
            Say("Diagnostics saved: " + file.Name);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Say("The diagnostics could not be saved: " + ex.Message);
        }
    }

    /// <summary>A file's text even while it is being written (the log is); null when there is none.</summary>
    private static string? ReadShared(string? path)
    {
        if (path is null || !File.Exists(path))
        {
            return null;
        }

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>The version, Windows, the decoders Windows offers and the graphics adapter, as text.</summary>
    private static string DescribeSystem()
    {
        var lines = new List<string> { $"rexplayer {Version}", $"Windows {Environment.OSVersion.Version}", $".NET {Environment.Version}", $"Processors: {Environment.ProcessorCount}" };
        foreach (var (codec, software, hardware) in MfDecoderFactory.Survey())
        {
            lines.Add($"{codec.DisplayName()}: software [{string.Join(", ", software)}], hardware [{string.Join(", ", hardware)}]");
        }

        if (OperatingSystem.IsWindowsVersionAtLeast(10))
        {
            try
            {
                using var renderer = Rex.Media.Interop.Graphics.D3D11VideoRenderer.Create(software: false);
                lines.Add($"Graphics: {renderer.AdapterName}{(renderer.IsSoftware ? " (software)" : "")}");
            }
            catch (InvalidOperationException ex)
            {
                lines.Add("Graphics: none (" + ex.Message + ")");
            }
        }

        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>An in-window dialog; true when the user chose <paramref name="primary"/>.</summary>
    private async Task<bool> Ask(string title, UIElement content, string? primary, string? close = null)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = content,
            CloseButtonText = close ?? (primary is null ? "Close" : "Cancel"),
            XamlRoot = Content.XamlRoot,
        };
        if (primary is not null)
        {
            dialog.PrimaryButtonText = primary;
            dialog.DefaultButton = ContentDialogButton.Primary;
        }

        return await ShowDialogAsync(dialog) == ContentDialogResult.Primary;
    }

    /// <summary>
    /// Shows a dialog, one at a time: WinUI allows only one, so one asked for while another is open
    /// (a script's command, say) is not shown. The window hides an open dialog before it closes.
    /// </summary>
    private async Task<ContentDialogResult> ShowDialogAsync(ContentDialog dialog)
    {
        if (_dialog is not null)
        {
            App.Log.Debug(LogSource, $"\"{dialog.Title}\" was not shown: \"{_dialog.Title}\" is open.");
            return ContentDialogResult.None;
        }

        _dialog = dialog;
        try
        {
            return await dialog.ShowAsync();
        }
        finally
        {
            _dialog = null;
        }
    }
}
