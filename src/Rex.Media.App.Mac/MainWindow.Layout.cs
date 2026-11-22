using Rex.Media.AppCore.Commands;
using Rex.Media.AppCore.Design;

namespace Rex.Media.App.Mac;

/// <summary>Lays the window out: the stage, the welcome shown before anything is open and the bar of controls across the bottom.</summary>
public sealed partial class MainWindow
{
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
}
