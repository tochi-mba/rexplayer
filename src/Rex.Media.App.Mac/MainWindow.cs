using System.Globalization;
using Rex.Media.AppCore;
using Rex.Media.AppCore.Automation;
using Rex.Media.AppCore.Commands;
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
    private readonly NSTextField _title = NSTextField.CreateLabel("Drop media here, or press Cmd+O to open a file.");
    private readonly NSTextField _artist = NSTextField.CreateLabel("");
    private readonly NSButton _time;
    private readonly NSTextField _statusChips = NSTextField.CreateLabel("");
    private readonly NSTextField _message = NSTextField.CreateLabel("");
    private readonly NSButton _playPause;
    private readonly TimelineSlider _seek;
    private readonly NSSlider _volume;
    private readonly List<(NSMenuItem Item, string Command)> _checks = [];
    private NSStackView? _chrome;
    private NSMenu? _pictureMenu;
    private bool _minimalInterface;
    private PlayerSettings _settings;
    private NSTimer? _clearMessage;
    private PictureView? _picture;
    private NSStackView? _welcome;
    private NSStackView? _resumeBanner;
    private NSTextField? _resumeNotice;
    private NSButton? _resumeAccept;
    private NSButton? _resumeStartOver;
    private bool _moving;
    private bool _remainingTime;
    private NSButton? _fullScreenTracks;
    private NSButton? _fullScreenEffects;
    private NSButton? _fullScreenLeave;
    private NSButton? _mainFullScreen;
    private NSButton? _mainRepeat;
    private NSButton? _mainShuffle;
    private NSButton? _mainMute;
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
        _time = NSButton.CreateButton("0:00", ToggleTimeDisplay);
        _playPause = NSButton.CreateButton("Play", () => Run(CommandCatalog.PlayPause));
        _seek = new TimelineSlider(new CGRect(0, 0, 400, 24), ShowSeekPreview, HideSeekPreview)
        {
            MinValue = 0,
            MaxValue = 1,
        };
        _seek.Activated += (_, _) => SeekToSlider();
        _volume = NSSlider.FromValue(_player.Volume, 0, _settings.MaxVolumePercent / 100.0, () => _player.SetVolume(_volume!.DoubleValue));
        _window.ContentView = Layout();
        _window.InitialFirstResponder = _playPause;
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
                    if (request.Id == "visualisers")
                    {
                        answered.SetResult(MacVisualizerSmokeAction());
                        return;
                    }
                    RefreshMusicPresentation();
                    RefreshMusicPosition();
                    answered.SetResult(new AutomationReply
                    {
                        Ok = true,
                        Text = $"visible={_musicPresentation?.Hidden == false};art={_musicArtwork?.Hidden == false};heading={_musicHeading?.StringValue};lyrics={_musicLyrics?.Hidden == false};visualizer={_settings.Visualizer};meter={_musicSpectrum?.StringValue.Length ?? 0};text={_musicLyrics?.StringValue}",
                    });
                    return;
                case "mac-snapshot":
                    answered.SetResult(SnapshotSmokeAction(request.Id, request.Path));
                    return;
                case "status":
                    break;
                case "ui.add-folder":
                    answered.SetResult(_libraryWindow?.IsVisible == true
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
                    answered.SetResult(_libraryWindow?.IsVisible == true
                        ? new AutomationReply { Ok = true, Text = _librarySummary?.StringValue }
                        : AutomationReply.Failed("Open the library to read its summary."));
                    return;
                case "ui.text" when request.Target == "LibraryPage":
                    answered.SetResult(_libraryWindow?.IsVisible == true
                        ? new AutomationReply { Ok = true, Text = _libraryPageSummary?.StringValue }
                        : AutomationReply.Failed("Open the library to read its page."));
                    return;
                case "ui.search":
                    if (_libraryWindow?.IsVisible != true || _librarySearch is null || request.Target is null)
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
                        CaptureLibraryScreenshot(request.Path);
                        answered.SetResult(new AutomationReply { Ok = true, Text = request.Path });
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
                    {
                        answered.SetResult(AutomationReply.Failed("Could not capture the library: " + ex.Message));
                    }

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

    private NSView Layout()
    {
        _title.Font = NSFont.SystemFontOfSize(18, NSFontWeight.Semibold)!;
        _title.LineBreakMode = NSLineBreakMode.TruncatingTail;
        _artist.Font = NSFont.SystemFontOfSize(12, NSFontWeight.Regular)!;
        _artist.TextColor = NSColor.SecondaryLabel;
        _artist.LineBreakMode = NSLineBreakMode.TruncatingTail;
        _message.TextColor = NSColor.SecondaryLabel;
        _seek.Continuous = false;
        _time.Font = NSFont.MonospacedDigitSystemFontOfSize(13, NSFontWeight.Regular)!;
        _time.BezelStyle = NSBezelStyle.Inline;
        _time.ToolTip = "Click to switch between elapsed and remaining time";
        ((NSView)_time).AccessibilityLabel = "Elapsed and remaining playback time";
        var previous = NSButton.CreateButton("Previous", () => Run(CommandCatalog.Previous));
        var stop = NSButton.CreateButton("Stop", () => Run(CommandCatalog.Stop));
        var next = NSButton.CreateButton("Next", () => Run(CommandCatalog.Next));
        var open = NSButton.CreateButton("Open file", () => Run(CommandCatalog.OpenFile));
        var queue = NSButton.CreateButton("Up Next", ShowQueue);
        var library = NSButton.CreateButton("Library", ShowLibrary);
        var fullScreen = _mainFullScreen = NSButton.CreateButton("Full screen", () => Run(CommandCatalog.ToggleFullScreen));
        var repeat = _mainRepeat = NSButton.CreateButton("Repeat", () => Run(CommandCatalog.CycleRepeat));
        var shuffle = _mainShuffle = NSButton.CreateButton("Shuffle", () => Run(CommandCatalog.ToggleShuffle));
        var mute = _mainMute = NSButton.CreateButton("Mute", () => Run(CommandCatalog.Mute));
        _fullScreenTracks = NSButton.CreateButton("Tracks", ShowTrackTools);
        _fullScreenEffects = NSButton.CreateButton("Effects", ShowEffects);
        _fullScreenLeave = NSButton.CreateButton("Exit full screen", () => Run(CommandCatalog.LeaveFullScreen));
        library.ToolTip = "Browse your own local library and watched folders";
        queue.ToolTip = "View, reorder, and manage what plays next";
        open.ToolTip = "Open a file (Cmd+O)";
        _playPause.BezelStyle = NSBezelStyle.Rounded;
        _playPause.Font = NSFont.SystemFontOfSize(15, NSFontWeight.Semibold)!;
        _playPause.WidthAnchor.ConstraintEqualTo(76).Active = true;
        _playPause.ToolTip = "Play or pause (Space)";
        _seek.ToolTip = "Seek through the current file";
        _volume.ToolTip = "Volume";
        var controls = NSStackView.FromViews([previous, _playPause, stop, next, fullScreen, open, library, queue,
            _fullScreenTracks, _fullScreenEffects, repeat, shuffle, mute, _fullScreenLeave, _volume]);
        controls.Orientation = NSUserInterfaceLayoutOrientation.Horizontal;
        controls.Spacing = 8;
        foreach (var button in controls.ArrangedSubviews.OfType<NSButton>())
        {
            button.HeightAnchor.ConstraintGreaterThanOrEqualTo(32).Active = true;
        }
        _volume.WidthAnchor.ConstraintEqualTo(100).Active = true;
        _seek.SetContentHuggingPriorityForOrientation(1, NSLayoutConstraintOrientation.Horizontal);
        // Keep a deterministic keyboard path around every primary transport control.
        previous.NextKeyView = _playPause;
        _playPause.NextKeyView = stop;
        stop.NextKeyView = next;
        open.NextKeyView = library;
        library.NextKeyView = queue;
        next.NextKeyView = fullScreen;
        fullScreen.NextKeyView = open;
        queue.NextKeyView = _fullScreenTracks;
        _fullScreenTracks.NextKeyView = _fullScreenEffects;
        _fullScreenEffects.NextKeyView = repeat;
        repeat.NextKeyView = shuffle;
        shuffle.NextKeyView = mute;
        mute.NextKeyView = _fullScreenLeave;
        _fullScreenLeave.NextKeyView = _volume;
        _volume.NextKeyView = _seek;
        _seek.NextKeyView = previous;
        SetMainKeyViews(previous, _playPause, stop, next, fullScreen, open, library, queue, _fullScreenTracks,
            _fullScreenEffects, repeat, shuffle, mute, _fullScreenLeave, _volume, _seek);
        ((NSView)_seek).AccessibilityLabel = "Playback position";
        ((NSView)_volume).AccessibilityLabel = "Playback volume";
        ((NSView)_playPause).AccessibilityLabel = "Play or pause playback";

        var timelineLine = NSStackView.FromViews([_seek, _time]);
        timelineLine.Orientation = NSUserInterfaceLayoutOrientation.Horizontal;
        timelineLine.Spacing = 12;
        var timeline = NSStackView.FromViews([BuildSeekPreview(), timelineLine]);
        timeline.Orientation = NSUserInterfaceLayoutOrientation.Vertical;
        timeline.Spacing = 4;
        _resumeNotice = NSTextField.CreateLabel("Continue where you stopped?");
        _resumeNotice.TextColor = NSColor.SecondaryLabel;
        var acceptResume = NSButton.CreateButton("Resume", () => _player.AcceptResume());
        _resumeAccept = acceptResume;
        var restartResume = NSButton.CreateButton("Start over", () =>
        {
            _player.DismissPendingResume();
            _player.StartOver();
        });
        _resumeStartOver = restartResume;
        _resumeBanner = NSStackView.FromViews([_resumeNotice, acceptResume, restartResume]);
        _resumeBanner.Orientation = NSUserInterfaceLayoutOrientation.Horizontal;
        _resumeBanner.Spacing = 8;
        _resumeBanner.Hidden = true;
        _statusChips.TextColor = NSColor.SecondaryLabel;
        _statusChips.Font = NSFont.SystemFontOfSize(11, NSFontWeight.Medium)!;
        var stack = NSStackView.FromViews([_title, _artist, _statusChips, _message, _resumeBanner, timeline, controls]);
        _chrome = stack;
        stack.Orientation = NSUserInterfaceLayoutOrientation.Vertical;
        stack.Alignment = NSLayoutAttribute.Leading;
        stack.Spacing = 12;
        stack.EdgeInsets = new NSEdgeInsets(20, 20, 20, 20);
        stack.WantsLayer = true;
        stack.Layer!.BackgroundColor = NSColor.WindowBackground.ColorWithAlphaComponent((System.Runtime.InteropServices.NFloat)0.88).CGColor;
        AddCrashRecoveryBanner(stack);
        AddConfirmationBanner(stack);
        stack.TranslatesAutoresizingMaskIntoConstraints = false;
        controls.WidthAnchor.ConstraintEqualTo(stack.WidthAnchor, 1, -40).Active = true;
        timeline.WidthAnchor.ConstraintEqualTo(stack.WidthAnchor, 1, -40).Active = true;

        var content = new DropView((paths, enqueue) => _player.Drop(paths, enqueue), HandlePictureWheel,
            HandlePictureDrag, HandlePictureClick, HandlePictureOtherClick, RevealFullScreenController);
        _pictureMenu = BuildPictureMenu();
        content.Menu = _pictureMenu;
        _picture = new PictureView(_log) { TranslatesAutoresizingMaskIntoConstraints = false };
        content.AddSubview(_picture);
        content.AddSubview(stack);
        var welcomeTitle = NSTextField.CreateLabel("rexplayer");
        welcomeTitle.TextColor = NSColor.White;
        welcomeTitle.Font = NSFont.SystemFontOfSize(44, NSFontWeight.Semibold)!;
        var welcomeHint = NSTextField.CreateLabel("Your music. Your films. Your space.");
        welcomeHint.TextColor = NSColor.LightGray;
        var welcomeOpen = NSButton.CreateButton("Open file", () => Run(CommandCatalog.OpenFile));
        var welcomeFolder = NSButton.CreateButton("Open folder", () => Run(CommandCatalog.OpenFolder));
        var welcomeLibrary = NSButton.CreateButton("Browse library", ShowLibrary);
        var welcomeQueue = NSButton.CreateButton("Up Next", ShowQueue);
        var welcomeActions = NSStackView.FromViews([welcomeOpen, welcomeFolder, welcomeLibrary, welcomeQueue]);
        welcomeActions.Orientation = NSUserInterfaceLayoutOrientation.Horizontal;
        welcomeActions.Spacing = 12;
        _welcome = NSStackView.FromViews([welcomeTitle, welcomeHint, welcomeActions]);
        _welcome.Orientation = NSUserInterfaceLayoutOrientation.Vertical;
        _welcome.Alignment = NSLayoutAttribute.CenterX;
        _welcome.Spacing = 18;
        _welcome.TranslatesAutoresizingMaskIntoConstraints = false;
        content.AddSubview(_welcome);
        AddSubtitleOverlays(content);
        AddStatsOverlay(content);
        AddMusicPresentation(content);
        AddMacKineticLayer(content);
        _welcome.CenterXAnchor.ConstraintEqualTo(_picture.CenterXAnchor).Active = true;
        _welcome.CenterYAnchor.ConstraintEqualTo(_picture.CenterYAnchor).Active = true;
        _picture.TopAnchor.ConstraintEqualTo(content.TopAnchor).Active = true;
        _picture.LeadingAnchor.ConstraintEqualTo(content.LeadingAnchor).Active = true;
        _picture.TrailingAnchor.ConstraintEqualTo(content.TrailingAnchor).Active = true;
        _picture.BottomAnchor.ConstraintEqualTo(stack.TopAnchor).Active = true;
        stack.LeadingAnchor.ConstraintEqualTo(content.LeadingAnchor).Active = true;
        stack.TrailingAnchor.ConstraintEqualTo(content.TrailingAnchor).Active = true;
        stack.BottomAnchor.ConstraintEqualTo(content.BottomAnchor).Active = true;
        return content;
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
        CommandCatalog.ToggleLibrary => _libraryWindow?.IsVisible == true,
        CommandCatalog.ToggleShuffle => _player.Playlist.Shuffle,
        CommandCatalog.CycleRepeat => _player.Playlist.Repeat != RepeatMode.Off,
        CommandCatalog.Mute => _player.Muted,
        CommandCatalog.StopAfterCurrent => _player.AfterCurrent == AfterItem.Stop,
        CommandCatalog.PauseAfterCurrent => _player.AfterCurrent == AfterItem.Pause,
        CommandCatalog.SleepAtEndOfItem => _player.SleepAtEndOfItem,
        CommandCatalog.ToggleFullScreen => IsFullScreen,
        CommandCatalog.ToggleAlwaysOnTop => _window.Level == NSWindowLevel.Floating,
        CommandCatalog.ToggleStats => _statsVisible,
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
        _title.StringValue = _player.Item is null ? "Drop media here, or press Cmd+O to open a file." : _player.Title;
        _artist.StringValue = _player.Item is null ? "Private, local playback" : _player.Artist ?? _player.State.ToString();
        _window.Title = _player.Item is null ? "rexplayer" : _player.Title + " - rexplayer";
        ShowTitleOnPlaybackStart();
        _playPause.Title = _player.IsPlaying ? "Pause" : "Play";
        _seek.Enabled = _player.CanSeek;
        if (_resumeBanner is not null && _resumeNotice is not null)
        {
            _resumeBanner.Hidden = _player.PendingResumeAt is not { } at || !_player.CanSeek;
            if (_player.PendingResumeAt is { } offered)
            {
                _resumeNotice.StringValue = "Resume from " + TimeText.Format(offered, _player.Duration) + "?";
            }
        }

        _volume.DoubleValue = _player.Volume;
        if (_mainMute is not null) _mainMute.Title = _player.Muted ? "Unmute" : "Mute";
        if (_mainShuffle is not null) _mainShuffle.Title = _player.Playlist.Shuffle ? "Shuffle: On" : "Shuffle";
        if (_mainRepeat is not null) _mainRepeat.Title = "Repeat: " + _player.Playlist.Repeat;
        if (_mainFullScreen is not null) _mainFullScreen.Title = IsFullScreen ? "Exit full screen" : "Full screen";
        _statusChips.StringValue = string.Join("  •  ", new[]
        {
            _settings.HardwareVideoDecoding ? "Hardware decoding" : null,
            Math.Abs(_player.Speed - 1) > 0.001 ? $"{_player.Speed:0.##}× speed" : null,
        }.Where(chip => chip is not null));
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
                _player.Memory.Left(item.Location, TimeSpan.FromSeconds(15), TimeSpan.Zero);
                _player.Open([item.Location]);
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
                _player.Memory.Left(item.Location, TimeSpan.FromSeconds(15), TimeSpan.Zero);
                _player.Open([item.Location]);
                break;
            case "never":
                _settings = _settings with { ResumeMode = ResumeBehavior.Never };
                _player.Settings = _settings;
                _player.Memory.Left(item.Location, TimeSpan.FromSeconds(15), TimeSpan.Zero);
                _player.Open([item.Location]);
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
        _time.Title = duration > TimeSpan.Zero
            ? _remainingTime ? "−" + Clock(duration - _player.Position) : $"{Clock(_player.Position)} / {Clock(duration)}"
            : Clock(_player.Position);
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
            && _seek.Buffered >= 0 && _seek.Chapters is not null && _mainKeyViews.Count == 16;
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

    private static string Clock(TimeSpan time) => time.TotalHours >= 1
        ? time.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture)
        : time.ToString(@"m\:ss", CultureInfo.InvariantCulture);

    private static readonly TimeSpan MessageAtLeast = TimeSpan.FromSeconds(3);

    private void Say(string text)
    {
        _message.StringValue = text;
        var showOverPicture = OnScreenMessage.Shows(text, _settings.OnScreenMessages);
        SetVideoOSD(showOverPicture ? text : "");
        _clearMessage?.Invalidate();
        // The line doubles as the window's status, so it keeps at least three seconds; a long
        // message or a problem stays as long as it takes to read.
        var readFor = OnScreenMessage.Duration(text);
        _clearMessage = NSTimer.CreateScheduledTimer(readFor > MessageAtLeast ? readFor : MessageAtLeast, _ =>
        {
            _message.StringValue = "";
            SetVideoOSD("");
        });
    }

}
