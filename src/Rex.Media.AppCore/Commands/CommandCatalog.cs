using System.Globalization;

namespace Rex.Media.AppCore.Commands;

/// <summary>One thing a user can do, with the shortcuts it has until the user changes them.</summary>
public sealed record Command(string Id, string Title, string Group, IReadOnlyList<KeyChord> DefaultShortcuts);

/// <summary>
/// Every user action, by id (ADR-011). Menus, shortcuts, the automation pipe and extensions all
/// dispatch these same ids, so an action behaves the same however it is asked for. The site's
/// shortcuts page and the shortcut editor are generated from this list.
/// </summary>
public static class CommandCatalog
{
    public const string PlayPause = "play-pause";
    public const string Stop = "stop";
    public const string Next = "next";
    public const string Previous = "previous";
    public const string Quit = "quit";
    public const string ShowPosition = "show-position";
    public const string GoToTime = "go-to-time";
    public const string CycleRepeat = "cycle-repeat";
    public const string ToggleShuffle = "toggle-shuffle";
    public const string JumpForwardVeryShort = "jump-forward-very-short";
    public const string JumpBackVeryShort = "jump-back-very-short";
    public const string JumpForwardShort = "jump-forward-short";
    public const string JumpBackShort = "jump-back-short";
    public const string JumpForwardMedium = "jump-forward-medium";
    public const string JumpBackMedium = "jump-back-medium";
    public const string JumpForwardLong = "jump-forward-long";
    public const string JumpBackLong = "jump-back-long";
    public const string Faster = "faster";
    public const string Slower = "slower";
    public const string NormalSpeed = "normal-speed";
    public const string SlightlyFaster = "slightly-faster";
    public const string SlightlySlower = "slightly-slower";
    public const string AudioEarlier = "audio-earlier";
    public const string AudioLater = "audio-later";
    public const string ResetAudioDelay = "reset-audio-delay";
    public const string VolumeUp = "volume-up";
    public const string VolumeDown = "volume-down";
    public const string Mute = "mute";
    public const string CycleAudioTrack = "cycle-audio-track";
    public const string CycleSubtitles = "cycle-subtitles";
    public const string ToggleSubtitles = "toggle-subtitles";
    public const string SubtitlesEarlier = "subtitles-earlier";
    public const string SubtitlesLater = "subtitles-later";
    public const string ResetSubtitleDelay = "reset-subtitle-delay";
    public const string AddSubtitles = "add-subtitles";
    public const string CycleSecondarySubtitles = "cycle-secondary-subtitles";
    public const string CycleVisualizer = "cycle-visualizer";
    public const string SavePlaylist = "save-playlist";
    public const string OpenLogFolder = "open-log-folder";
    public const string ZoomIn = "zoom-in";
    public const string ZoomOut = "zoom-out";
    public const string ResetZoom = "reset-zoom";
    public const string PanLeft = "pan-left";
    public const string PanRight = "pan-right";
    public const string PanUp = "pan-up";
    public const string PanDown = "pan-down";
    public const string ToggleNavigator = "toggle-navigator";
    public const string AddBookmark = "add-bookmark";
    public const string ClearHistory = "clear-history";
    public const string Resume = "resume";
    public const string SetSlotPrefix = "set-slot-";
    public const string StopAfterCurrent = "stop-after-current";
    public const string PauseAfterCurrent = "pause-after-current";
    public const string SleepOff = "sleep-off";
    public const string SleepAtEndOfItem = "sleep-end-of-item";
    public const string SleepPrefix = "sleep-";
    public const string PlaySlotPrefix = "play-slot-";
    public const string OpenPlaylist = "open-playlist";
    public const string SubtitlesBigger = "subtitles-bigger";
    public const string SubtitlesSmaller = "subtitles-smaller";
    public const string ResetSubtitleSize = "reset-subtitle-size";
    public const string ToggleFullScreen = "toggle-full-screen";
    public const string LeaveFullScreen = "leave-full-screen";
    public const string CycleAspectRatio = "cycle-aspect-ratio";
    public const string CycleCrop = "cycle-crop";
    public const string ScaleQuarter = "scale-quarter";
    public const string ScaleHalf = "scale-half";
    public const string ScaleOriginal = "scale-original";
    public const string ScaleDouble = "scale-double";
    public const string ToggleAlwaysOnTop = "toggle-always-on-top";
    public const string Snapshot = "snapshot";
    public const string ToggleStats = "toggle-stats";
    public const string OpenFile = "open-file";
    public const string OpenFolder = "open-folder";
    public const string OpenLocation = "open-location";
    public const string PasteLocation = "paste-location";
    public const string TogglePlaylist = "toggle-playlist";
    public const string ClearPlaylist = "clear-playlist";
    public const string MinimalInterface = "minimal-interface";
    public const string MediaInformation = "media-information";
    public const string ShowLog = "show-log";
    public const string SaveDiagnostics = "save-diagnostics";
    public const string Preferences = "preferences";
    public const string Effects = "effects";
    public const string Help = "help";
    public const string CheckForUpdates = "check-for-updates";
    public const string ShortcutSheet = "shortcut-sheet";

