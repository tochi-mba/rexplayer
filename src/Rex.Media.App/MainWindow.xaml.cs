using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls.Primitives;
using Rex.Media.AppCore;
using Rex.Media.AppCore.Commands;
using Rex.Media.AppCore.Player;
using Rex.Media.Audio;
using Rex.Media.Audio.Wasapi;
using Rex.Media.Codecs.MediaFoundation;
using Rex.Media.Engine;
using Rex.Media.Interop.Graphics;
using Rex.Media.IO;
using Rex.Media.Primitives;
using Rex.Media.Settings;
using Rex.Media.Video;
using Rex.Media.Video.D3D11;

namespace Rex.Media.App;

/// <summary>
/// The player window. It shows what the <see cref="PlayerController"/> is doing and turns clicks,
/// keys and drops into its commands; the picture is drawn by the engine straight into the swap-chain
/// panel. Decisions live in Rex.Media.AppCore; this class only wires them to WinUI.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001", Justification = "A window's life ends at Closed, where the player and the presenter are disposed.")]
public sealed partial class MainWindow : Window
{
    private const string LogSource = "window";

    private static readonly string SettingsPath = App.SettingsPath;

    private readonly PlayerController _player;
    private readonly DispatcherQueueTimer _osdTimer;
    private readonly DispatcherQueueTimer _saveTimer;
    private readonly DispatcherQueueTimer _controlsTimer;
    private readonly DispatcherQueueTimer _statsTimer;
    private readonly DispatcherQueueTimer _resumeTimer;
    private readonly DispatcherQueueTimer _sleepTimer;
    private readonly ResumePoint? _crashed;
    private SessionStats? _lastStats;
    private ShapePreset _aspect = VideoGeometry.AspectRatios[0];
    private ShapePreset _crop = VideoGeometry.Crops[0];
    private PlaylistItem? _shownItem;
    private DateTime _lastStatsAt;
    private PlayerSettings _settings;
    private Keymap _keymap;
    private D3D11Presenter? _presenter;
    private IReadOnlyList<string>? _startupFiles;
    private Task _presenterWork = Task.CompletedTask;
    private bool _updatingControls;
    private bool _showRemaining = true;
    private Microsoft.UI.Xaml.Controls.ContentDialog? _dialog;
    private bool _closed;
    private bool _closing;

    // While the seek bar is dragged, the thumb follows the pointer, not the playing position.
    private bool _scrubbing;
    private readonly System.Collections.ObjectModel.ObservableCollection<PlaylistRow> _playlistRows = [];

