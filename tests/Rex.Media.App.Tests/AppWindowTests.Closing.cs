using System.IO;
using Rex.Media.TestKit;

namespace Rex.Media.App.Tests;

public sealed partial class AppWindowTests
{
    /// <summary>
    /// Closing in the middle of playing ends cleanly, every time. Each open puts a notice on screen
    /// whose clock used to fire after the window had gone and end the process with 0xC000027B, so
    /// each round closes inside that notice's second and a half.
    /// </summary>
    [Fact]
    public void ClosingWhilePlayingSoundVideoOrAVisualisationExitsCleanly()
    {
        var film = Path.Combine(_media, "Film.mp4");
        File.Copy(RepoPaths.Combine("tests", "fixtures", "mp4", "h264-aac.mp4"), film);
        var song = Song(30);
        var music = Music(30);

        for (var round = 0; round < 3; round++)
        {
            foreach (var (file, root) in new[] { (song, (string?)null), (film, null), (music, RootShowing("Halo")) })
            {
                using var app = AppProcess.Start([file], root);
                Wait.For(() => app.Find("PlayPauseButton").Current.Name == "Pause", "playing to begin: " + Path.GetFileName(file));
                if (root is not null)
                {
                    Wait.For(() => app.IsShown("Visualizer"), "the visualisation");
                }

                Assert.Equal(0, app.Close());
            }
        }
    }
}
