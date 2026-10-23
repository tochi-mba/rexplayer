using System.IO;
using System.Windows.Automation;
using Rex.Media.TestKit;
using Rex.Media.Settings;

namespace Rex.Media.App.Tests;

public sealed partial class AppWindowTests
{
    [Fact]
    [Capability("UI-13")]
    public void ACorruptOfflineMediaFileShowsARealFailureNotice()
    {
        var broken = Path.Combine(_media, "evidence-broken.wav");
        File.WriteAllBytes(broken, [1, 2, 3, 4]);
        using var app = AppProcess.Start([broken], extraSettings: "\"theme\": \"Light\"",
            environment: new Dictionary<string, string> { ["REXPLAYER_VISUAL_EVIDENCE"] = "1" });
        var viewport = (TransformPattern)app.Window.GetCurrentPattern(TransformPattern.Pattern);
        viewport.Resize(1000, 700);
        Wait.For(() => Math.Abs(app.Window.Current.BoundingRectangle.Width - 1000) < 2 &&
            Math.Abs(app.Window.Current.BoundingRectangle.Height - 700) < 2,
            "the pinned corrupt-media error viewport");
        Wait.For(() => app.Status().State == "Faulted" && app.IsShown("Osd") &&
            app.Text("Osd").Contains("could not be played", StringComparison.Ordinal),
            "the actual damaged-file failure notice to appear");
        Screenshots.Match(app, "player-corrupt-file-error");
        Assert.Equal(0, app.Close());
    }

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
