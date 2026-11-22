using System.Globalization;
using Rex.Media.AppCore;
using Rex.Media.AppCore.Automation;
using Rex.Media.AppCore.Commands;
using Rex.Media.AppCore.Design;
using Rex.Media.AppCore.Platform;
using Rex.Media.AppCore.Player;
using Rex.Media.AppCore.Shell;
using Rex.Media.Audio;
using Rex.Media.Audio.CoreAudio;
using Rex.Media.Codecs.Apple;
using Rex.Media.Diagnostics;
using Rex.Media.Engine;
using Rex.Media.IO;
using Rex.Media.Library;
using Rex.Media.Settings;
using Rex.Media.Video;

namespace Rex.Media.App.Mac;

/// <summary>
/// The player window on a Mac. It shows what the <see cref="PlayerController"/> is doing and turns
/// clicks, keys and menus into its commands, exactly as the Windows window does: the menus are
/// <see cref="MenuBarLayout"/>, the shortcuts the shared <see cref="Keymap"/>, the decisions
/// AppCore's. This class only wires them to AppKit. Pictures are drawn by the Metal presenter, with
/// the same colours and placement as on Windows.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000", Justification = "AppKit owns the views once they are in the window's tree.")]
public sealed partial class MainWindow : IDisposable
{
    private const string LogSource = "window";
    private const string HelpUrl = "https://tochi-mba.github.io/rexplayer/";

    private readonly DataRoots _roots;
    private readonly RexLog _log;
    private readonly NSWindow _window;
    private readonly PlayerController _player;
    private Keymap _keymap;
    private readonly RexLabel _title = MacTheme.Label("", 2, DesignRole.TextPrimary, semibold: true);
    private readonly RexLabel _artist = MacTheme.Label("", 1, DesignRole.TextSecondary);
    private readonly RexLabel _elapsed = MacTheme.Label("--:--");
    private readonly ClockButton _time;
    private readonly RexLabel _statusChips = MacTheme.Label("", 1, DesignRole.TextSecondary);
    private readonly RexLabel _message = MacTheme.Label("", 1, DesignRole.TextSecondary);
    private readonly RexButton _playPause;
    private readonly TimelineSlider _seek;
    private readonly RexSlider _volume;
    private readonly List<(NSMenuItem Item, string Command)> _checks = [];

    /// <summary>The bar's and the welcome's controls in the order they show, with the ids and kinds ui.describe reports.</summary>
    private readonly List<(string Id, string Kind, NSView View)> _describedControls = [];
    private NSView? _chrome;
    private NSMenu? _pictureMenu;
    private bool _minimalInterface;
    private PlayerSettings _settings;
    private NSTimer? _clearMessage;
    private PictureView? _picture;
    private NSView? _welcome;
    private NSView? _resumeBanner;
    private NSView? _statusLine;
    private RexLabel? _resumeNotice;
    private RexButton? _resumeAccept;
    private RexButton? _resumeStartOver;
    private RexButton? _startOver;
    private RexButton? _speedButton;
    private bool _moving;

    /// <summary>Whether the clock at the right counts down what is left (the default, as on Windows) or shows the whole length.</summary>
    private bool _remainingTime = true;
    private RexButton? _fullScreenTracks;
    private RexButton? _fullScreenEffects;
    private RexButton? _fullScreenLeave;
    private RexButton? _mainFullScreen;
    private RexButton? _mainRepeat;
    private RexButton? _mainShuffle;
    private RexButton? _mainMute;
    private NSTimer? _fullScreenHideTimer;

    public MainWindow(DataRoots roots, RexLog log)
    {
        _roots = roots;
        _log = log;
        _settings = SettingsStore.Load(roots.SettingsPath);
        _keymap = new Keymap(_settings.Shortcuts);
        _player = new PlayerController(NewSession, OpenSource, OnWindowThread, _settings, store: OpenStore(), log: log);
        _player.Changed += (_, _) => ShowState();
        _player.PositionChanged += (_, _) => ShowPosition();
        _player.Message += (_, text) => Say(text);
        _player.SubtitlesChanged += (_, _) => OnWindowThread(() =>
        {
            RefreshTrackTools();
            UpdateSubtitleOverlays();
        });

        _window = new NSWindow(new CGRect(0, 0, 960, 560), NSWindowStyle.Titled | NSWindowStyle.Closable | NSWindowStyle.Miniaturizable | NSWindowStyle.Resizable, NSBackingStore.Buffered, false)
        {
            Title = "rexplayer",
            MinSize = new CGSize(640, 420),
        };
        _window.ReleaseWhenClosed(false);
        _window.AcceptsMouseMovedEvents = true;
        MacTheme.Style(_window);
        _time = new ClockButton(ToggleTimeDisplay);
        _playPause = MacTheme.IconButton("play.fill", "Play", () => Run(CommandCatalog.PlayPause), RexButtonLook.AccentRound);
        _seek = new TimelineSlider(new CGRect(0, 0, 400, 32), ShowSeekPreview, HideSeekPreview)
        {
            MinValue = 0,
            MaxValue = 1,
        };
        _seek.Activated += (_, _) => SeekToSlider();
        _volume = new RexSlider(new CGRect(0, 0, 120, 32))
        {
            MinValue = 0,
            MaxValue = _settings.MaxVolumePercent / 100.0,
            DoubleValue = _player.Volume,
            Continuous = true,
        };
        _volume.Activated += (_, _) => _player.SetVolume(_volume.DoubleValue);
        _window.ContentView = Layout();
        _window.InitialFirstResponder = _playPause;
        WatchFullScreen();
        StartMacKeyboardTraversal();
        RestartGlobalShortcutMonitor();
        InitializeLibrary();
        InitializeQueue();
        NSApplication.SharedApplication.MainMenu = BuildMenus();
        ApplyMacTheme();
        NSTimer.CreateRepeatingScheduledTimer(TimeSpan.FromSeconds(1), _ =>
        {
            _player.SleepTick();
            NameMacControls();
        });
    }

    /// <summary>Shows the window, opening what Finder handed over, or the queue the last run left.</summary>
    public void Show(IReadOnlyList<string> files)
    {
        _window.Center();
        _window.MakeKeyAndOrderFront(null);
        NameMacControls();
        if (files.Count > 0)
        {
            OpenHandedOver(files);
        }
        else if (_player.RestoreAfterUpdate(RunningVersion) is { } restored)
        {
            if (restored.LibraryVisible) ShowLibrary();
            if (_librarySearch is not null) _librarySearch.StringValue = restored.Search;
            if (restored.PlaylistVisible) ShowQueue();
        }
        else
        {
            _player.RestoreQueue();
        }

        ShowState();
        ShowStartupGuidance();
        BeginCrashRecovery();
        StartMediaControls();
        BeginMacUpdateChecks();
        StartMacSpeech();
    }

