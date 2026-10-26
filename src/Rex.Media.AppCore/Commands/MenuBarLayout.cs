namespace Rex.Media.AppCore.Commands;

/// <summary>
/// The menu bar (UI-02) as data, so every shell shows the same menus in the same order with the
/// same groups. Each menu is a title and its entries: a command id, a <see cref="SubMenuPrefix"/>
/// id for a group of related commands one level in, or null for a separator. Groups keep each menu
/// short enough to read at a glance: the commands people reach for stay on top, their finer
/// variants one level in. A shell adds its own dynamic items (picture looks, bookmarks, named
/// playlists) after these.
/// </summary>
public static class MenuBarLayout
{
    /// <summary>Marks a menu entry that opens one of <see cref="SubMenus"/>.</summary>
    public const string SubMenuPrefix = "submenu:";

    /// <summary>The menus, in order, each with its entries.</summary>
    public static IReadOnlyList<(string Title, string?[] Commands)> Menus { get; } =
    [
        ("Media", [CommandCatalog.OpenFile, CommandCatalog.OpenFolder, CommandCatalog.OpenLocation, CommandCatalog.PasteLocation, null, CommandCatalog.OpenPlaylist, CommandCatalog.SavePlaylist, null, CommandCatalog.Quit]),
        ("Playback", [CommandCatalog.PlayPause, CommandCatalog.Stop, CommandCatalog.Previous, CommandCatalog.Next, null,
            CommandCatalog.SkipSection, CommandCatalog.NextEpisode, null,
            SubMenuPrefix + "jump", CommandCatalog.GoToTime, SubMenuPrefix + "speed", null,
            CommandCatalog.CycleRepeat, CommandCatalog.ToggleShuffle, CommandCatalog.ShowPosition, null,
            CommandCatalog.AddBookmark, CommandCatalog.Resume, null,
            SubMenuPrefix + "sleep"]),
        ("Audio", [CommandCatalog.CycleAudioTrack, null, CommandCatalog.VolumeUp, CommandCatalog.VolumeDown, CommandCatalog.Mute, SubMenuPrefix + "audio-timing", null,
            CommandCatalog.Effects, CommandCatalog.CycleVisualizer, CommandCatalog.VisualizerSettings, CommandCatalog.ToggleLyrics]),
        ("Subtitles", [CommandCatalog.AddSubtitles, null, CommandCatalog.CycleSubtitles, CommandCatalog.ToggleSubtitles, CommandCatalog.CycleSecondarySubtitles, null,
            SubMenuPrefix + "subtitle-timing", SubMenuPrefix + "subtitle-size", null,
            CommandCatalog.ToggleKineticCaptions, SubMenuPrefix + "kinetic-captions", null, CommandCatalog.Transcribe, CommandCatalog.SaveTranscript]),
        ("Video", [CommandCatalog.ToggleFullScreen, null, CommandCatalog.CycleAspectRatio, CommandCatalog.CustomAspectRatio, CommandCatalog.CycleCrop, null,
            CommandCatalog.ZoomIn, CommandCatalog.ZoomOut, CommandCatalog.ResetZoom, CommandCatalog.ToggleNavigator, null, SubMenuPrefix + "window-size", null,
            CommandCatalog.Snapshot, CommandCatalog.ToggleStats, CommandCatalog.ToggleAlwaysOnTop]),
        ("View", [CommandCatalog.TogglePlaylist, CommandCatalog.ToggleLibrary, CommandCatalog.MinimalInterface, null, CommandCatalog.MediaInformation, null,
            CommandCatalog.Preferences, CommandCatalog.KeyboardAndMouse, null,
            SubMenuPrefix + "library-backup", CommandCatalog.ClearPlaylist, CommandCatalog.ClearHistory]),
        ("Help", [CommandCatalog.ShortcutSheet, CommandCatalog.Help, null, CommandCatalog.ShowLog, CommandCatalog.OpenLogFolder, CommandCatalog.SaveDiagnostics, null, CommandCatalog.CheckForUpdates, CommandCatalog.WhatsNew, null, CommandCatalog.About]),
    ];