    private const string Playback = "Playback";
    private const string Audio = "Audio";
    private const string Video = "Video";
    private const string Subtitle = "Subtitles";
    private const string Media = "Media";
    private const string View = "View";
    private const string Tools = "Tools";

    public static IReadOnlyList<Command> All { get; } =
    [
        New(PlayPause, "Play or pause", Playback, "Space", "MediaPlayPause"),
        New(Stop, "Stop", Playback, "S", "MediaStop"),
        New(Next, "Next item", Playback, "N", "MediaNext"),
        New(Previous, "Previous item", Playback, "P", "MediaPrevious"),
        New(ShowPosition, "Show the position", Playback, "T"),
        New(GoToTime, "Go to a time", Playback, "Ctrl+T"),
        New(CycleRepeat, "Repeat: off, all, one", Playback, "L"),
        New(ToggleShuffle, "Shuffle", Playback, "R"),
        New(JumpForwardVeryShort, "Jump forward a little", Playback, "Shift+Right"),
        New(JumpBackVeryShort, "Jump back a little", Playback, "Shift+Left"),
        New(JumpForwardShort, "Jump forward", Playback, "Right"),
        New(JumpBackShort, "Jump back", Playback, "Left"),
        New(JumpForwardMedium, "Jump forward a minute", Playback, "Ctrl+Right"),
        New(JumpBackMedium, "Jump back a minute", Playback, "Ctrl+Left"),
        New(JumpForwardLong, "Jump forward five minutes", Playback, "Ctrl+Alt+Right"),
        New(JumpBackLong, "Jump back five minutes", Playback, "Ctrl+Alt+Left"),
        New(Faster, "Faster", Playback, "Plus", "Shift+Equals"),
        New(Slower, "Slower", Playback, "Minus"),
        New(NormalSpeed, "Normal speed", Playback, "Equals"),
        New(SlightlyFaster, "A little faster", Playback, "RightBracket"),
        New(SlightlySlower, "A little slower", Playback, "LeftBracket"),
        New(AudioEarlier, "Sound 50 ms earlier", Audio, "J"),
        New(AudioLater, "Sound 50 ms later", Audio, "K"),
        New(ResetAudioDelay, "Sound back in step", Audio),
        New(VolumeUp, "Volume up", Audio, "Up", "Ctrl+Up"),
        New(VolumeDown, "Volume down", Audio, "Down", "Ctrl+Down"),
        New(Mute, "Mute", Audio, "M"),
        New(CycleAudioTrack, "Next audio track", Audio, "B"),
        New(CycleVisualizer, "Next visualisation", Audio, "Z"),
        New(CycleSubtitles, "Next subtitle track", Subtitle, "V"),
        New(ToggleSubtitles, "Subtitles on or off", Subtitle, "Shift+V"),
        New(SubtitlesEarlier, "Subtitles 50 ms earlier", Subtitle, "G"),
        New(SubtitlesLater, "Subtitles 50 ms later", Subtitle, "H"),
        New(ResetSubtitleDelay, "Subtitles back in step", Subtitle),
        New(AddSubtitles, "Add a subtitle file", Subtitle),
        New(CycleSecondarySubtitles, "Next second subtitle track", Subtitle, "Alt+V"),
        New(SubtitlesBigger, "Bigger subtitles", Subtitle, "Ctrl+Plus"),
        New(SubtitlesSmaller, "Smaller subtitles", Subtitle, "Ctrl+Minus"),
        New(ResetSubtitleSize, "Subtitles at their normal size", Subtitle, "Ctrl+0"),
        New(ToggleFullScreen, "Full screen", Video, "F", "F11"),
        New(LeaveFullScreen, "Leave full screen", Video, "Escape"),
        New(CycleAspectRatio, "Next aspect ratio", Video, "A"),
        New(CycleCrop, "Next crop", Video, "C"),
        New(ScaleQuarter, "Window at a quarter size", Video, "Alt+1"),
        New(ScaleHalf, "Window at half size", Video, "Alt+2"),
        New(ScaleOriginal, "Window at the video's size", Video, "Alt+3"),
        New(ScaleDouble, "Window at double size", Video, "Alt+4"),
        New(ToggleAlwaysOnTop, "Always on top", Video),
        New(ZoomIn, "Zoom in", Video, "Alt+Plus", "Alt+Equals"),
        New(ZoomOut, "Zoom out", Video, "Alt+Minus"),
        New(ResetZoom, "See the whole picture", Video, "Alt+0"),
        New(PanLeft, "Look further left", Video, "Alt+Left"),
        New(PanRight, "Look further right", Video, "Alt+Right"),
        New(PanUp, "Look further up", Video, "Alt+Up"),
        New(PanDown, "Look further down", Video, "Alt+Down"),
        New(ToggleNavigator, "Navigator while zoomed", Video, "Alt+N"),
        New(Snapshot, "Take a snapshot", Video, "Shift+S"),
        New(ToggleStats, "Statistics over the picture", Video, "Ctrl+Shift+I"),
        New(OpenFile, "Open files", Media, "Ctrl+O"),
        New(OpenFolder, "Open a folder", Media, "Ctrl+F"),
        New(OpenLocation, "Open a location", Media, "Ctrl+N"),
        New(PasteLocation, "Play what is on the clipboard", Media, "Ctrl+V"),
        New(Quit, "Quit", Media, "Ctrl+Q"),
        New(TogglePlaylist, "Playlist", View, "Ctrl+L"),
        New(SavePlaylist, "Save the playlist", View, "Ctrl+Y"),
        New(OpenPlaylist, "Open a playlist", View, "Ctrl+X"),
        New(AddBookmark, "Add a bookmark here", Playback, "Ctrl+B"),
        New(Resume, "Go back to where I left off", Playback),
        New(ClearHistory, "Clear the history", View),
        New(StopAfterCurrent, "Stop after this item", Playback),
        New(PauseAfterCurrent, "Pause after this item", Playback),
        New(SleepOff, "Sleep timer off", Playback),
        New(SleepAtEndOfItem, "Sleep at the end of this item", Playback),
        .. new[] { 15, 30, 45, 60, 90, 120 }.Select(minutes => New(SleepPrefix + minutes.ToString(CultureInfo.InvariantCulture), $"Sleep in {minutes} minutes", Playback)),
        .. Enumerable.Range(1, 9).Select(n => New(SetSlotPrefix + n.ToString(CultureInfo.InvariantCulture), $"Keep this in quick slot {n}", Playback, $"Ctrl+Shift+{n}")),
        .. Enumerable.Range(1, 9).Select(n => New(PlaySlotPrefix + n.ToString(CultureInfo.InvariantCulture), $"Play quick slot {n}", Playback, $"Ctrl+{n}")),
        New(ClearPlaylist, "Clear the playlist", View, "Ctrl+W"),
        New(MinimalInterface, "Minimal interface", View, "Ctrl+H"),
        New(MediaInformation, "Media information", Tools, "Ctrl+I"),
        New(ShowLog, "Log", Tools, "Ctrl+M"),
        New(OpenLogFolder, "Open the log folder", Tools),
        New(SaveDiagnostics, "Save diagnostics for a problem report", Tools),
        New(Preferences, "Preferences", Tools, "Ctrl+P"),
        New(Effects, "Effects and equaliser", Tools, "Ctrl+E"),
        New(Help, "Help", Tools, "F1"),
        New(CheckForUpdates, "Check for updates", Tools),
        New(ShortcutSheet, "Keyboard shortcuts", Tools, "Ctrl+Slash"),
    ];

    /// <summary>The command with this id, or null.</summary>
    public static Command? Find(string id) => All.FirstOrDefault(command => command.Id == id);

    /// <summary>The minutes a sleep timer command is for, or null for any other command.</summary>
    public static int? SleepMinutesOf(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        return id.StartsWith(SleepPrefix, StringComparison.Ordinal) && int.TryParse(id.AsSpan(SleepPrefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var minutes) && minutes > 0 ? minutes : null;
    }

    /// <summary>The quick slot a slot command is for, or null for any other command.</summary>
    public static int? SlotNumber(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        var number = id.StartsWith(SetSlotPrefix, StringComparison.Ordinal) ? id[SetSlotPrefix.Length..]
            : id.StartsWith(PlaySlotPrefix, StringComparison.Ordinal) ? id[PlaySlotPrefix.Length..]
            : null;
        return int.TryParse(number, NumberStyles.None, CultureInfo.InvariantCulture, out var slot) && slot is >= 1 and <= 9 ? slot : null;
    }

    private static Command New(string id, string title, string group, params string[] shortcuts) =>
        new(id, title, group, [.. shortcuts.Select(KeyChord.Parse)]);
}
