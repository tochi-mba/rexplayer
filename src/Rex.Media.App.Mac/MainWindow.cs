using System.Globalization;
using Rex.Media.AppCore;
using Rex.Media.AppCore.Automation;
using Rex.Media.AppCore.Commands;
using Rex.Media.AppCore.Platform;
using Rex.Media.AppCore.Player;
using Rex.Media.Audio;
using Rex.Media.Audio.CoreAudio;
using Rex.Media.Codecs.Apple;
using Rex.Media.Diagnostics;
using Rex.Media.Engine;
using Rex.Media.IO;
using Rex.Media.Library;
using Rex.Media.Primitives;
using Rex.Media.Settings;
using Rex.Media.Video;
using Rex.Media.Video.Metal;

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
    private readonly NSTextField _time = NSTextField.CreateLabel("0:00");
    private readonly NSTextField _message = NSTextField.CreateLabel("");
    private readonly NSButton _playPause;
    private readonly NSSlider _seek;
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
    private bool _moving;

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
        _playPause = NSButton.CreateButton("Play", () => Run(CommandCatalog.PlayPause));
        _seek = NSSlider.FromValue(0, 0, 1, () => SeekToSlider());
        _volume = NSSlider.FromValue(_player.Volume, 0, _settings.MaxVolumePercent / 100.0, () => _player.SetVolume(_volume!.DoubleValue));
        _window.ContentView = Layout();
        InitializeLibrary();
        InitializeQueue();
        NSApplication.SharedApplication.MainMenu = BuildMenus();
        ApplyMacTheme();
        NSTimer.CreateRepeatingScheduledTimer(TimeSpan.FromSeconds(1), _ => _player.SleepTick());
    }

    /// <summary>Shows the window, opening what Finder handed over, or the queue the last run left.</summary>
    public void Show(IReadOnlyList<string> files)
    {
        _window.Center();
        _window.MakeKeyAndOrderFront(null);
        if (files.Count > 0)
        {
            OpenHandedOver(files);
        }
        else
        {
            _player.RestoreQueue();
        }

        ShowState();
        ShowStartupGuidance();
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
        DisposeMusicPresentation();
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
                case "mac-drop":
                    _player.Drop(request.Paths ?? []);
                    answered.SetResult(new AutomationReply { Ok = true });
                    return;
                case "mac-subtitles":
                    answered.SetResult(new AutomationReply
                    {
                        Ok = true,
                        Text = $"tracks={_player.SubtitleTracks.Count};selected={_player.Subtitles?.Name ?? "off"}",
                    });
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
            Decoders = MediaRegistries.Decoders(new AppleDecoderFactory()),
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
        var previous = NSButton.CreateButton("Previous", () => Run(CommandCatalog.Previous));
        var stop = NSButton.CreateButton("Stop", () => Run(CommandCatalog.Stop));
        var next = NSButton.CreateButton("Next", () => Run(CommandCatalog.Next));
        var open = NSButton.CreateButton("Open file", () => Run(CommandCatalog.OpenFile));
        var queue = NSButton.CreateButton("Up Next", ShowQueue);
        var library = NSButton.CreateButton("Library", ShowLibrary);
        library.ToolTip = "Browse your own local library and watched folders";
        queue.ToolTip = "View, reorder, and manage what plays next";
        open.ToolTip = "Open a file (Cmd+O)";
        _playPause.BezelStyle = NSBezelStyle.Rounded;
        _playPause.Font = NSFont.SystemFontOfSize(15, NSFontWeight.Semibold)!;
        _playPause.WidthAnchor.ConstraintEqualTo(76).Active = true;
        _playPause.ToolTip = "Play or pause (Space)";
        _seek.ToolTip = "Seek through the current file";
        _volume.ToolTip = "Volume";
        var controls = NSStackView.FromViews([previous, _playPause, stop, next, open, library, queue, _volume]);
        controls.Orientation = NSUserInterfaceLayoutOrientation.Horizontal;
        controls.Spacing = 8;
        _volume.WidthAnchor.ConstraintEqualTo(100).Active = true;
        _seek.SetContentHuggingPriorityForOrientation(1, NSLayoutConstraintOrientation.Horizontal);

        var timeline = NSStackView.FromViews([_seek, _time]);
        timeline.Orientation = NSUserInterfaceLayoutOrientation.Horizontal;
        timeline.Spacing = 12;
        _resumeNotice = NSTextField.CreateLabel("Continue where you stopped?");
        _resumeNotice.TextColor = NSColor.SecondaryLabel;
        var acceptResume = NSButton.CreateButton("Resume", () => _player.AcceptResume());
        var restartResume = NSButton.CreateButton("Start over", () =>
        {
            _player.DismissPendingResume();
            _player.StartOver();
        });
        _resumeBanner = NSStackView.FromViews([_resumeNotice, acceptResume, restartResume]);
        _resumeBanner.Orientation = NSUserInterfaceLayoutOrientation.Horizontal;
        _resumeBanner.Spacing = 8;
        _resumeBanner.Hidden = true;
        var stack = NSStackView.FromViews([_title, _artist, _message, _resumeBanner, timeline, controls]);
        _chrome = stack;
        stack.Orientation = NSUserInterfaceLayoutOrientation.Vertical;
        stack.Alignment = NSLayoutAttribute.Leading;
        stack.Spacing = 12;
        stack.EdgeInsets = new NSEdgeInsets(20, 20, 20, 20);
        stack.TranslatesAutoresizingMaskIntoConstraints = false;
        controls.WidthAnchor.ConstraintEqualTo(stack.WidthAnchor, 1, -40).Active = true;
        timeline.WidthAnchor.ConstraintEqualTo(stack.WidthAnchor, 1, -40).Active = true;

        var content = new DropView(paths => _player.Drop(paths));
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
        app.AddItem(new NSMenuItem("Tracks & subtitle sync…", (_, _) => ShowTrackTools()));
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
        var value = NSPasteboard.GeneralPasteboard.GetStringForType(NSPasteboard.NSPasteboardTypeString);
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
        ShowPosition();
        UpdateSubtitleOverlays();
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
        _time.StringValue = duration > TimeSpan.Zero ? $"{Clock(_player.Position)} / {Clock(duration)}" : Clock(_player.Position);
        UpdateSubtitleOverlays();
        RefreshMusicPosition();
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

    private void Say(string text)
    {
        _message.StringValue = text;
        SetVideoOSD(text);
        _clearMessage?.Invalidate();
        _clearMessage = NSTimer.CreateScheduledTimer(TimeSpan.FromSeconds(3), _ =>
        {
            _message.StringValue = "";
            SetVideoOSD("");
        });
    }

    /// <summary>
    /// The picture: a view backed by a CAMetalLayer the presenter draws into, its drawable kept at
    /// the view's size in the screen's own pixels.
    /// </summary>
    private sealed class PictureView : NSView
    {
        private readonly CoreAnimation.CAMetalLayer _layer = new();

        public PictureView(RexLog log)
        {
            WantsLayer = true;
            Layer = _layer;
            _layer.BackgroundColor = NSColor.Black.CGColor;
            try
            {
                Presenter = MetalPresenter.ForLayer(_layer);
            }
            catch (InvalidOperationException ex)
            {
                log.Error("window", "Pictures cannot be shown: " + ex.Message);
            }

            if (Presenter is null)
            {
                log.Warning("window", "This Mac has no Metal device; videos play their sound only.");
            }
        }

        public MetalPresenter? Presenter { get; }

        public override void Layout()
        {
            base.Layout();
            var scale = Window?.BackingScaleFactor ?? 1;
            _layer.ContentsScale = scale;
            Presenter?.Resize((int)(Bounds.Width * scale), (int)(Bounds.Height * scale));
            Presenter?.Redraw();
        }
    }

    /// <summary>The window's content: takes files and folders dropped on it.</summary>
    private sealed class DropView : NSView
    {
        private readonly Action<IReadOnlyList<string>> _open;

        public DropView(Action<IReadOnlyList<string>> open)
        {
            _open = open;
            RegisterForDraggedTypes([NSPasteboardType.FileUrl.GetConstant()!]);
        }

        public override NSDragOperation DraggingEntered(INSDraggingInfo sender) => NSDragOperation.Copy;

        public override bool PerformDragOperation(INSDraggingInfo sender)
        {
            var urls = sender.DraggingPasteboard.ReadObjectsForClasses([new ObjCRuntime.Class(typeof(NSUrl))], null)?.OfType<NSUrl>().Select(url => url.Path!).ToList();
            if (urls is not { Count: > 0 })
            {
                return false;
            }

            _open(urls);
            return true;
        }
    }
}
