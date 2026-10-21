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
using Rex.Media.Settings;

namespace Rex.Media.App.Mac;

/// <summary>
/// The player window on a Mac. It shows what the <see cref="PlayerController"/> is doing and turns
/// clicks, keys and menus into its commands, exactly as the Windows window does: the menus are
/// <see cref="MenuBarLayout"/>, the shortcuts the shared <see cref="Keymap"/>, the decisions
/// AppCore's. This class only wires them to AppKit. Pictures arrive with the Metal presenter;
/// until then video files play their sound.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000", Justification = "AppKit owns the views once they are in the window's tree.")]
public sealed class MainWindow : IDisposable
{
    private const string LogSource = "window";
    private const string HelpUrl = "https://tochi-mba.github.io/rexplayer/";

    private readonly DataRoots _roots;
    private readonly RexLog _log;
    private readonly NSWindow _window;
    private readonly PlayerController _player;
    private readonly Keymap _keymap;
    private readonly NSTextField _title = NSTextField.CreateLabel("Drop media here, or press Cmd+O to open a file.");
    private readonly NSTextField _time = NSTextField.CreateLabel("0:00");
    private readonly NSTextField _message = NSTextField.CreateLabel("");
    private readonly NSButton _playPause;
    private readonly NSSlider _seek;
    private readonly NSSlider _volume;
    private readonly List<(NSMenuItem Item, string Command)> _checks = [];
    private PlayerSettings _settings;
    private NSTimer? _clearMessage;
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

        _window = new NSWindow(new CGRect(0, 0, 960, 560), NSWindowStyle.Titled | NSWindowStyle.Closable | NSWindowStyle.Miniaturizable | NSWindowStyle.Resizable, NSBackingStore.Buffered, false)
        {
            Title = "rexplayer",
            MinSize = new CGSize(360, 150),
        };
        _window.ReleaseWhenClosed(false);
        _playPause = NSButton.CreateButton("Play", () => Run(CommandCatalog.PlayPause));
        _seek = NSSlider.FromValue(0, 0, 1, () => SeekToSlider());
        _volume = NSSlider.FromValue(_player.Volume, 0, _settings.MaxVolumePercent / 100.0, () => _player.SetVolume(_volume!.DoubleValue));
        _window.ContentView = Layout();
        NSApplication.SharedApplication.MainMenu = BuildMenus();
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

        _player.Dispose();
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
                case "status":
                    break;
                default:
                    answered.SetResult(AutomationReply.Failed($"\"{request.Command}\" is not a request on this Mac yet; try open, run or status."));
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
        _message.TextColor = NSColor.SecondaryLabel;
        _seek.Continuous = false;
        _time.Font = NSFont.MonospacedDigitSystemFontOfSize(13, NSFontWeight.Regular)!;
        var previous = NSButton.CreateButton("Previous", () => Run(CommandCatalog.Previous));
        var stop = NSButton.CreateButton("Stop", () => Run(CommandCatalog.Stop));
        var next = NSButton.CreateButton("Next", () => Run(CommandCatalog.Next));
        var controls = NSStackView.FromViews([previous, _playPause, stop, next, _seek, _time, _volume]);
        controls.Orientation = NSUserInterfaceLayoutOrientation.Horizontal;
        controls.Spacing = 8;
        _volume.WidthAnchor.ConstraintEqualTo(100).Active = true;
        _seek.SetContentHuggingPriorityForOrientation(1, NSLayoutConstraintOrientation.Horizontal);

        var stack = NSStackView.FromViews([_title, _message, controls]);
        stack.Orientation = NSUserInterfaceLayoutOrientation.Vertical;
        stack.Alignment = NSLayoutAttribute.Leading;
        stack.Spacing = 10;
        stack.EdgeInsets = new NSEdgeInsets(20, 20, 20, 20);
        stack.TranslatesAutoresizingMaskIntoConstraints = false;
        controls.WidthAnchor.ConstraintEqualTo(stack.WidthAnchor, 1, -40).Active = true;

        var content = new DropView(paths => _player.Open(paths));
        content.AddSubview(stack);
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
            case CommandCatalog.Quit:
                NSApplication.SharedApplication.Terminate(null);
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
            case CommandCatalog.Help:
                NSWorkspace.SharedWorkspace.OpenUrl(new NSUrl(HelpUrl));
                return true;
            case CommandCatalog.ShowLog or CommandCatalog.OpenLogFolder:
                Directory.CreateDirectory(_roots.LogFolder);
                NSWorkspace.SharedWorkspace.OpenUrl(NSUrl.FromFilename(_roots.LogFolder));
                return true;
            case CommandCatalog.About:
                NSApplication.SharedApplication.OrderFrontStandardAboutPanel(null);
                return true;
            default:
                return false;
        }
    }

    private bool IsFullScreen => (_window.StyleMask & NSWindowStyle.FullScreenWindow) != 0;

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

    /// <summary>Whether what an on-and-off command switches is on now, for its tick.</summary>
    private bool IsOn(string command) => command switch
    {
        CommandCatalog.ToggleShuffle => _player.Playlist.Shuffle,
        CommandCatalog.CycleRepeat => _player.Playlist.Repeat != RepeatMode.Off,
        CommandCatalog.Mute => _player.Muted,
        CommandCatalog.StopAfterCurrent => _player.AfterCurrent == AfterItem.Stop,
        CommandCatalog.PauseAfterCurrent => _player.AfterCurrent == AfterItem.Pause,
        CommandCatalog.SleepAtEndOfItem => _player.SleepAtEndOfItem,
        CommandCatalog.ToggleFullScreen => IsFullScreen,
        CommandCatalog.ToggleAlwaysOnTop => _window.Level == NSWindowLevel.Floating,
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
        _title.StringValue = _player.Item is null ? "Drop media here, or press Cmd+O to open a file." : _player.Title;
        _window.Title = _player.Item is null ? "rexplayer" : _player.Title + " - rexplayer";
        _playPause.Title = _player.IsPlaying ? "Pause" : "Play";
        _seek.Enabled = _player.CanSeek;
        _volume.DoubleValue = _player.Volume;
        ShowPosition();
        ShowChecks();
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
        _clearMessage?.Invalidate();
        _clearMessage = NSTimer.CreateScheduledTimer(TimeSpan.FromSeconds(3), _ => _message.StringValue = "");
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