    /// <summary>The groups the menus open one level in, by their <see cref="SubMenuPrefix"/> id: a title and its entries.</summary>
    public static IReadOnlyDictionary<string, (string Title, string?[] Commands)> SubMenus { get; } = new Dictionary<string, (string Title, string?[] Commands)>(StringComparer.Ordinal)
    {
        [SubMenuPrefix + "jump"] = ("Jump", [CommandCatalog.JumpForwardVeryShort, CommandCatalog.JumpBackVeryShort, null,
            CommandCatalog.JumpForwardShort, CommandCatalog.JumpBackShort, null,
            CommandCatalog.JumpForwardMedium, CommandCatalog.JumpBackMedium, null,
            CommandCatalog.JumpForwardLong, CommandCatalog.JumpBackLong]),
        [SubMenuPrefix + "speed"] = ("Speed", [CommandCatalog.Faster, CommandCatalog.Slower, CommandCatalog.SlightlyFaster, CommandCatalog.SlightlySlower, null, CommandCatalog.NormalSpeed]),
        [SubMenuPrefix + "sleep"] = ("Sleep timer", [CommandCatalog.StopAfterCurrent, CommandCatalog.PauseAfterCurrent, null,
            CommandCatalog.SleepPrefix + "15", CommandCatalog.SleepPrefix + "30", CommandCatalog.SleepPrefix + "60", CommandCatalog.SleepAtEndOfItem, null, CommandCatalog.SleepOff]),
        [SubMenuPrefix + "audio-timing"] = ("Sound timing", [CommandCatalog.AudioEarlier, CommandCatalog.AudioLater, CommandCatalog.ResetAudioDelay]),
        [SubMenuPrefix + "subtitle-timing"] = ("Subtitle timing", [CommandCatalog.SubtitlesEarlier, CommandCatalog.SubtitlesLater, CommandCatalog.ResetSubtitleDelay]),
        [SubMenuPrefix + "subtitle-size"] = ("Subtitle size", [CommandCatalog.SubtitlesBigger, CommandCatalog.SubtitlesSmaller, CommandCatalog.ResetSubtitleSize]),
        [SubMenuPrefix + "kinetic-captions"] = ("Kinetic caption style", [CommandCatalog.CycleCaptionStyle, CommandCatalog.CycleCaptionWords, CommandCatalog.CycleCaptionLayout, null,
            CommandCatalog.KineticCaptionsLivelier, CommandCatalog.KineticCaptionsCalmer, null, CommandCatalog.KineticCaptionSettings]),
        [SubMenuPrefix + "window-size"] = ("Window size", [CommandCatalog.ScaleQuarter, CommandCatalog.ScaleHalf, CommandCatalog.ScaleOriginal, CommandCatalog.ScaleDouble]),
        [SubMenuPrefix + "library-backup"] = ("Library backup", [CommandCatalog.BackUpLibrary, CommandCatalog.RestoreLibrary]),
    };

    /// <summary>Whether an entry opens a sub-menu rather than running a command.</summary>
    public static bool IsSubMenu(string entry) => entry.StartsWith(SubMenuPrefix, StringComparison.Ordinal);

    /// <summary>The short id of a sub-menu entry ("jump" for "submenu:jump"), for automation ids.</summary>
    public static string SubMenuId(string entry) => entry[SubMenuPrefix.Length..];

    /// <summary>
    /// Whether a command switches something on and off (rather than doing a thing once), so its
    /// menu item carries a tick that follows the player.
    /// </summary>
    public static bool IsOnOff(string command) => command is CommandCatalog.ToggleShuffle or CommandCatalog.CycleRepeat or CommandCatalog.Mute
        or CommandCatalog.ToggleSubtitles or CommandCatalog.ToggleNavigator or CommandCatalog.StopAfterCurrent or CommandCatalog.PauseAfterCurrent
        or CommandCatalog.SleepAtEndOfItem or CommandCatalog.ToggleFullScreen or CommandCatalog.ToggleAlwaysOnTop or CommandCatalog.ToggleStats
        or CommandCatalog.TogglePlaylist or CommandCatalog.ToggleLyrics or CommandCatalog.ToggleLibrary or CommandCatalog.MinimalInterface;

    /// <summary>Every command id the bar offers, menus first then their sub-menus, in order and without separators.</summary>
    public static IEnumerable<string> AllCommands() =>
        Menus.SelectMany(menu => menu.Commands)
            .Concat(SubMenus.Values.SelectMany(group => group.Commands))
            .OfType<string>()
            .Where(entry => !IsSubMenu(entry));
}