    public void OpenHandedOver(IReadOnlyList<string> files) => _player.OpenHandedOver(files, _settings.EnqueueFromSecondLaunch, DateTimeOffset.Now);

    /// <summary>Saves what the next run should remember and stops playing.</summary>
    public void Dispose()
    {
        try
        {
            SettingsStore.Save(_roots.SettingsPath, _settings with { Volume = _player.Volume, Muted = _player.Muted });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.Warning(LogSource, "The settings could not be saved: " + ex.Message);
        }

        CloseLibrary();
        CloseQueue();
        DisposeDiagnostics();
        DisposePreferences();
        DisposePlaybackTools();
        DisposeShortcuts();
        DisposeSavedPlaylists();
        DisposeEffects();
        DisposeTrackTools();
        DisposeHistory();
        DisposeStartupGuidance();
        DisposeStats();
        DisposeFullScreenController();
        DisposeTimelinePreview();
        DisposeMusicPresentation();
        DisposeMusicVisualizers();
        EndCrashRecovery();
        DisposePlaybackActivity();
        StopMediaControls();
        DisposeSubtitleStyle();
        DisposeMacUpdates();
        DisposeMacSpeech();
        DisposeMacKinetic();
        DisposeMediaInformation();
        DisposeQueueTiming();
        StopMacKeyboardTraversal();
        _player.Dispose();
        _picture?.Presenter?.Dispose();
        _window.Dispose();
    }

