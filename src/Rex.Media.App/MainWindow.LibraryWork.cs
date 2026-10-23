namespace Rex.Media.App;

public sealed partial class MainWindow
{
    /// <summary>
    /// Intro and credit analysis decodes several files. It yields not only to playback but also to
    /// active library browsing, where WinUI needs the same processor and disk for layout and art.
    /// </summary>
    private void UpdateBackgroundSectionPause() =>
        _backgroundSections?.SetPaused(_player.IsPlaying || LibraryOpen);
}
