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
        // The stage itself is the notice: the reason in a sentence, a way on, and the rest of the
        // reason behind Details. It is said once, so no passing message repeats it over the stage.
        Wait.For(() => app.Status().State == "Faulted"
            && app.Find("Stage").Current.Name == "PLAYBACK ERROR playback stage"
            && app.IsShown("ErrorOpenFile")
            && app.Text("IdleHint").Contains("does not recognise", StringComparison.Ordinal),
            "the actual damaged-file failure notice to appear");
        Assert.False(app.IsShown("Osd"), "The stage already says why the file could not be played.");
        Assert.False(app.IsShown("LyricsStatus"), "A failed file has no lyrics to talk about.");
        Assert.True(app.IsShown("ErrorDetails"), "The bytes the file starts with are kept for whoever asks.");
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