    /// <summary>Answers the automation pipe (TOOL-03) as the Windows window does: open, run or status.</summary>
    public Task<AutomationReply> AnswerAsync(AutomationRequest request)
    {
        var answered = new TaskCompletionSource<AutomationReply>(TaskCreationOptions.RunContinuationsAsynchronously);
        OnWindowThread(() =>
        {
            switch (request.Command)
            {
                case "open":
                    if (request.Paths.Count > 0)
                    {
                        _player.OpenHandedOver(request.Paths, request.Enqueue, request.LaunchedAt ?? DateTimeOffset.Now);
                    }

                    if (OperatingSystem.IsMacOSVersionAtLeast(14))
                    {
                        NSApplication.SharedApplication.Activate();
                    }
                    else
                    {
                        NSApplication.SharedApplication.ActivateIgnoringOtherApps(true);
                    }
                    break;
                case "run" when request.Id is { } id && CommandCatalog.Find(id) is not null:
                    Run(id);
                    break;
                case "run":
                    answered.SetResult(AutomationReply.Failed($"There is no command \"{request.Id}\"."));
                    return;
                case "seek" when request.To is { } seconds && double.IsFinite(seconds) && seconds >= 0:
                    _player.Seek(TimeSpan.FromSeconds(seconds));
                    break;
                case "seek":
                    answered.SetResult(AutomationReply.Failed("seek needs a non-negative time in seconds."));
                    return;
                case "mac-queue":
                    if (!QueueSmokeAction(request.Id))
                    {
                        answered.SetResult(AutomationReply.Failed("The native queue smoke action is unavailable: " + request.Id));
                        return;
                    }

                    break;
                case "mac-library":
                    if (!LibrarySmokeAction(request.Id, request.Paths))
                    {
                        answered.SetResult(AutomationReply.Failed("The native library smoke action is unavailable: " + request.Id));
                        return;
                    }

                    break;
                case "mac-library-backup":
                    answered.SetResult(LibraryBackupSmokeAction(request.Id, request.Path));
                    return;
                case "mac-playlists":
                    if (!SavedPlaylistSmokeAction(request.Id, request.Path))
                    {
                        answered.SetResult(AutomationReply.Failed("Mac playlist action failed: " + request.Id));
                        return;
                    }

                    break;
                case "mac-shortcuts":
                    if (!ShortcutSmokeAction(request.Id))
                    {
                        answered.SetResult(AutomationReply.Failed("Mac shortcut action failed: " + request.Id));
                        return;
                    }

                    break;
                case "mac-keyboard":
                    answered.SetResult(ProbeMacKeyboardTraversal());
                    return;
                case "mac-accessibility":
                    var (controls, missing) = NameMacControls();
                    answered.SetResult(new AutomationReply
                    {
                        Ok = missing == 0,
                        Text = $"controls={controls};missing-labels={missing}",
                    });
                    return;
                case "mac-bookmarks":
                    answered.SetResult(BookmarkSmokeAction(request.Id));
                    return;
                case "mac-pitch-speed":
                    answered.SetResult(PitchSpeedSmokeAction(request.Id));
                    return;
                case "mac-resume":
                    answered.SetResult(ResumeSmokeAction(request.Id));
                    return;
                case "mac-quick-slots":
                    answered.SetResult(QuickSlotSmokeAction(request.Id));
                    return;
                case "mac-startup-guidance":
                    answered.SetResult(StartupGuidanceSmokeAction(request.Id));
                    return;
                case "mac-diagnostics":
                    answered.SetResult(DiagnosticsSmokeAction(request.Id, request.Path));
                    return;
                case "mac-window":
                    answered.SetResult(new AutomationReply
                    {
                        Ok = true,
                        Text = $"fullscreen={IsFullScreen};floating={_window.Level == NSWindowLevel.Floating};minimal={_minimalInterface};context={ContextCommandCount};title={_window.Title}",
                    });
                    return;
                case "mac-fullscreen-controller":
                    answered.SetResult(ProbeFullScreenController());
                    return;
                case "mac-main-window":
                    answered.SetResult(ProbeMainWindow());
                    return;
                case "mac-video-geometry":
                    answered.SetResult(VideoGeometrySmokeAction(request.Id));
                    return;
                case "mac-kinetic-captions":
                    answered.SetResult(MacKineticSmokeAction(request.Id));
                    return;
                case "mac-osd":
                    answered.SetResult(MacOsdSmokeAction(request.Id));
                    return;
                case "mac-speech":
                    answered.SetResult(MacSpeechSmokeAction(request.Id));
                    return;
                case "mac-loudness":
                    answered.SetResult(MacLoudnessSmokeAction(request.Id));
                    return;
                case "mac-equalizer":
                    answered.SetResult(MacEqualizerSmokeAction(request.Id));
                    return;
                case "mac-stereo":
                    answered.SetResult(MacStereoSmokeAction(request.Id));
                    return;
                case "mac-window-sizing":
                    answered.SetResult(MacWindowSizingSmokeAction(request.Id));
                    return;
                case "mac-crash-recovery":
                    answered.SetResult(MacCrashRecoverySmokeAction(request.Id));
                    return;
                case "mac-stats":
                    answered.SetResult(MacStatsSmokeAction(request.Id));
                    return;
                case "mac-drop":
                    _player.Drop(request.Paths ?? [], request.Enqueue);
                    answered.SetResult(new AutomationReply { Ok = true });
                    return;
                case "mac-subtitles":
                    answered.SetResult(SubtitleSmokeAction(request.Id));
                    return;
                case "mac-subtitle-style":
                    answered.SetResult(SubtitleStyleSmokeAction(request.Id));
                    return;
                case "mac-audio":
                    answered.SetResult(new AutomationReply
                    {
                        Ok = true,
                        Text = $"muted={_player.Muted};volume={_player.Volume.ToString("0.###", CultureInfo.InvariantCulture)}",
                    });
                    return;
                case "mac-music":
                    answered.SetResult(MusicSmokeAction(request.Id));
                    return;
                case "mac-snapshot":
                    answered.SetResult(SnapshotSmokeAction(request.Id, request.Path));
                    return;
                case "mac-updates":
                    answered.SetResult(MacUpdateSmokeAction(request.Id));
                    return;
                case "mac-preferences":
                    answered.SetResult(PreferencesSmokeAction(request.Id));
                    return;
                case "status":
                    break;
                case "ui.add-folder":
                    answered.SetResult(LibraryShown
                        ? AddLibraryFolders(request.Paths)
                        : AutomationReply.Failed("Open the library before adding watched folders."));
                    return;
                case "ui.keyboard":
                    answered.SetResult(ProbeLibraryKeyboard());
                    return;
                case "ui.verify-layout":
                    answered.SetResult(VerifyLibraryResultLayout());
                    return;
                case "ui.tree":
                    answered.SetResult(LibraryControlTree());
                    return;
                case "ui.press":
                    answered.SetResult(PressLibraryControl(request.Target));
                    return;
                case "ui.set" when request.Target == "LibraryCategory":
                    answered.SetResult(ChangeLibraryCategory(request.Value));
                    return;
                case "ui.set" when request.Target == "LibraryWidth":
                    answered.SetResult(ResizeLibraryWindow(request.Value));
                    return;
                case "ui.text" when request.Target == "LibrarySummary":
                    answered.SetResult(LibraryShown
                        ? new AutomationReply { Ok = true, Text = _librarySummary?.StringValue }
                        : AutomationReply.Failed("Open the library to read its summary."));
                    return;
                case "ui.text" when request.Target == "LibraryPage":
                    answered.SetResult(LibraryShown
                        ? new AutomationReply { Ok = true, Text = _libraryPageSummary?.StringValue }
                        : AutomationReply.Failed("Open the library to read its page."));
                    return;
                case "ui.search":
                    if (!LibraryShown || _librarySearch is null || request.Target is null)
                    {
                        answered.SetResult(AutomationReply.Failed("Open the library and supply a search query."));
                        return;
                    }

                    _librarySearch.StringValue = request.Target;
                    ChangeLibraryView();
                    answered.SetResult(new AutomationReply { Ok = true, Text = _librarySummary?.StringValue });
                    return;
                case "ui.snapshot":
                    if (string.IsNullOrWhiteSpace(request.Path))
                    {
                        answered.SetResult(AutomationReply.Failed("ui.snapshot needs a path."));
                        return;
                    }

                    try
                    {
                        CaptureWindowSnapshot(request.Path);
                        answered.SetResult(new AutomationReply { Ok = true, Text = request.Path });
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
                    {
                        answered.SetResult(AutomationReply.Failed("Could not capture the window: " + ex.Message));
                    }

                    return;
                case "ui.describe":
                    answered.SetResult(new AutomationReply { Ok = true, Text = DescribeMainControls() });
                    return;
                default:
                    answered.SetResult(AutomationReply.Failed($"\"{request.Command}\" is not a request on this Mac yet; try open, run or status."));
                    return;
            }

            answered.SetResult(new AutomationReply
            {
                Ok = true,
                State = _player.State.ToString(),
                Title = request.Command == "mac-queue"
                    ? (_queueSummary?.StringValue ?? "") + " | " + (_queueShuffle?.Title ?? "") + " | " + (_queueRepeat?.Title ?? "")
                    : request.Command == "mac-library"
                        ? LibrarySmokeTitle()
                        : _player.Item is null ? null : _player.Title,
                Position = _player.Position.TotalSeconds,
                Duration = _player.Duration > TimeSpan.Zero ? _player.Duration.TotalSeconds : null,
            });
        });
        return answered.Task;
    }

    private void OnWindowThread(Action action) => NSApplication.SharedApplication.BeginInvokeOnMainThread(action);

    private static IByteSource OpenSource(string location) =>
        Uri.TryCreate(location, UriKind.Absolute, out var uri) && !uri.IsFile
            ? throw new NotSupportedException("Playing from the network arrives in a later version.")
            : new FileByteSource(location);

    /// <summary>The audio output: the Mac's default device, or nothing audible when REXPLAYER_FAKE_AUDIO=1 (tests).</summary>
    private static IAudioSink NewAudioSink() =>
        Environment.GetEnvironmentVariable("REXPLAYER_FAKE_AUDIO") == "1" ? new NullAudioSink() : new CoreAudioSink();

    private MediaSession NewSession(Action<SessionEvent> listener) => new(
        new EngineOptions
        {
            Demuxers = MediaRegistries.Demuxers(TimeSpan.FromSeconds(_settings.PictureSeconds)),
            Decoders = MediaRegistries.Decoders(new AppleDecoderFactory(
                _settings.HardwareVideoDecoding, _settings.HardwareH264Decoding,
                _settings.HardwareHevcDecoding)),
            GraphicsCardDecoding = _settings.HardwareVideoDecoding,
            AudioSinkFactory = NewAudioSink,
            VideoPresenterFactory = _picture?.Presenter is { } presenter ? () => new BorrowedPresenter(presenter) : null,
            Log = _log,
        },
        listener);

    private RexStore OpenStore()
    {
        try
        {
            return RexStore.Open(_roots.MemoryStore);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.Warning(LogSource, "The store could not be opened, so nothing is remembered this run: " + ex.Message);
            return RexStore.InMemory();
        }
    }

    /// <summary>
    /// The window as Windows lays it out: the black stage with everything shown over the picture,
    /// the Up Next pane at its right, the library over the stage when it is open, and the bar of
    /// controls across the bottom.
    /// </summary>
    private NSView Layout()
    {
        var content = new DropView((paths, enqueue) => _player.Drop(paths, enqueue), HandlePictureWheel,
            HandlePictureDrag, HandlePictureClick, HandlePictureOtherClick, RevealFullScreenController);
        _pictureMenu = BuildPictureMenu();
        content.Menu = _pictureMenu;
        _picture = new PictureView(_log) { TranslatesAutoresizingMaskIntoConstraints = false };
        content.AddSubview(_picture);
        var welcome = BuildWelcome();
        content.AddSubview(welcome);
        AddSubtitleOverlays(content);
        AddStatsOverlay(content);
        AddMusicPresentation(content);
        AddMacKineticLayer(content);

        _libraryHost = MacTheme.Panel(DesignRole.Canvas);
        _libraryHost.Hidden = true;
        content.AddSubview(_libraryHost);
        _queueHost = MacTheme.Panel(DesignRole.Surface);
        _queueHost.Hidden = true;
        content.AddSubview(_queueHost);
        var queueEdge = MacTheme.Separator(vertical: true);
        _queueHost.AddSubview(queueEdge);
        queueEdge.TopAnchor.ConstraintEqualTo(_queueHost.TopAnchor).Active = true;
        queueEdge.BottomAnchor.ConstraintEqualTo(_queueHost.BottomAnchor).Active = true;
        queueEdge.LeadingAnchor.ConstraintEqualTo(_queueHost.LeadingAnchor).Active = true;

        var bar = BuildBottomBar();
        _chrome = bar;
        content.AddSubview(bar);
        AddSeekPreview(content);

        _picture.TopAnchor.ConstraintEqualTo(content.TopAnchor).Active = true;
        _picture.LeadingAnchor.ConstraintEqualTo(content.LeadingAnchor).Active = true;
        _picture.TrailingAnchor.ConstraintEqualTo(_queueHost.LeadingAnchor).Active = true;
        _stageToBar = _picture.BottomAnchor.ConstraintEqualTo(bar.TopAnchor);
        _stageToBar.Active = true;
        _stageToBottom = _picture.BottomAnchor.ConstraintEqualTo(content.BottomAnchor);
        _queueHost.TopAnchor.ConstraintEqualTo(content.TopAnchor).Active = true;
        _queueHost.TrailingAnchor.ConstraintEqualTo(content.TrailingAnchor).Active = true;
        _queueHost.BottomAnchor.ConstraintEqualTo(_picture.BottomAnchor).Active = true;
        _queueHostWidth = _queueHost.WidthAnchor.ConstraintEqualTo(0);
        _queueHostWidth.Active = true;
        _libraryHost.TopAnchor.ConstraintEqualTo(_picture.TopAnchor).Active = true;
        _libraryHost.BottomAnchor.ConstraintEqualTo(_picture.BottomAnchor).Active = true;
        _libraryHost.LeadingAnchor.ConstraintEqualTo(_picture.LeadingAnchor).Active = true;
        _libraryHost.TrailingAnchor.ConstraintEqualTo(_picture.TrailingAnchor).Active = true;
        welcome.CenterXAnchor.ConstraintEqualTo(_picture.CenterXAnchor).Active = true;
        welcome.CenterYAnchor.ConstraintEqualTo(_picture.CenterYAnchor).Active = true;
        welcome.WidthAnchor.ConstraintLessThanOrEqualTo(_picture.WidthAnchor, 1, -64).Active = true;
        bar.LeadingAnchor.ConstraintEqualTo(content.LeadingAnchor).Active = true;
        bar.TrailingAnchor.ConstraintEqualTo(content.TrailingAnchor).Active = true;
        bar.BottomAnchor.ConstraintEqualTo(content.BottomAnchor).Active = true;
        return content;
    }

    /// <summary>
    /// What the stage shows before anything is open, as Windows' idle block: the kicker, the name,
    /// how to begin, the two ways in, and the tagline.
    /// </summary>
    private NSView BuildWelcome()
    {
        var kicker = Described(MacTheme.Kicker("WELCOME", MacTheme.Accent), "IdleKicker", "label");
        var title = Described(MacTheme.Label("rexplayer", 5, MacTheme.OverlayText, semibold: true), "IdleTitle", "label");
        title.Font = NSFont.SystemFontOfSize(44, NSFontWeight.Semibold)!;
        title.Alignment = NSTextAlignment.Center;
        var hint = Described(MacTheme.WrappingLabel("Drop media here, or press ⌘O to open a file.", 2, MacTheme.OverlaySecondaryText), "IdleHint", "label");
        hint.Alignment = NSTextAlignment.Center;
        hint.PreferredMaxLayoutWidth = 560;
        var open = Described(MacTheme.TextButton("Open file", () => Run(CommandCatalog.OpenFile), RexButtonLook.Accent), "WelcomeOpen", "accent-text");
        open.ToolTip = "Open a file (⌘O)";
        var library = Described(MacTheme.TextButton("Browse library", ShowLibrary, RexButtonLook.Overlay), "WelcomeLibrary", "overlay-text");
        open.WidthAnchor.ConstraintGreaterThanOrEqualTo(120).Active = true;
        library.WidthAnchor.ConstraintGreaterThanOrEqualTo(120).Active = true;
        var actions = MacTheme.Stack(false, 1, open, library);
        actions.Spacing = 10;
        var tagline = Described(MacTheme.Label("Your music. Your films. Your space.", 1, MacTheme.OverlaySecondaryText), "WelcomeTagline", "label");
        var welcome = MacTheme.Stack(true, 0, kicker, title, hint, actions, tagline);
        welcome.Alignment = NSLayoutAttribute.CenterX;
        welcome.Spacing = 7;
        welcome.SetCustomSpacing(7 + 18, hint);
        welcome.SetCustomSpacing(14, actions);
        welcome.AccessibilityIdentifier = "Idle";
        _welcome = welcome;
        return welcome;
    }

    /// <summary>
    /// The bar across the bottom, as Windows' own: the banners that ask something, the status line,
    /// the seek row (Start over, elapsed, the seek bar, remaining) and the transport row (the
    /// buttons, what is playing, then speed and sound).
    /// </summary>
    private NSView BuildBottomBar()
    {
        var bar = MacTheme.Panel(DesignRole.Surface);
        bar.AccessibilityIdentifier = "Controls";
        var edge = MacTheme.Separator();
        bar.AddSubview(edge);
        edge.TopAnchor.ConstraintEqualTo(bar.TopAnchor).Active = true;
        edge.LeadingAnchor.ConstraintEqualTo(bar.LeadingAnchor).Active = true;
        edge.TrailingAnchor.ConstraintEqualTo(bar.TrailingAnchor).Active = true;

        // The seek row.
        _startOver = Described(MacTheme.TextButton("Start over", StartOverFromBar, label: "Start this media from the beginning"), "StartOverButton", "text");
        _startOver.Enabled = false;
        Described(_elapsed, "Elapsed", "label");
        _elapsed.Font = MacTheme.DigitFont(2);
        ((NSView)_elapsed).AccessibilityLabel = "Elapsed time";
        Described(_seek, "SeekBar", "slider");
        _seek.Continuous = false;
        _seek.ToolTip = "Seek through the current file";
        ((NSView)_seek).AccessibilityLabel = "Position";
        _seek.HeightAnchor.ConstraintEqualTo(32).Active = true;
        _seek.SetContentHuggingPriorityForOrientation(1, NSLayoutConstraintOrientation.Horizontal);
        _seek.SetContentCompressionResistancePriority(250, NSLayoutConstraintOrientation.Horizontal);
        Described(_time, "Remaining", "label");
        var seekRow = MacTheme.Stack(false, 2, _startOver, _elapsed, _seek, _time);
        seekRow.SetCustomSpacing(MacTheme.Space(1), _startOver);

        // The transport row: the buttons at the left.
        var previous = Described(MacTheme.IconButton("backward.end.fill", "Previous", () => Run(CommandCatalog.Previous)), "PreviousButton", "icon");
        Described(_playPause, "PlayPauseButton", "accent-icon");
        var stop = Described(MacTheme.IconButton("stop.fill", "Stop", () => Run(CommandCatalog.Stop)), "StopButton", "icon");
        var next = Described(MacTheme.IconButton("forward.end.fill", "Next", () => Run(CommandCatalog.Next)), "NextButton", "icon");
        var divider = MacTheme.Separator(vertical: true);
        divider.HeightAnchor.ConstraintEqualTo(24).Active = true;
        var shuffle = _mainShuffle = Described(MacTheme.IconButton("shuffle", "Shuffle", () => Run(CommandCatalog.ToggleShuffle), RexButtonLook.IconToggle), "ShuffleButton", "icon-toggle");
        var repeat = _mainRepeat = Described(MacTheme.IconButton("repeat", "Repeat off", () => Run(CommandCatalog.CycleRepeat), RexButtonLook.IconToggle), "RepeatButton", "icon-toggle");
        var queue = _shellQueueToggle = Described(MacTheme.IconButton("list.bullet", "Up Next", ToggleQueue, RexButtonLook.IconToggle), "PlaylistButton", "icon-toggle");
        var library = _shellLibraryToggle = Described(MacTheme.IconButton("books.vertical", "Library", ToggleLibrary, RexButtonLook.IconToggle), "LibraryButton", "icon-toggle");
        var fullScreen = _mainFullScreen = Described(MacTheme.IconButton("arrow.up.left.and.arrow.down.right", "Full screen", () => Run(CommandCatalog.ToggleFullScreen)), "FullScreenButton", "icon");
        _fullScreenTracks = Described(MacTheme.IconButton("captions.bubble", "Tracks", ShowTrackTools), "FullScreenTracksButton", "icon");
        _fullScreenEffects = Described(MacTheme.IconButton("slider.horizontal.3", "Effects", ShowEffects), "FullScreenEffectsButton", "icon");
        _fullScreenLeave = Described(MacTheme.IconButton("arrow.down.right.and.arrow.up.left", "Exit full screen", () => Run(CommandCatalog.LeaveFullScreen)), "LeaveFullScreenButton", "icon");
        previous.ToolTip = "Previous item";
        _playPause.ToolTip = "Play or pause (Space)";
        stop.ToolTip = "Stop playback";
        next.ToolTip = "Next item";
        shuffle.ToolTip = "Shuffle the queue";
        repeat.ToolTip = "Change repeat mode";
        queue.ToolTip = "View, reorder, and manage what plays next";
        library.ToolTip = "Browse your own local library and watched folders";
        fullScreen.ToolTip = "Toggle full screen";
        var buttons = MacTheme.Stack(false, 0, previous, _playPause, stop, next, divider, shuffle, repeat, queue,
            library, fullScreen, _fullScreenTracks, _fullScreenEffects, _fullScreenLeave);
        ShowFullScreenButtons();

        // What is playing, in the middle.
        Described(_title, "NowPlaying", "label");
        ((NSView)_artist).AccessibilityIdentifier = "NowPlayingContext";
        foreach (var line in new[] { _title, _artist })
        {
            line.SetContentCompressionResistancePriority(100, NSLayoutConstraintOrientation.Horizontal);
            line.SetContentHuggingPriorityForOrientation(1, NSLayoutConstraintOrientation.Horizontal);
        }

        var nowPlaying = MacTheme.Stack(true, 0, _title, _artist);
        nowPlaying.Spacing = 1;
        nowPlaying.SetContentHuggingPriorityForOrientation(1, NSLayoutConstraintOrientation.Horizontal);
        nowPlaying.SetContentCompressionResistancePriority(100, NSLayoutConstraintOrientation.Horizontal);

        // Speed and sound at the right.
        _speedButton = Described(MacTheme.TextButton("1×", ShowSpeedMenu, label: "Playback speed, 1×"), "SpeedButton", "text");
        _speedButton.HeightAnchor.ConstraintEqualTo((nfloat)MacTheme.ControlSize).Active = true;
        var mute = _mainMute = Described(MacTheme.IconButton("speaker.wave.2.fill", "Mute", () => Run(CommandCatalog.Mute)), "MuteButton", "icon");
        mute.ToolTip = "Mute or unmute";
        Described(_volume, "VolumeBar", "slider");
        _volume.ToolTip = "Volume";
        ((NSView)_volume).AccessibilityLabel = "Volume";
        _volume.WidthAnchor.ConstraintEqualTo(120).Active = true;
        _volume.HeightAnchor.ConstraintEqualTo(32).Active = true;
        var sound = MacTheme.Stack(false, 0, _speedButton, mute, _volume);
        var transport = MacTheme.Stack(false, 2, buttons, nowPlaying, sound);

        // The lines that come and go above the seek row.
        _statusChips.Font = NSFont.SystemFontOfSize(MacTheme.TypeSize(1), NSFontWeight.Medium)!;
        _statusLine = MacTheme.Stack(false, 2, _statusChips, _message);
        _statusLine.Hidden = true;
        _resumeNotice = MacTheme.WrappingLabel("Continue where you stopped?");
        _resumeNotice.SetContentHuggingPriorityForOrientation(1, NSLayoutConstraintOrientation.Horizontal);
        _resumeAccept = MacTheme.TextButton("Resume", () => _player.AcceptResume(), RexButtonLook.Accent);
        _resumeStartOver = MacTheme.TextButton("Start over", StartOverFromBar);
        _resumeBanner = Banner(false, _resumeNotice, _resumeAccept, _resumeStartOver);
        var (confirmation, entry) = BuildConfirmationBanners();

        var rows = MacTheme.Stack(true, 1, BuildCrashRecoveryBanner(), confirmation, entry, _resumeBanner, _statusLine, seekRow, transport);
        rows.Alignment = NSLayoutAttribute.Width;
        bar.AddSubview(rows);
        rows.TopAnchor.ConstraintEqualTo(edge.BottomAnchor, MacTheme.Space(1)).Active = true;
        rows.BottomAnchor.ConstraintEqualTo(bar.BottomAnchor, -MacTheme.Space(1)).Active = true;
        rows.LeadingAnchor.ConstraintEqualTo(bar.LeadingAnchor, MacTheme.Space(3)).Active = true;
        rows.TrailingAnchor.ConstraintEqualTo(bar.TrailingAnchor, -MacTheme.Space(3)).Active = true;

        // Tab walks the bar as it reads, left to right and row by row; F6 jumps between regions.
        NSView[] order = [_startOver, _seek, previous, _playPause, stop, next, shuffle, repeat, queue, library,
            fullScreen, _fullScreenTracks, _fullScreenEffects, _fullScreenLeave, _speedButton, mute, _volume];
        for (var index = 0; index < order.Length; index++)
        {
            order[index].NextKeyView = order[(index + 1) % order.Length];
        }

        SetMainKeyViews(order);
        return bar;
    }

    /// <summary>Records <paramref name="view"/> under the Windows automation id of the same control, for ui.describe and assistive tools.</summary>
    private T Described<T>(T view, string id, string kind) where T : NSView
    {
        view.AccessibilityIdentifier = id;
        _describedControls.Add((id, kind, view));
        return view;
    }

    /// <summary>The controls of the bar and the welcome a person can see now, as "id:kind" in the order they show.</summary>
    private string DescribeMainControls() => string.Join(";", _describedControls
        .Where(control => !control.View.IsHiddenOrHasHiddenAncestor
            && !(LibraryShown && _welcome is not null && control.View.IsDescendantOf(_welcome)))
        .Select(control => control.Id + ":" + control.Kind));

    /// <summary>Saves the window's whole content (stage or library, Up Next, the bar) as a PNG at <paramref name="path"/>.</summary>
    private void CaptureWindowSnapshot(string path)
    {
        if (_window.ContentView is not { } content)
        {
            throw new InvalidOperationException("The window has nothing to capture.");
        }

        content.LayoutSubtreeIfNeeded();
        using var bitmap = content.BitmapImageRepForCachingDisplayInRect(content.Bounds)
            ?? throw new InvalidOperationException("AppKit could not capture the window.");
        content.CacheDisplay(content.Bounds, bitmap);
        using var png = bitmap.RepresentationUsingTypeProperties(NSBitmapImageFileType.Png)
            ?? throw new InvalidOperationException("AppKit could not encode the window's picture.");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        if (!png.Save(NSUrl.FromFilename(Path.GetFullPath(path)), true))
        {
            throw new IOException("Could not save the window's picture.");
        }
    }

    /// <summary>Starts the media over from its beginning, as the bar's and the resume banner's Start over both do.</summary>
    private void StartOverFromBar()
    {
        _player.DismissPendingResume();
        _player.StartOver();
    }

    /// <summary>The speed as the speed button shows it: "1×", "1.5×".</summary>
    private static string SpeedLabel(double speed) => speed.ToString("0.##", CultureInfo.InvariantCulture) + "×";

    /// <summary>Offers slower and faster speeds in one place under the speed button, as Windows does, with the full playback tools after them.</summary>
    private void ShowSpeedMenu()
    {
        if (_speedButton is null)
        {
            return;
        }

        var menu = new NSMenu("Playback speed");
        menu.AddItem(new NSMenuItem("Slower", (_, _) => Run(CommandCatalog.Slower)));
        menu.AddItem(new NSMenuItem("Normal speed (1×)", (_, _) => Run(CommandCatalog.NormalSpeed)));
        menu.AddItem(new NSMenuItem("Faster", (_, _) => Run(CommandCatalog.Faster)));
        menu.AddItem(NSMenuItem.SeparatorItem);
        foreach (var rate in PlayerController.SpeedSteps)
        {
            var chosen = rate;
            menu.AddItem(new NSMenuItem(SpeedLabel(rate), (_, _) => _player.SetSpeed(chosen))
            {
                State = Math.Abs(rate - _player.Speed) < 0.005 ? NSCellStateValue.On : NSCellStateValue.Off,
            });
        }

        menu.AddItem(NSMenuItem.SeparatorItem);
        menu.AddItem(new NSMenuItem("Playback tools…", (_, _) => ShowPlaybackTools()));
        menu.PopUpMenu(null, new CGPoint(0, _speedButton.Bounds.Height + 4), _speedButton);
    }

    /// <summary>Shows Tracks, Effects and Exit full screen only in full screen, where the menus are out of reach.</summary>
    private void ShowFullScreenButtons()
    {
        foreach (var button in new[] { _fullScreenTracks, _fullScreenEffects, _fullScreenLeave })
        {
            if (button is not null)
            {
                button.Hidden = !IsFullScreen;
            }
        }
    }

    /// <summary>Shows the status line only while it has chips or a message to show.</summary>
    private void RefreshStatusLine()
    {
        if (_statusLine is not null)
        {
            _statusLine.Hidden = _statusChips.StringValue.Length == 0 && _message.StringValue.Length == 0;
        }
    }

    /// <summary>The menu bar: the app's own menu as every Mac app has it, then the menus Windows has, from the same layout.</summary>
    private NSMenu BuildMenus()
    {
        _checks.Clear();
        var bar = new NSMenu();
        var app = new NSMenu("rexplayer");
        app.AddItem(new NSMenuItem("About rexplayer", (_, _) => NSApplication.SharedApplication.OrderFrontStandardAboutPanel(null)));
        app.AddItem(NSMenuItem.SeparatorItem);
        app.AddItem(Entry(CommandCatalog.Preferences, "Settings…"));
        app.AddItem(new NSMenuItem("Playback tools…", (_, _) => ShowPlaybackTools()));
        app.AddItem(Entry(CommandCatalog.KeyboardAndMouse, "Keyboard shortcuts…"));
        app.AddItem(new NSMenuItem("Named playlists…", (_, _) => ShowSavedPlaylists()));
        app.AddItem(Entry(CommandCatalog.Effects, "Effects & filters…"));
        app.AddItem(new NSMenuItem("Audio visualisations…", (_, _) => ShowMusicVisualizers()));
        app.AddItem(new NSMenuItem("Tracks & subtitle sync…", (_, _) => ShowTrackTools()));
        app.AddItem(new NSMenuItem("Subtitle appearance…", (_, _) => ShowSubtitleStyle()));
        app.AddItem(new NSMenuItem("Software updates…", (_, _) => ShowMacUpdates()));
        app.AddItem(new NSMenuItem("Offline transcription…", (_, _) => ShowMacSpeech()));
        app.AddItem(new NSMenuItem("Kinetic captions…", (_, _) => ShowMacKineticSettings()));
        app.AddItem(new NSMenuItem("Recent media & quick slots…", (_, _) => ShowHistory()));
        app.AddItem(new NSMenuItem("Your library…", (_, _) => Run(CommandCatalog.ToggleLibrary)));
        app.AddItem(new NSMenuItem("Up Next…", (_, _) => ShowQueue()));
        // Reuse the shared command and its customizable keyboard equivalent.
        app.AddItem(Entry(CommandCatalog.ToggleLibrary, "Library…"));
        app.AddItem(NSMenuItem.SeparatorItem);
        app.AddItem(new NSMenuItem("Hide rexplayer", "h", (_, _) => NSApplication.SharedApplication.Hide(NSApplication.SharedApplication)));
        app.AddItem(new NSMenuItem("Quit rexplayer", "q", (_, _) => NSApplication.SharedApplication.Terminate(null)));
        bar.AddItem(new NSMenuItem("rexplayer") { Submenu = app });
        foreach (var (title, commands) in MenuBarLayout.Menus)
        {
            bar.AddItem(new NSMenuItem(title) { Submenu = Fill(new NSMenu(title), commands) });
        }

        return bar;
    }

    /// <summary>
    /// The picture menu uses the shared context layout, so every command stays available when
    /// the ordinary controls are hidden or the window is full screen (UI-03/UI-05).
    /// </summary>
    private NSMenu BuildPictureMenu()
    {
        var menu = new NSMenu("Playback");
        foreach (var (title, commands) in ContextMenuLayout.Groups)
        {
            if (title.Length == 0)
            {
                FillContextGroup(menu, commands);
            }
            else
            {
                var group = new NSMenu(title);
                FillContextGroup(group, commands);
                menu.AddItem(new NSMenuItem(title) { Submenu = group });
            }
        }

        return menu;
    }

    private void FillContextGroup(NSMenu menu, IEnumerable<string?> commands)
    {
        foreach (var command in commands)
        {
            menu.AddItem(command is null ? NSMenuItem.SeparatorItem : Entry(command, null));
        }
    }

    private static int ContextCommandCount => ContextMenuLayout.Groups.Sum(group => group.Commands.Count(command => command is not null));

    private NSMenu Fill(NSMenu menu, string?[] entries)
    {
        foreach (var entry in entries)
        {
            if (entry is null)
            {
                menu.AddItem(NSMenuItem.SeparatorItem);
            }
            else if (MenuBarLayout.SubMenus.TryGetValue(entry, out var group))
            {
                menu.AddItem(new NSMenuItem(group.Title) { Submenu = Fill(new NSMenu(group.Title), group.Commands) });
            }
            else if (entry != CommandCatalog.Quit && entry != CommandCatalog.Preferences && entry != CommandCatalog.About)
            {
                // Quit, Settings and About live in the app menu on a Mac, where people look for them.
                menu.AddItem(Entry(entry, null));
            }
        }

        return menu;
    }

    private NSMenuItem Entry(string id, string? title)
    {
        var command = CommandCatalog.Find(id)!;
        var item = new NSMenuItem(title ?? CommandCatalog.TitleOf(command, _settings), (_, _) => Run(id));
        if (_keymap.ShortcutsFor(id) is [var chord, ..] && MacKeyEquivalents.For(chord) is { } key)
        {
            item.KeyEquivalent = key.Key;
            item.KeyEquivalentModifierMask = (key.Command ? NSEventModifierMask.CommandKeyMask : 0)
                | (key.Option ? NSEventModifierMask.AlternateKeyMask : 0)
                | (key.Shift ? NSEventModifierMask.ShiftKeyMask : 0);
        }

        if (MenuBarLayout.IsOnOff(id))
        {
            _checks.Add((item, id));
        }

        return item;
    }

    /// <summary>Runs a command: playback commands go to the player, the rest are the window's own.</summary>
    private void Run(string command)
    {
        if (!_player.Execute(command) && !RunWindowCommand(command))
        {
            // Said aloud and logged, never silently dropped: the parity ledger lists what is still owed here.
            Say(CommandCatalog.Find(command)?.Title + " comes to the Mac in a later version.");
            _log.Info(LogSource, "Not on the Mac yet: " + command);
        }

        ShowChecks();
    }

    /// <summary>Whether what an on-and-off command switches is on now, for its tick.</summary>
    private bool IsOn(string command) => command switch
    {
        CommandCatalog.ToggleLibrary => LibraryShown,
        CommandCatalog.TogglePlaylist => QueueShown,
        CommandCatalog.ToggleShuffle => _player.Playlist.Shuffle,
        CommandCatalog.CycleRepeat => _player.Playlist.Repeat != RepeatMode.Off,
        CommandCatalog.Mute => _player.Muted,
        CommandCatalog.StopAfterCurrent => _player.AfterCurrent == AfterItem.Stop,
        CommandCatalog.PauseAfterCurrent => _player.AfterCurrent == AfterItem.Pause,
        CommandCatalog.SleepAtEndOfItem => _player.SleepAtEndOfItem,
        CommandCatalog.ToggleFullScreen => IsFullScreen,
        CommandCatalog.ToggleAlwaysOnTop => _window.Level == NSWindowLevel.Floating,
        CommandCatalog.ToggleStats => _statsVisible,
        CommandCatalog.ToggleNavigator => _settings.ShowNavigator,
        CommandCatalog.MinimalInterface => _minimalInterface,
        _ => false,
    };

    private void ShowChecks()
    {
        foreach (var (item, command) in _checks)
        {
            item.State = IsOn(command) ? NSCellStateValue.On : NSCellStateValue.Off;
        }
    }

    private void ShowState()
    {
        if (_welcome is not null)
        {
            _welcome.Hidden = _player.Item is not null;
        }
        _title.StringValue = _player.Item is null ? "" : _player.Title;
        _artist.StringValue = _player.Item is null ? "" : _player.Artist ?? "";
        _artist.Hidden = _artist.StringValue.Length == 0;
        _window.Title = _player.Item is null ? "rexplayer" : _player.Title + " - rexplayer";
        ShowTitleOnPlaybackStart();
        _playPause.Symbol = _player.IsPlaying ? "pause.fill" : "play.fill";
        _playPause.Label = _player.IsPlaying ? "Pause" : "Play";
        _seek.Enabled = _player.CanSeek;
        if (_startOver is not null) _startOver.Enabled = _player.CanSeek;
        if (_resumeBanner is not null && _resumeNotice is not null)
        {
            _resumeBanner.Hidden = _player.PendingResumeAt is not { } at || !_player.CanSeek;
            if (_player.PendingResumeAt is { } offered)
            {
                _resumeNotice.StringValue = "Resume from " + TimeText.Format(offered, _player.Duration) + "?";
            }
        }

        _volume.DoubleValue = _player.Volume;
        if (_mainMute is not null)
        {
            _mainMute.Symbol = _player.Muted ? "speaker.slash.fill" : "speaker.wave.2.fill";
            _mainMute.Label = _player.Muted ? "Unmute" : "Mute";
        }

        if (_mainShuffle is not null) _mainShuffle.On = _player.Playlist.Shuffle;
        if (_mainRepeat is not null)
        {
            _mainRepeat.Symbol = _player.Playlist.Repeat == RepeatMode.One ? "repeat.1" : "repeat";
            _mainRepeat.On = _player.Playlist.Repeat != RepeatMode.Off;
            _mainRepeat.Label = _player.Playlist.Repeat switch
            {
                RepeatMode.All => "Repeat all",
                RepeatMode.One => "Repeat one",
                _ => "Repeat off",
            };
        }

        if (_mainFullScreen is not null)
        {
            _mainFullScreen.Symbol = IsFullScreen ? "arrow.down.right.and.arrow.up.left" : "arrow.up.left.and.arrow.down.right";
            _mainFullScreen.Label = IsFullScreen ? "Exit full screen" : "Full screen";
        }

        ShowFullScreenButtons();
        if (_speedButton is not null)
        {
            var speed = SpeedLabel(_player.Speed);
            _speedButton.Text = speed;
            _speedButton.Label = "Playback speed, " + speed;
        }

        RefreshShellToggles();
        _statusChips.StringValue = string.Join("  •  ", new[]
        {
            _settings.HardwareVideoDecoding ? "Hardware decoding" : null,
            Math.Abs(_player.Speed - 1) > 0.001 ? $"{_player.Speed:0.##}× speed" : null,
        }.Where(chip => chip is not null));
        RefreshStatusLine();
        ShowPosition();
        UpdateDockTile();
        UpdateSystemNowPlaying();
        UpdatePlaybackActivity();
        UpdateSubtitleOverlays();
        ApplyAutomaticWindowSize();
        ShowChecks();
        RefreshPlaybackTools();
        RefreshTrackTools();
        RefreshHistory();
        RefreshMusicPresentation();
    }

    // Opening saves where the playing item is, which would overwrite the planted point, so the
    // session ends first and the planted point is the one the reopened item finds.
    private void OfferResumeAt15(string location)
    {
        _player.Stop();
        _player.Memory.Left(location, TimeSpan.FromSeconds(15), TimeSpan.Zero);
        _player.Open([location]);
    }

    /// <summary>Exercise the native, nonmodal resume offer and its two real buttons.</summary>
    private AutomationReply ResumeSmokeAction(string? action)
    {
        if (_player.Item is not { } item)
        {
            return AutomationReply.Failed("Open media before checking resume behavior.");
        }

        switch (action)
        {
            case "offer":
                _settings = _settings with { ResumeMode = ResumeBehavior.Ask };
                _player.Settings = _settings;
                OfferResumeAt15(item.Location);
                break;
            case "accept":
                _resumeAccept?.PerformClick(_resumeAccept);
                break;
            case "start-over":
                _resumeStartOver?.PerformClick(_resumeStartOver);
                break;
            case "always":
                _settings = _settings with { ResumeMode = ResumeBehavior.Always };
                _player.Settings = _settings;
                OfferResumeAt15(item.Location);
                break;
            case "never":
                _settings = _settings with { ResumeMode = ResumeBehavior.Never };
                _player.Settings = _settings;
                OfferResumeAt15(item.Location);
                break;
            case "status":
                break;
            default:
                return AutomationReply.Failed("Unknown resume action: " + action);
        }

        ShowState();
        return new AutomationReply
        {
            Ok = true,
            Text = $"mode={_settings.ResumeMode};pending={_player.PendingResumeAt?.TotalSeconds.ToString(CultureInfo.InvariantCulture) ?? "none"};visible={_resumeBanner?.Hidden == false};remembered={_player.Memory.ResumePoint(item.Location)?.TotalSeconds.ToString(CultureInfo.InvariantCulture) ?? "none"}",
            Position = _player.Position.TotalSeconds,
        };
    }

    private void ShowPosition()
    {
        if (_moving)
        {
            return;
        }

        var duration = _player.Duration;
        _seek.DoubleValue = duration > TimeSpan.Zero ? Math.Clamp(_player.Position / duration, 0, 1) : 0;
        _seek.Buffered = _player.Item?.Location is { } location && File.Exists(location) ? 1 : _seek.DoubleValue;
        _seek.Chapters = duration > TimeSpan.Zero && _player.Info is { } info
            ? [.. info.Chapters.Select(chapter => Math.Clamp(chapter.Start.ToTimeSpan() / duration, 0, 1))]
            : [];
        _seek.NeedsDisplay = true;
        var (elapsed, remaining) = TimeText.Clocks(_player.Position, duration, _player.Item is null ? null : _player.State, _remainingTime);
        _elapsed.StringValue = elapsed;
        _time.Clock = remaining;
        UpdateSubtitleOverlays();
        UpdateSystemNowPlaying();
        RefreshMusicPosition();
    }

    private void ToggleTimeDisplay()
    {
        _remainingTime = !_remainingTime;
        ShowPosition();
    }

    private AutomationReply ProbeMainWindow()
    {
        var before = _remainingTime;
        ToggleTimeDisplay();
        var toggled = _remainingTime != before;
        ShowSeekPreview(0.5);
        var preview = _seekPreview?.Hidden == false && !string.IsNullOrWhiteSpace(_seekPreviewTime?.StringValue);
        HideSeekPreview();
        var controls = new[] { _mainFullScreen, _mainRepeat, _mainShuffle, _mainMute,
            _fullScreenTracks, _fullScreenEffects, _fullScreenLeave };
        var complete = controls.All(button => button is { Enabled: true }) && toggled && preview
            && _seek.Buffered >= 0 && _seek.Chapters is not null && _mainKeyViews.Count == 17;
        return complete
            ? new AutomationReply
            {
                Ok = true,
                Text = $"transport={controls.Length};timeline=elapsed,remaining,chapters,buffer,preview;" +
                    $"volume=True;chips={_statusChips.StringValue};keys={_mainKeyViews.Count}",
            }
            : AutomationReply.Failed("The native main-window controls are incomplete.");
    }

    private void SeekToSlider()
    {
        _moving = true;
        try
        {
            if (_player.CanSeek)
            {
                _player.Seek(_player.Duration * _seek.DoubleValue);
            }
        }
        finally
        {
            _moving = false;
        }
    }

    private static readonly TimeSpan MessageAtLeast = TimeSpan.FromSeconds(3);

    private void Say(string text)
    {
        _message.StringValue = text;
        RefreshStatusLine();
        var showOverPicture = OnScreenMessage.Shows(text, _settings.OnScreenMessages);
        SetVideoOSD(showOverPicture ? text : "");
        _clearMessage?.Invalidate();
        // The line doubles as the window's status, so it keeps at least three seconds; a long
        // message or a problem stays as long as it takes to read.
        var readFor = OnScreenMessage.Duration(text);
        _clearMessage = NSTimer.CreateScheduledTimer(readFor > MessageAtLeast ? readFor : MessageAtLeast, _ =>
        {
            _message.StringValue = "";
            RefreshStatusLine();
            SetVideoOSD("");
        });
    }

}
