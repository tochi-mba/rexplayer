using System.Globalization;
using Rex.Media.AppCore;
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
