namespace Rex.Media.AppCore.Commands;

/// <summary>
/// The picture's right-click menu (UI-03): the same commands as the menu bar, reachable even in
/// full screen and the minimal interface. Each group is a title (empty for entries at the top
/// level) and its command ids, null for a separator.
/// </summary>
public static class ContextMenuLayout
{
    /// <summary>The menu's groups, in order.</summary>
    public static IReadOnlyList<(string Title, string?[] Commands)> Groups { get; } =
    [
        ("", [CommandCatalog.PlayPause, CommandCatalog.Stop, CommandCatalog.Previous, CommandCatalog.Next, null, CommandCatalog.ToggleFullScreen, CommandCatalog.MinimalInterface]),
        ("Audio", [CommandCatalog.CycleAudioTrack, CommandCatalog.VolumeUp, CommandCatalog.VolumeDown, CommandCatalog.Mute, CommandCatalog.CycleVisualizer, CommandCatalog.VisualizerSettings]),
        ("Subtitles", [CommandCatalog.AddSubtitles, CommandCatalog.CycleSubtitles, CommandCatalog.ToggleSubtitles, CommandCatalog.CycleSecondarySubtitles, CommandCatalog.ToggleKineticCaptions]),
        ("Video", [CommandCatalog.CycleAspectRatio, CommandCatalog.CustomAspectRatio, CommandCatalog.CycleCrop, CommandCatalog.ZoomIn, CommandCatalog.ZoomOut, CommandCatalog.ResetZoom, CommandCatalog.Snapshot, CommandCatalog.ToggleStats, CommandCatalog.ToggleAlwaysOnTop]),
        ("Media", [CommandCatalog.OpenFile, CommandCatalog.OpenFolder, CommandCatalog.PasteLocation, CommandCatalog.TogglePlaylist, CommandCatalog.MediaInformation]),
        ("", [null, CommandCatalog.Preferences, CommandCatalog.Quit]),
    ];
}
