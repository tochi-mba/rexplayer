using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Rex.Media.AppCore;
using Rex.Media.AppCore.Commands;
using Rex.Media.AppCore.Player;
using Rex.Media.AppCore.Shell;
using Rex.Media.Audio;
using Rex.Media.Audio.Wasapi;
using Rex.Media.Codecs.MediaFoundation;
using Rex.Media.Engine;
using Rex.Media.Interop.Graphics;
using Rex.Media.IO;
using Rex.Media.Library;
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
    private readonly bool _evidenceOsd;
    private object? _osdNoticeItem;
    private readonly DispatcherQueueTimer _saveTimer;
    private readonly DispatcherQueueTimer _controlsTimer;
    private readonly DispatcherQueueTimer _pointerTimer;
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
    private readonly string? _startupCommand;
    private Task _presenterWork = Task.CompletedTask;
    private bool _updatingControls;
    private bool _showRemaining = true;
    private Microsoft.UI.Xaml.Controls.ContentDialog? _dialog;
    private bool _closed;
    private volatile bool _closing;

    // Every clock the window makes, so closing stops them all: a tick after Closed touches controls
    // that are going, and the exception ends the process (0xC000027B) instead of reaching App.
    private readonly List<DispatcherQueueTimer> _timers = [];
    private readonly WindowTeardown _teardown = new((part, ex) => App.Log.Error(LogSource, $"Closing {part} failed: {ex}"));

    // While the seek bar is dragged, the thumb follows the pointer, not the playing position.
    private bool _scrubbing;
    private long _lastScrubSeekAt;
    private long _accessibleSeekSecond = long.MinValue;
    private long _accessibleSeekDurationTicks = long.MinValue;
    private readonly System.Collections.ObjectModel.ObservableCollection<PlaylistRow> _playlistRows = [];
    private PlaylistItem? _queueLastFollowed;
    private bool _queuePaneWasVisible;

    /// <param name="files">What the launch was asked to open.</param>
    /// <param name="settings">The settings the launch has already read; read again when null.</param>
    /// <param name="startupCommand">A safe jump-list task to run after initial activation, or null.</param>
    public MainWindow(IReadOnlyList<string> files, PlayerSettings? settings = null, string? startupCommand = null)
    {
        App.Milestones.Reach("window");
        InitializeComponent();
        RexTheme.Follow(Root, RefreshThemedRows);
        App.Milestones.Reach("xaml");
        _settings = settings ?? SettingsStore.Load(SettingsPath);
        _startupCommand = startupCommand;

        // Read before this run writes its own: a marker here is the last run's, which did not close cleanly.
        _crashed = ResumeMarker.Read(ResumePath);
        _keymap = new Keymap(_settings.Shortcuts);
        Title = "rexplayer";
        SystemBackdrop = new Microsoft.UI.Xaml.Media.MicaBackdrop();
        ApplyTheme();
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "rexplayer.ico"));
        ExtendsContentIntoTitleBar = false;

        _player = new PlayerController(NewSession, OpenSource, OnWindowThread, _settings, store: App.Warmup?.TakeMemory() ?? OpenStore(), log: App.Log) { Milestones = App.Milestones };
        _player.SectionAnalyzer = (path, cancellation) => SectionAnalysis.Analyze(path,
            MediaRegistries.Decoders(new MfDecoderFactory()), cancellation);
        _player.SectionCache = new SectionAnalysisCache(App.Roots.SectionCache);
        _player.Changed += (_, _) => ShowState();
        _player.PositionChanged += (_, _) => ShowPosition();
        _player.PositionChanged += (_, _) => ShowLyricLine();
        _player.PositionChanged += (_, _) => RefreshLyricsCompanion();
        _player.Changed += (_, _) => RefreshLyricsCompanion();
        _player.Message += (_, text) => SayFromPlayer(text);
        _player.Playlist.Changed += (_, _) => ShowPlaylist();
        WireSubtitles();
        WireKineticCaptions();
        WireTranscription();
        WireVisualizer();
        WireStageLayout();
        WireLyricNavigation();
        WireLibrary();
        ApplyTooltips();
        ApplyGlobalShortcuts();
        WireZoom();
        Root.SizeChanged += (_, _) => LayOutMediaStage();
        Stage.SizeChanged += (_, _) => LayOutMediaStage();
        WireSubjectTools();
        WireSeekBar();
        WireTransport();
        // The WinUI Slider may mark pointer motion handled as it moves its thumb.
        // Listen to routed motion anyway so the real timeline hover time is
        // updated even when optional thumbnail decoding is switched off.
        SeekArea.AddHandler(UIElement.PointerMovedEvent,
            new Microsoft.UI.Xaml.Input.PointerEventHandler(OnSeekAreaPointerMoved),
            handledEventsToo: true);

        // UI evidence runs hold a genuine failure notice long enough for two
        // independently rendered screenshots. Normal users keep the brief OSD.
        _evidenceOsd = Environment.GetEnvironmentVariable("REXPLAYER_VISUAL_EVIDENCE") == "1";
        _osdTimer = Timer(OnScreenMessage.Shortest,
            () => OsdBox.Visibility = Visibility.Collapsed);
        _saveTimer = Timer(TimeSpan.FromMilliseconds(500), SaveSettings);
        _controlsTimer = Timer(TimeSpan.FromSeconds(_settings.ControlsHideSeconds), HideFullScreenControls);
        _pointerTimer = Timer(TimeSpan.FromSeconds(1.5), HideFullScreenPointer);
        _statsTimer = Timer(TimeSpan.FromSeconds(1), () => Forget(RefreshStatsAsync()));
        _statsTimer.IsRepeating = true;
        // Started and stopped by KeepClocks as the player's state asks; idle, neither ticks.
        _resumeTimer = Timer(WindowClocks.ResumeEvery, RememberWhereWeAre);
        _resumeTimer.IsRepeating = true;
        _sleepTimer = Timer(WindowClocks.SleepEvery, _player.SleepTick);
        _sleepTimer.IsRepeating = true;

        BuildMenus();
        Root.AddHandler(UIElement.PreviewKeyDownEvent, new Microsoft.UI.Xaml.Input.KeyEventHandler(OnPreviewKeyDown), handledEventsToo: true);
        Root.AddHandler(UIElement.PreviewKeyUpEvent, new Microsoft.UI.Xaml.Input.KeyEventHandler(OnPreviewKeyUp), handledEventsToo: true);
        Video.Loaded += (_, _) => AttachVideo();
        Video.SizeChanged += (_, _) => ResizeVideo();
        Video.CompositionScaleChanged += (_, _) => ResizeVideo();
        Closed += OnClosed;

        // WinUI can raise focus and container events while tearing down the visual tree.
        // Mark the window as closing at the first native notification, before those
        // callbacks can schedule fresh artwork or composition transitions.
        AppWindow.Closing += (_, _) =>
        {
            _closing = true;
            _dialog?.Hide();
        };

        RestorePlacement();

        // Never so small that the controls cannot be used, nor so large that a small video cannot be shown at half size.
        if (AppWindow.Presenter is OverlappedPresenter overlapped)
        {
            overlapped.PreferredMinimumWidth = 360;
            overlapped.PreferredMinimumHeight = 320;
        }

        PlaylistView.ItemsSource = _playlistRows;
        PlaylistView.ContainerContentChanging += OnPlaylistRowShown;
        WirePerformance();
        WireHiddenWindow();
        ApplySettingsToControls();
        ShowState();
        ShowPosition();
        ShowPlaylist();

        // Opened once the picture panel is ready, so the first item has somewhere to show its pictures.
        _startupFiles = files;
        WireLaunch();
    }

    public static string Version { get; } = typeof(MainWindow).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "0.0.0";

    /// <summary>
    /// The audio output: the Windows default device, or nothing audible when REXPLAYER_FAKE_AUDIO=1
    /// (the UI tests, which run where there may be no sound card).
    /// </summary>
    private IAudioSink NewAudioSink()
    {
        if (Environment.GetEnvironmentVariable("REXPLAYER_FAKE_AUDIO") == "1")
        {
            return new NullAudioSink();
        }

        // The sound follows Windows' default output; say so when it moves.
        var sink = new WasapiAudioSink();
        sink.Moved += () => OnWindowThread(() => Say("Sound moved to the new output device"));
        return sink;
    }

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
            Milestones = App.Milestones,
            GraphicsCardDecoding = App.GraphicsCardDecoding,
            VideoQueueCapacity = _budget.VideoQueueCapacity,
            AudioQueueCapacity = _budget.AudioQueueCapacity,
        },
        listener);

    /// <summary>Runs the window's repeating clocks only while they have work (<see cref="WindowClocks"/>).</summary>
    private void KeepClocks()
    {
        Run(_resumeTimer, WindowClocks.Resume(_player.Item is not null));
        Run(_sleepTimer, WindowClocks.Sleep(_player.SleepTimerSet));
    }

    /// <summary>Starts or stops <paramref name="timer"/>; one already running keeps its rhythm.</summary>
    private static void Run(DispatcherQueueTimer timer, bool wanted)
    {
        if (wanted && !timer.IsRunning)
        {
            timer.Start();
        }
        else if (!wanted && timer.IsRunning)
        {
            timer.Stop();
        }
    }

    private DispatcherQueueTimer Timer(TimeSpan interval, Action tick)
    {
        var timer = CreateTimer();
        timer.Interval = interval;
        timer.IsRepeating = false;
        OnTick(timer, tick);
        return timer;
    }

    /// <summary>What <paramref name="timer"/> does at each tick, guarded as every window callback is (<see cref="OnWindowCallback"/>).</summary>
    private void OnTick(DispatcherQueueTimer timer, Action tick) => timer.Tick += (_, _) => OnWindowCallback(tick);

    /// <summary>
    /// Runs work Windows calls back on the window's thread: a queued action or a clock's tick. An
    /// exception escaping such a callback ends the process (0xC000027B) without ever reaching App,
    /// so it is written down here with its stack and the window carries on, as App does for an
    /// event handler's. Nothing runs once the window is closing.
    /// </summary>
    private void OnWindowCallback(Action work)
    {
        if (_closing)
        {
            return;
        }

        try
        {
            work();
        }
        catch (Exception ex)
        {
            App.Log.Critical(LogSource, "Work on the window's thread failed; the window carries on: " + ex);
        }
    }

    /// <summary>
    /// Lets <paramref name="task"/> run on without being waited for. A failure is written down with
    /// its stack as it happens, instead of vanishing until the collector finds the task.
    /// </summary>
    internal static void Forget(Task task, [System.Runtime.CompilerServices.CallerArgumentExpression(nameof(task))] string what = "") =>
        task.ContinueWith(
            static (failed, state) => App.Log.Error(LogSource, $"{((string)state!).Split('\n', 2)[0].Trim()} failed: {failed.Exception!.InnerException}"),
            what,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

    /// <summary>A clock on the window's thread that closing stops.</summary>
    private DispatcherQueueTimer CreateTimer()
    {
        var timer = DispatcherQueue.CreateTimer();
        _timers.Add(timer);
        return timer;
    }

    /// <summary>Stops every clock and takes the window off the app-wide frame and window events.</summary>
    private void StopClocksAndFrames()
    {
        foreach (var timer in _timers)
        {
            timer.Stop();
        }

        _probe?.Dispose();
        _probe = null;
        Microsoft.UI.Xaml.Media.CompositionTarget.Rendering -= OnCaptionRendering;
        _captionsDrawing = false;
        AppWindow.Changed -= OnAppWindowChanged;
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
            _ready.TrySetResult();
            _startupFiles = null;
            Forget(GreetThenRecoverAsync(started.Count > 0));
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
            if (App.Warmup?.TakePictures() is { } made)
            {
                // Made while the window loaded; it only needs this panel's size.
                (_presenter, swapChain) = made;
                made.Presenter.Resize(width, height);
            }
            else
            {
                _presenter = Task.Run(() => D3D11Presenter.ForComposition(width, height, out swapChain, App.SoftwareGraphics)).GetAwaiter().GetResult();
            }

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
                ConfigureEffect(presenter, _settings.VideoEffect, _settings.VideoStyleValues, _settings.VideoEffectStrength);
                presenter.Redraw();
            });
            App.Milestones.Reach("presenter");
            Forget(CompleteFactsAsync());
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
        if (_startupCommand is { } command)
        {
            Run(command);
        }
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
        var picture = _player.Item?.Location is { } location && MediaFiles.IsPicture(location);
        if (!picture && PictureIntervalButton.Visibility == Visibility.Visible)
        {
            // Popup flyouts have their own focus scope. Dismissing the picture
            // control during a stream switch must also close its open choices
            // and move focus away from the removed menu/button.
            if (Content.XamlRoot is { } xamlRoot)
            {
                var focused = Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(xamlRoot);
                if (ReferenceEquals(focused, PictureIntervalButton)
                    || (focused is MenuFlyoutItemBase menuItem && PictureIntervalMenu.Items.Contains(menuItem)))
                {
                    PlayPauseButton.Focus(FocusState.Programmatic);
                }
            }

            PictureIntervalMenu.Hide();
        }

        PictureIntervalButton.Visibility = picture ? Visibility.Visible : Visibility.Collapsed;
        if (picture)
        {
            ShowPictureIntervalLabel();
        }
        NowPlaying.Text = title;

        var idle = _player.State is SessionState.Idle || (_player.Item is not null && !HasVideo);
        VideoBlankLayer.Visibility = HasVideo ? Visibility.Collapsed : Visibility.Visible;
        Idle.Visibility = idle ? Visibility.Visible : Visibility.Collapsed;
        IdleScroller.Visibility = Idle.Visibility;
        WelcomeActions.Visibility = _player.Item is null ? Visibility.Visible : Visibility.Collapsed;
        IdleTitle.Text = _player.Item is null ? "rexplayer" : title;
        IdleHint.Text = _player.Item is null ? "Drop media here, or press Ctrl+O to open a file."
            : _player.State == SessionState.Opening ? "Opening" + "\u2026"
            : _player.Failure is { } failure ? DamageNotices.Short(failure) : _player.Artist ?? "";
        ShowStageError();

        var waitingForDownload = _player.State == SessionState.Buffering;
        DownloadBufferOverlay.Visibility = waitingForDownload ? Visibility.Visible : Visibility.Collapsed;
        if (waitingForDownload)
        {
            var download = _player.Item?.Location is { } downloadLocation ? _library?.Entry(downloadLocation) : null;
            var fraction = download is { Downloading: true }
                ? Downloads.Fraction(download.Size, download.FinalSize, download.Allocated)
                : null;
            DownloadBufferProgress.IsIndeterminate = fraction is null;
            DownloadBufferProgress.Value = fraction.GetValueOrDefault() * 100;
            var progress = download is { Downloading: true }
                ? Downloads.Detail(download.Size, download.FinalSize, download.Allocated) + ". "
                : "";
            DownloadBufferOverlayText.Text = progress + (string.IsNullOrWhiteSpace(title)
                ? "Playback will retry automatically as more becomes available."
                : $"{title} will retry automatically as more becomes available.");
        }

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
        StartOverButton.IsEnabled = _player.CanSeek;
        ShowSpeed();
        _updatingControls = false;
        ShowTransportState();

        if (!ReferenceEquals(_player.Item, _shownItem))
        {
            ResetSeekPreview();
            // Only dismiss a notice from the previous item. The new item may already have
            // announced its title or state before this update reaches the window.
            if (ReferenceEquals(_osdNoticeItem, _shownItem))
            {
                _osdTimer.Stop();
                OsdBox.Visibility = Visibility.Collapsed;
                Osd.Text = "";
            }
            // Each item starts at its own shape; a crop chosen for one film rarely suits the next.
            _shownItem = _player.Item;
            if (_subjectOpen)
            {
                CloseSubjectTools();
            }

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

        RestoreUpdatedPictureView();
        _updatePlaying = playing;
        ShowUpdates();
        KeepAwake(playing && HasVideo, playing);
        KeepClocks();
        LayOutSubtitles();
        ShowPresentation();
        ApplyVisualizer();
        ApplyKineticCaptions();
        ShowMemory();
        ShowEpisodeSkip();
        ShowNamedPlaylists();
        // Queue mutations and decoder-state changes arrive separately. Keep Now/Next and the
        // actual duration true as a stream opens, pauses, ends or faults.
        if (_player.Playlist.CurrentIndex is >= 0 and var queueIndex && queueIndex < _playlistRows.Count
            && _player.Duration > TimeSpan.Zero
            && _playlistRows[queueIndex].DurationText != TimeText.Format(_player.Duration, _player.Duration))
        {
            ShowPlaylist();
        }
        else
        {
            ShowQueuePlaybackState();
        }

        ShowMenuChecks();
        ShowTaskbarState();
        ShowCurrentLibraryItem();
        ShowMovieVersions();
        RememberLater();
    }

    private void ShowPosition()
    {
        var duration = _player.Duration;
        var timeline = TimeText.Timeline(_player.Position, duration, _player.State == SessionState.Ended);
        (Elapsed.Text, Remaining.Text) = TimeText.Clocks(_player.Position, duration, _player.Item is null ? null : _player.State, _showRemaining);
        _updatingControls = true;
        SeekBar.Maximum = timeline.SliderMaximum;
        if (!_scrubbing)
        {
            SeekBar.Value = timeline.SliderValue;
        }

        _updatingControls = false;
        if (_player.Playlist.CurrentIndex is >= 0 and var currentIndex && currentIndex < _playlistRows.Count)
        {
            _playlistRows[currentIndex].SetProgress(_player.Position, duration);
        }

        var wholeSecond = (long)Math.Floor(timeline.Elapsed.TotalSeconds);
        if (wholeSecond != _accessibleSeekSecond || duration.Ticks != _accessibleSeekDurationTicks)
        {
            _accessibleSeekSecond = wholeSecond;
            _accessibleSeekDurationTicks = duration.Ticks;
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetHelpText(SeekBar,
                timeline.Remaining is { } remaining
                    ? $"Position {TimeText.Format(timeline.Elapsed, duration)} of {TimeText.Format(duration)}; {TimeText.Format(remaining, duration)} remaining. Use arrow keys or drag to seek."
                    : "The duration is not known; seeking is unavailable.");
        }

        ShowEpisodeSkip();
        ShowTaskbarPosition();
    }

    /// <summary>Brings the playlist view in line, row by row, keeping its selection and scroll and the playing item in sight.</summary>
    private void ShowPlaylist()
    {
        var current = _player.Playlist.CurrentIndex;
        var repeated = _player.Playlist.Items
            .GroupBy(item => item.Title, StringComparer.CurrentCultureIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet(StringComparer.CurrentCultureIgnoreCase);
        var rows = _player.Playlist.Items.Select((item, i) =>
        {
            var source = repeated.Contains(item.Title) ? PlaylistItem.SourceOf(item.Location) : null;
            var secondary = string.Join(" · ", new[] { item.Artist, source }.Where(text => !string.IsNullOrWhiteSpace(text)).Distinct(StringComparer.CurrentCultureIgnoreCase));
            var whole = _library?.Entry(item.Location)?.Duration;
            TimeSpan? length = ReferenceEquals(_player.Playlist.Current, item) && _player.Duration > TimeSpan.Zero
                ? _player.Duration : item.End is { } end && whole is { } full && full < end
                    ? full - item.Start : item.End is { } cueEnd ? cueEnd - item.Start
                    : whole is { } known ? known - item.Start : null;
            if (length <= TimeSpan.Zero)
            {
                length = null;
            }

            return new PlaylistRow(item.Title, secondary, i == current, _player.IsMissing(item), item.Location, length);
        }).ToList();
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

        if (current >= 0 && current < _playlistRows.Count)
        {
            _playlistRows[current].SetProgress(_player.Position, _player.Duration);
        }

        ShowQueuePlaybackState();

        var paneVisible = PlaylistPane.Visibility == Visibility.Visible;
        PlaylistUndoReorder.IsEnabled = _queueUndoOrder is { } previous && _player.Playlist.CanRestoreOrder(previous);

        if (QueueScrollPolicy.ShouldFollow(paneVisible, !_queuePaneWasVisible, _queueLastFollowed, _player.Playlist.Current))
        {
            PlaylistView.ScrollIntoView(_playlistRows[current]);
        }

        _queueLastFollowed = _player.Playlist.Current;
        _queuePaneWasVisible = paneVisible;
        ShowTaskbarState();
    }

    /// <summary>Titles and reversible actions follow engine state even when the queue itself did not change.</summary>
    private void ShowQueuePlaybackState()
    {
        ShowQueueHeader(QueuePresentation.Of(_player.Playlist.Items.Count, _player.Playlist.Current,
            _player.Playlist.PeekNext(automatic: true), _player.Item, _player.State));
        PlaylistUndoRemoval.IsEnabled = _undoQueueRemoval is { } removed
            && ReferenceEquals(_undoQueuePlayerItem, _player.Item)
            && _player.Playlist.CanUndoRemoval(removed);
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
        KeepPlace();
        _settings = _settings with
        {
            Volume = _player.Volume,
            Muted = _player.Muted,
            Repeat = _player.Playlist.Repeat,
            Shuffle = _player.Playlist.Shuffle,
            PlaylistVisible = PlaylistPane.Visibility == Visibility.Visible,
            Window = placement,
            LibraryRoute = _libraryRouteApplied ? CurrentLibraryRoute() : _settings.LibraryRoute,
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
        // The screen it was left on may be gone (unplugged, or a remote session at another size).
        if (Rex.Media.AppCore.Shell.WindowPlacementFit.Fit(_settings.Window, Screens()) is { } window)
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

    /// <summary>Every screen's work area, the main screen first.</summary>
    private static List<Rex.Media.AppCore.Shell.ScreenArea> Screens()
    {
        var screens = new List<Rex.Media.AppCore.Shell.ScreenArea>();
        var all = DisplayArea.FindAll();
        // Read by index: enumerating this list is not supported by its projection.
        for (var i = 0; i < all.Count; i++)
        {
            var area = all[i].WorkArea;
            var screen = new Rex.Media.AppCore.Shell.ScreenArea(area.X, area.Y, area.Width, area.Height);
            if (all[i].IsPrimary)
            {
                screens.Insert(0, screen);
            }
            else
            {
                screens.Add(screen);
            }
        }

        return screens;
    }

    /// <summary>
    /// Runs <paramref name="action"/> on the window's thread, unless the window is closing by then:
    /// work queued from another thread as it closes must not wake timers or touch controls that are going.
    /// </summary>
    private void OnWindowThread(Action action) => OnWindowThread(DispatcherQueuePriority.Normal, action);

    /// <inheritdoc cref="OnWindowThread(Action)"/>
    private void OnWindowThread(DispatcherQueuePriority priority, Action action)
    {
        // Engine events may arrive after Closed, when WinUI has detached this window's
        // dispatcher. Never access that dispatcher after the window starts closing.
        if (_closing)
        {
            return;
        }

        var dispatcher = DispatcherQueue;
        if (dispatcher is null)
        {
            return;
        }

        dispatcher.TryEnqueue(priority, () => OnWindowCallback(action));
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        _onlineLyricsCancellation?.Cancel();
        // Ordered: first nothing more can be queued or tick, then the parts that draw stop, then
        // the settings are kept, then the workers and the player go, and the graphics device last.
        // Each part closes on its own, so one failure is logged and the rest still close.
        _closing = true;
        AppDomain.CurrentDomain.FirstChanceException += LogLateException;
        var failures = _teardown.Run(
        [
            ("the clocks", StopClocksAndFrames),
            ("the subtitle contrast observer", UnwireSubtitleContrast),
            ("the performance watch", UnwirePerformance),
            // The seek area can still see the pointer leave while the window goes, so its work is
            // cancelled, not disposed; and the visualisation stops drawing (and the camera stops) first.
            ("the seek preview", ResetSeekPreview),
            ("the detached lyrics window", CloseLyricsCompanion),
            ("the subject models", () => _ = CloseSubjectModels()),
            ("the visualisation", StopVisualizing),
            ("the update", InstallAsWeClose),
            ("the settings", SaveSettings),
            ("the queue", _player.SaveQueue),
            ("transcription", CloseTranscription),
            ("the library", CloseLibrary),
            ("the shell", DisposeShell),
            ("the global shortcuts", () => _globalHotkeys?.Dispose()),
            ("the resume marker", TryClearResumeMarker),
            ("keeping awake", () => KeepAwake(false, false)),
            ("the player", _player.Dispose),
            // Presenter work is queued on a background thread. Finish its final disposal before
            // WinUI tears down the window, or a late native callback may crash the process.
            ("the presenter", ClosePresenter),
            ("the metadata", StopMetadata),
        ]);
        if (failures < 0)
        {
            return;
        }

        _closed = true;
        App.Log.Info(LogSource, failures == 0 ? "rexplayer closed." : $"rexplayer closed; {failures} parts failed to close.");
    }

    private void ClosePresenter()
    {
        OnPresenterThread(presenter => presenter.Dispose());
        _presenter = null;
        _presenterWork.GetAwaiter().GetResult();
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
            // Update the visible preview immediately, but bound expensive
            // decoder seeks while the pointer generates many ValueChanged events.
            // EndScrub always seeks to the exact final destination.
            var now = Environment.TickCount64;
            if (now - _lastScrubSeekAt < 80)
            {
                return;
            }

            _lastScrubSeekAt = now;
        }

        _player.Seek(TimeSpan.FromSeconds(e.NewValue));
    }

    private void WireSeekBar()
    {
        // The Slider handles pointer movement while dragging or hovering its
        // thumb. Observe handled events at the containing timeline too, or
        // the accessible time preview never appears over the actual slider.
        SeekArea.AddHandler(UIElement.PointerMovedEvent,
            new Microsoft.UI.Xaml.Input.PointerEventHandler(OnSeekAreaPointerMoved), handledEventsToo: true);
        // A mouse entering the seek slider without moving it further must
        // still show an accessible time preview. The Slider swallows some
        // bubbled motion events, so observe both its own and parent route.
        SeekBar.AddHandler(UIElement.PointerMovedEvent,
            new Microsoft.UI.Xaml.Input.PointerEventHandler(OnSeekAreaPointerMoved), handledEventsToo: true);
        SeekBar.AddHandler(UIElement.PointerEnteredEvent,
            new Microsoft.UI.Xaml.Input.PointerEventHandler(OnSeekAreaPointerMoved), handledEventsToo: true);
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
        _lastScrubSeekAt = 0;
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