    public MainWindow(IReadOnlyList<string> files)
    {
        InitializeComponent();
        _settings = SettingsStore.Load(SettingsPath);

        // Read before this run writes its own: a marker here is the last run's, which did not close cleanly.
        _crashed = ResumeMarker.Read(ResumePath);
        _keymap = new Keymap(_settings.Shortcuts);
        Title = "rexplayer";
        SystemBackdrop = new Microsoft.UI.Xaml.Media.MicaBackdrop();
        ApplyTheme();
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "rexplayer.ico"));
        ExtendsContentIntoTitleBar = false;

        _player = new PlayerController(NewSession, OpenSource, OnWindowThread, _settings, store: OpenStore(), log: App.Log);
        _player.Changed += (_, _) => ShowState();
        _player.PositionChanged += (_, _) => ShowPosition();
        _player.PositionChanged += (_, _) => ShowLyricLine();
        _player.Message += (_, text) => Say(text);
        _player.Playlist.Changed += (_, _) => ShowPlaylist();
        WireSubtitles();
        WireVisualizer();
        WireMemory();
        WireLibrary();
        ApplyTooltips();
        ApplyGlobalShortcuts();
        WireZoom();
        WireSeekBar();

        _osdTimer = Timer(TimeSpan.FromSeconds(1.5), () => OsdBox.Visibility = Visibility.Collapsed);
        _saveTimer = Timer(TimeSpan.FromMilliseconds(500), SaveSettings);
        _controlsTimer = Timer(TimeSpan.FromSeconds(_settings.ControlsHideSeconds), HideFullScreenControls);
        _statsTimer = Timer(TimeSpan.FromSeconds(1), () => _ = RefreshStatsAsync());
        _statsTimer.IsRepeating = true;
        _resumeTimer = Timer(TimeSpan.FromSeconds(5), RememberWhereWeAre);
        _resumeTimer.IsRepeating = true;
        _resumeTimer.Start();
        _sleepTimer = Timer(TimeSpan.FromSeconds(1), _player.SleepTick);
        _sleepTimer.IsRepeating = true;
        _sleepTimer.Start();

        BuildMenus();
        Root.AddHandler(UIElement.PreviewKeyDownEvent, new Microsoft.UI.Xaml.Input.KeyEventHandler(OnPreviewKeyDown), handledEventsToo: true);
        Root.AddHandler(UIElement.PreviewKeyUpEvent, new Microsoft.UI.Xaml.Input.KeyEventHandler(OnPreviewKeyUp), handledEventsToo: true);
        Video.Loaded += (_, _) => AttachVideo();
        Video.SizeChanged += (_, _) => ResizeVideo();
        Video.CompositionScaleChanged += (_, _) => ResizeVideo();
        Closed += OnClosed;

        // A dialog still open when the window goes would be torn down with it, which WinUI does not survive.
        AppWindow.Closing += (_, _) => _dialog?.Hide();

        RestorePlacement();

        // Never so small that the controls cannot be used, nor so large that a small video cannot be shown at half size.
        if (AppWindow.Presenter is OverlappedPresenter overlapped)
        {
            overlapped.PreferredMinimumWidth = 360;
            overlapped.PreferredMinimumHeight = 150;
        }

        PlaylistView.ItemsSource = _playlistRows;
        PlaylistView.ContainerContentChanging += OnPlaylistRowShown;
        ApplySettingsToControls();
        ShowState();
        ShowPosition();
        ShowPlaylist();

        // Opened once the picture panel is ready, so the first item has somewhere to show its pictures.
        _startupFiles = files;
    }

    public static string Version { get; } = typeof(MainWindow).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "0.0.0";

    /// <summary>
    /// The audio output: the Windows default device, or nothing audible when REXPLAYER_FAKE_AUDIO=1
    /// (the UI tests, which run where there may be no sound card).
    /// </summary>
    private static IAudioSink NewAudioSink() =>
        Environment.GetEnvironmentVariable("REXPLAYER_FAKE_AUDIO") == "1" ? new NullAudioSink() : new WasapiAudioSink();

    private static IByteSource OpenSource(string location)
    {
        if (Uri.TryCreate(location, UriKind.Absolute, out var uri) && !uri.IsFile)
        {
            throw new NotSupportedException("Playing from the network arrives in a later version.");
        }

        return new FileByteSource(location);
    }

    private MediaSession NewSession(Action<SessionEvent> listener) => new(
        new EngineOptions
        {
            Demuxers = MediaRegistries.Demuxers(TimeSpan.FromSeconds(_settings.PictureSeconds)),
            Decoders = MediaRegistries.Decoders(new MfDecoderFactory(), new WicDecoderFactory()),
            AudioSinkFactory = NewAudioSink,
            VideoPresenterFactory = () => _presenter is { } presenter ? new BorrowedPresenter(presenter) : null!,
            Log = App.Log,
        },
        listener);

    private DispatcherQueueTimer Timer(TimeSpan interval, Action tick)
    {
        var timer = DispatcherQueue.CreateTimer();
        timer.Interval = interval;
        timer.IsRepeating = false;
        timer.Tick += (_, _) => tick();
        return timer;
    }

    /// <summary>Creates the presenter for the panel and hands its swap chain over, once the panel has a size.</summary>
    private void AttachVideo()
    {
        if (_presenter is null && OperatingSystem.IsWindowsVersionAtLeast(10))
        {
            CreatePresenter();
        }

        if (_startupFiles is { Count: > 0 } files)
        {
            // Files handed over by other launches just before this one is ready join these.
            _player.OpenHandedOver(files, enqueue: false, App.LaunchedAt);
        }
        else if (_startupFiles is not null)
        {
            _player.RestoreQueue();
        }

        if (_startupFiles is { } started)
        {
            _startupFiles = null;
            _ = GreetThenRecoverAsync(started.Count > 0);
        }
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows10.0")]
    private void CreatePresenter()
    {
        var (width, height) = VideoPixels();
        try
        {
            // Made on a thread-pool thread: the window's thread is a single-threaded COM apartment,
            // and Direct3D objects made there cannot be used by the engine's threads.
            nint swapChain = 0;
            _presenter = Task.Run(() => D3D11Presenter.ForComposition(width, height, out swapChain)).GetAwaiter().GetResult();
            var panel = WinRT.MarshalInspectable<object>.FromManaged(Video);
            try
            {
                SwapChainPanelNative.SetSwapChain(panel, swapChain);
            }
            finally
            {
                Marshal.Release(panel);
                Marshal.Release(swapChain);
            }

            var (scaleX, scaleY) = (Video.CompositionScaleX, Video.CompositionScaleY);
            OnPresenterThread(presenter =>
            {
                presenter.SetScale(scaleX, scaleY);
                var lookOptions = VideoStyleOptions.ForLook(_settings.VideoLook);
                presenter.SetLook(_settings.VideoLook,
                    VideoStyleOptions.Read(_settings.VideoStyleValues, lookOptions[0]),
                    VideoStyleOptions.Read(_settings.VideoStyleValues, lookOptions[1]));
                var effectOptions = VideoStyleOptions.ForEffect(_settings.VideoEffect);
                var power = _settings.VideoStyleValues.ContainsKey(effectOptions[0].Key)
                    ? VideoStyleOptions.Read(_settings.VideoStyleValues, effectOptions[0])
                    : _settings.VideoEffectStrength;
                presenter.SetEffect(_settings.VideoEffect, power,
                    VideoStyleOptions.Read(_settings.VideoStyleValues, effectOptions[1]));
                presenter.Redraw();
            });
            App.Log.Info(LogSource, $"Pictures are drawn by {_presenter.Name}.");
        }
        catch (Exception ex) when (ex is InvalidOperationException or COMException)
        {
            // Sound still plays; the window says why there are no pictures.
            _presenter = null;
            App.Log.Error(LogSource, "No pictures can be shown: " + ex.Message);
            Say("Pictures cannot be shown on this PC: " + ex.Message);
        }
    }

    private async Task GreetThenRecoverAsync(bool openedSomething)
    {
        await GreetAsync();
        AfterGreeting(_crashed, openedSomething);
    }

    private (int Width, int Height) VideoPixels() =>
        ((int)Math.Max(1, Math.Round(Video.ActualWidth * Video.CompositionScaleX)), (int)Math.Max(1, Math.Round(Video.ActualHeight * Video.CompositionScaleY)));

    private void ResizeVideo()
    {
        if (_presenter is null)
        {
            AttachVideo();
            return;
        }

        var (width, height) = VideoPixels();
        var (scaleX, scaleY) = (Video.CompositionScaleX, Video.CompositionScaleY);
        // With no pictures on their way the surface would keep its old size until the next picture,
        // leaving the window's background showing round it. Audio must forget the previous picture;
        // a paused picture or video keeps and redraws it.
        var clear = _player.Info is not null && !HasVideo;
        var redraw = !_player.IsPlaying;
        OnPresenterThread(presenter =>
        {
            presenter.Resize(width, height);
            presenter.SetScale(scaleX, scaleY);
            if (clear)
            {
                presenter.Clear();
            }
            else if (redraw)
            {
                presenter.Redraw();
            }
        });
    }

    /// <summary>
    /// Uses the presenter off the window's thread (see <see cref="CreatePresenter"/>), one call at a
    /// time in the order asked; a failure is logged rather than lost.
    /// </summary>
    private void OnPresenterThread(Action<D3D11Presenter> use)
    {
        if (_presenter is not { } presenter)
        {
            return;
        }

        _presenterWork = _presenterWork.ContinueWith(
            _ =>
            {
                try
                {
                    use(presenter);
                }
                catch (Exception ex) when (ex is InvalidOperationException or COMException or ObjectDisposedException)
                {
                    App.Log.Warning(LogSource, "Drawing failed: " + ex.Message);
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.None,
            TaskScheduler.Default);
    }

    private bool HasVideo => _player.Info?.FirstTrack(MediaKind.Video) is not null;

    /// <summary>Brings every control in line with the player's state.</summary>
    private void ShowState()
    {
        var playing = _player.IsPlaying;
        PlayPauseIcon.Glyph = playing ? "\uE769" : "\uE768";
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(PlayPauseButton, playing ? "Pause" : "Play");

        var title = _player.Title;
        Title = title.Length > 0 ? $"{title} \u2014 rexplayer" : "rexplayer";
        NowPlaying.Text = title;

        var idle = _player.State is SessionState.Idle || (_player.Item is not null && !HasVideo);
        VideoBlankLayer.Visibility = HasVideo ? Visibility.Collapsed : Visibility.Visible;
        Idle.Visibility = idle ? Visibility.Visible : Visibility.Collapsed;
        IdleTitle.Text = _player.Item is null ? "rexplayer" : title;
        IdleHint.Text = _player.Item is null ? "Drop media here, or press Ctrl+O to open a file."
            : _player.State == SessionState.Opening ? "Opening" + "\u2026"
            : _player.Failure ?? _player.Artist ?? "";

        _updatingControls = true;
        ShuffleButton.IsChecked = _player.Playlist.Shuffle;
        RepeatButton.IsChecked = _player.Playlist.Repeat != RepeatMode.Off;
        RepeatIcon.Glyph = _player.Playlist.Repeat == RepeatMode.One ? "\uE8ED" : "\uE8EE";
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(RepeatButton, _player.Playlist.Repeat switch
        {
            RepeatMode.All => "Repeat all",
            RepeatMode.One => "Repeat one",
            _ => "Repeat off",
        });
        VolumeBar.Maximum = _settings.MaxVolumePercent;
        VolumeBar.Value = Math.Round(_player.Volume * 100);
        MuteIcon.Glyph = _player.Muted ? "\uE74F" : "\uE767";
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(MuteButton, _player.Muted ? "Unmute" : "Mute");
        SeekBar.IsEnabled = _player.CanSeek;
        ShowSpeed();
        _updatingControls = false;

        if (!ReferenceEquals(_player.Item, _shownItem))
        {
            // Each item starts at its own shape; a crop chosen for one film rarely suits the next.
            _shownItem = _player.Item;
            if (_view.IsZoomed)
            {
                SetView(PictureView.Whole);
            }

            if (_aspect.Ratio is not null || _crop.Ratio is not null)
            {
                (_aspect, _crop) = (VideoGeometry.AspectRatios[0], VideoGeometry.Crops[0]);
                ApplyShape();
            }
        }

        // Sound without pictures leaves the picture black; until the item's details are known the last picture stays, so videos follow one another without a black flash.
        if (_player.Info is not null && !HasVideo)
        {
            OnPresenterThread(presenter => presenter.Clear());
        }

        KeepAwake(playing && HasVideo, playing);
        LayOutSubtitles();
        ShowPresentation();
        ApplyVisualizer();
        ShowMemory();
        ShowEpisodeSkip();
        ShowNamedPlaylists();
        ShowMenuChecks();
        RememberLater();
    }

    private void ShowPosition()
    {
        var duration = _player.Duration;
        Elapsed.Text = TimeText.Format(_player.Position, duration);
        Remaining.Text = duration <= TimeSpan.Zero ? "--:--"
            : _showRemaining ? "-" + TimeText.Format(duration - _player.Position, duration)
            : TimeText.Format(duration);
        _updatingControls = true;
        SeekBar.Maximum = Math.Max(1, duration.TotalSeconds);
        if (!_scrubbing)
        {
            SeekBar.Value = Math.Min(SeekBar.Maximum, _player.Position.TotalSeconds);
        }

        _updatingControls = false;
        ShowEpisodeSkip();
    }

    /// <summary>Brings the playlist view in line, row by row, keeping its selection and scroll and the playing item in sight.</summary>
    private void ShowPlaylist()
    {
        var current = _player.Playlist.CurrentIndex;
        var rows = _player.Playlist.Items.Select((item, i) => new PlaylistRow((i == current ? "\u25B6 " : "") + item.Title, _player.IsMissing(item), item.Location)).ToList();
        for (var i = 0; i < rows.Count; i++)
        {
            if (i >= _playlistRows.Count)
            {
                _playlistRows.Add(rows[i]);
            }
            else if (!_playlistRows[i].Equals(rows[i]))
            {
                _playlistRows[i] = rows[i];
            }
        }

        while (_playlistRows.Count > rows.Count)
        {
            _playlistRows.RemoveAt(_playlistRows.Count - 1);
        }

        if (current >= 0 && PlaylistPane.Visibility == Visibility.Visible)
        {
            PlaylistView.ScrollIntoView(_playlistRows[current]);
        }
    }

    /// <summary>A short message over the picture, which fades after a moment.</summary>
    private void Say(string text)
    {
        App.Log.Info(LogSource, text);
        if (!_settings.OnScreenMessages)
        {
            return;
        }

        Osd.Text = text;
        OsdBox.Visibility = Visibility.Visible;
        _osdTimer.Stop();
        _osdTimer.Start();
    }

    /// <summary>Light, dark or Windows' choice; the title bar follows the content.</summary>
    private void ApplyTheme()
    {
        Root.RequestedTheme = _settings.Theme switch
        {
            ThemeChoice.Light => ElementTheme.Light,
            ThemeChoice.Dark => ElementTheme.Dark,
            _ => ElementTheme.Default,
        };
        AppWindow.TitleBar.PreferredTheme = _settings.Theme switch
        {
            ThemeChoice.Light => TitleBarTheme.Light,
            ThemeChoice.Dark => TitleBarTheme.Dark,
            _ => TitleBarTheme.UseDefaultAppMode,
        };
    }

    private void ApplySettingsToControls()
    {
        PlaylistPane.Visibility = _settings.PlaylistVisible ? Visibility.Visible : Visibility.Collapsed;
        PlaylistButton.IsChecked = _settings.PlaylistVisible;
        ApplyAlwaysOnTop();
        ApplyMinimalInterface();
        ApplyStatsOverlay();
    }

    /// <summary>Saves the settings shortly after the last change, so a crash loses at most that moment.</summary>
    private void RememberLater()
    {
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private void SaveSettings()
    {
        // A dialog dismissed as the window closed may finish after it: the window saved everything as it closed.
        if (_closed)
        {
            return;
        }

        var placement = AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Maximized }
            ? _settings.Window is { } last ? last with { Maximized = true } : null
            : AppWindow.Presenter.Kind == AppWindowPresenterKind.Overlapped && AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Restored }
                ? new WindowPlacement(AppWindow.Position.X, AppWindow.Position.Y, AppWindow.Size.Width, AppWindow.Size.Height, false)
                : _settings.Window;
        _settings = _settings with
        {
            Volume = _player.Volume,
            Muted = _player.Muted,
            Repeat = _player.Playlist.Repeat,
            Shuffle = _player.Playlist.Shuffle,
            PlaylistVisible = PlaylistPane.Visibility == Visibility.Visible,
            Window = placement,
        };
        try
        {
            SettingsStore.Save(SettingsPath, _settings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            App.Log.Warning(LogSource, "The settings could not be saved: " + ex.Message);
        }
    }

    private void RestorePlacement()
    {
        if (_settings.Window is { } window)
        {
            AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(window.X, window.Y, window.Width, window.Height));
            if (window.Maximized && AppWindow.Presenter is OverlappedPresenter overlapped)
            {
                overlapped.Maximize();
            }
        }
        else
        {
            AppWindow.Resize(new Windows.Graphics.SizeInt32(1280, 800));
        }
    }

    /// <summary>
    /// Runs <paramref name="action"/> on the window's thread, unless the window is closing by then:
    /// work queued from another thread as it closes must not wake timers or touch controls that are going.
    /// </summary>
    private void OnWindowThread(Action action) => DispatcherQueue.TryEnqueue(() =>
    {
        if (!_closing)
        {
            action();
        }
    });

    private void OnClosed(object sender, WindowEventArgs args)
    {
        _closing = true;
        _saveTimer.Stop();
        _resumeTimer.Stop();
        _sleepTimer.Stop();
        // The seek area can still see the pointer leave while the window goes, so its work is
        // cancelled, not disposed; and the visualisation stops drawing (and the camera stops) first.
        HideSeekPreview();
        StopVisualizing();
        SaveSettings();
        _player.SaveQueue();
        CloseLibrary();
        _globalHotkeys?.Dispose();
        TryClearResumeMarker();
        KeepAwake(false, false);
        _player.Dispose();
        OnPresenterThread(presenter => presenter.Dispose());
        _closed = true;
        App.Log.Info(LogSource, "rexplayer closed.");
    }

    private void OnSeekBarChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_updatingControls)
        {
            return;
        }

        // Dragged: the time shows where the thumb is, and the picture follows; a click or a key seeks at once.
        if (_scrubbing)
        {
            Elapsed.Text = TimeText.Format(TimeSpan.FromSeconds(e.NewValue), _player.Duration);
        }

        _player.Seek(TimeSpan.FromSeconds(e.NewValue));
    }

    private void WireSeekBar()
    {
        SeekBar.AddHandler(UIElement.PointerPressedEvent, new Microsoft.UI.Xaml.Input.PointerEventHandler((_, _) => _scrubbing = true), handledEventsToo: true);
        SeekBar.AddHandler(UIElement.PointerReleasedEvent, new Microsoft.UI.Xaml.Input.PointerEventHandler((_, _) => EndScrub()), handledEventsToo: true);
        SeekBar.AddHandler(UIElement.PointerCaptureLostEvent, new Microsoft.UI.Xaml.Input.PointerEventHandler((_, _) => EndScrub()), handledEventsToo: true);
    }

    /// <summary>The drag is over: the last place it reached is where playing carries on.</summary>
    private void EndScrub()
    {
        if (!_scrubbing)
        {
            return;
        }

        _scrubbing = false;
        _player.Seek(TimeSpan.FromSeconds(SeekBar.Value));
        ShowPosition();
    }

    private void OnVolumeChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (!_updatingControls)
        {
            _player.SetVolume(e.NewValue / 100);
        }
    }

    private void OnRemainingTapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
    {
        _showRemaining = !_showRemaining;
        ShowPosition();
    }
}
