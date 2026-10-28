namespace Rex.Media.AppCore.Commands;

/// <summary>
/// The picture's right-click menu (UI-03): the same commands as the menu bar, reachable even in
/// full screen and the minimal interface. Each group is a title (empty for entries at the top
/// level) and its command ids, null for a separator. Its groups come in the menu bar's order, and
/// it stays short enough to open over the picture without running off a small window.
/// </summary>
public static class ContextMenuLayout
{
    /// <summary>The menu's groups, in order.</summary>
    public static IReadOnlyList<(string Title, string?[] Commands)> Groups { get; } =
    [
        ("", [CommandCatalog.PlayPause, CommandCatalog.Stop, CommandCatalog.Previous, CommandCatalog.Next, null, CommandCatalog.ToggleFullScreen, CommandCatalog.MinimalInterface]),
        ("Media", [CommandCatalog.OpenFile, CommandCatalog.OpenFolder, CommandCatalog.PasteLocation, CommandCatalog.TogglePlaylist, CommandCatalog.MediaInformation]),
        ("Audio", [CommandCatalog.CycleAudioTrack, CommandCatalog.VolumeUp, CommandCatalog.VolumeDown, CommandCatalog.Mute, CommandCatalog.CycleVisualizer, CommandCatalog.VisualizerSettings]),
        ("Subtitles", [CommandCatalog.AddSubtitles, CommandCatalog.CycleSubtitles, CommandCatalog.ToggleSubtitles, CommandCatalog.CycleSecondarySubtitles, CommandCatalog.ToggleKineticCaptions]),
        ("Video", [CommandCatalog.CycleAspectRatio, CommandCatalog.CustomAspectRatio, CommandCatalog.CycleCrop, CommandCatalog.ZoomIn, CommandCatalog.ZoomOut, CommandCatalog.ResetZoom, CommandCatalog.Snapshot, CommandCatalog.ToggleStats, CommandCatalog.ToggleAlwaysOnTop]),
        // Quit is in the Media menu and the window's own close button; it has no place over the picture.
        ("", [null, CommandCatalog.Preferences]),
    ];
}
