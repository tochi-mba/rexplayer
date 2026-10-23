namespace Rex.Media.App;

public sealed partial class MainWindow
{
    /// <summary>
    /// Intro and credit analysis always yields to playback. On Balanced and Fastest it also
    /// yields to library browsing, leaving processor and disk time for layout and artwork.
    /// </summary>
    private void UpdateBackgroundSectionPause() =>
        _backgroundSections?.SetPaused(_player.IsPlaying || (LibraryOpen && _budget.PauseBackgroundWorkWhileBrowsing));
}
