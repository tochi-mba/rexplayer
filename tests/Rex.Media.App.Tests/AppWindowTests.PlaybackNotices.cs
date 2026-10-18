using System.IO;
using Rex.Media.Settings;
using Rex.Media.TestKit;

namespace Rex.Media.App.Tests;

public sealed partial class AppWindowTests
{
    [Fact]
    public void AFailedFilesNoticeDoesNotRemainOnTheNextQueuedItem()
    {
        var broken = Path.Combine(_media, "broken.wav");
        File.WriteAllBytes(broken, [1, 2, 3, 4]);
        var song = Song(30);
        var root = Path.Combine(_media, "player");
        Directory.CreateDirectory(root);
        SettingsStore.Save(Path.Combine(root, "settings.json"), new PlayerSettings
        {
            FirstRunDone = true,
            LastSeenVersion = AppProcess.Version,
            UpdateChecks = UpdateCadence.Off,
            TitleSeconds = 0,
            Visualizer = VisualizerChoice.Off,
        });

        using var app = AppProcess.Start([broken, song], root: root);
        Wait.For(() => app.Text("NowPlaying") == "Sungba", "the next queued item to open");
        // With opening titles disabled, no replacement title can hide a stale failure notice.
        Assert.False(app.IsShown("Osd"));
        Wait.For(() => app.LogText.Contains("could not be played", StringComparison.Ordinal), "the failed file to remain in the log");
        Assert.Equal(0, app.Close());
    }
}
