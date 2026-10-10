using System.Globalization;
using System.IO;
using Rex.Media.Settings;
using Rex.Media.TestKit;

namespace Rex.Media.App.Tests;

/// <summary>
/// The real window, driven the way a person would: open media, watch it play, pause, stop, change
/// the volume and the panes, and find everything as they left it next time. One window at a time.
/// </summary>
[Collection("desktop")]
public sealed class AppEndToEndTests : IDisposable
{
    private readonly string _media = Path.Combine(Path.GetTempPath(), "rexplayer-ui-media-" + Guid.NewGuid().ToString("N"));

    public AppEndToEndTests() => Directory.CreateDirectory(_media);

    public void Dispose() => Directory.Delete(_media, recursive: true);

    /// <summary>A quiet mono WAV of <paramref name="seconds"/> seconds, named <paramref name="name"/>.</summary>
    private string Song(string name, int seconds)
    {
        var path = Path.Combine(_media, name + ".wav");
        File.WriteAllBytes(path, WavBuilder.Pcm(8000, 1, 16, Pcm.Int16(new float[8000 * seconds])).Build());
        return path;
    }

    private static double Seconds(string text) =>
        text.Split(':').Aggregate(0.0, (total, part) => (total * 60) + double.Parse(part.TrimStart('-'), CultureInfo.InvariantCulture));

    [Fact]
    [Capability("UI-01")]
    public void AFileNamedOnTheCommandLinePlaysAndTheControlsFollowIt()
    {
        using var app = AppProcess.Start([Song("Lonely At The Top", 6)]);

        Wait.For(() => app.Title == "Lonely At The Top \u2014 rexplayer", "the title to name the song");
        Assert.Equal("Lonely At The Top", app.Text("NowPlaying"));
        Wait.For(() => Seconds(app.Text("Elapsed")) >= 1, "the position to move");
        Assert.Equal("Pause", app.Find("PlayPauseButton").Current.Name);

        app.Press("PlayPauseButton");
        Wait.For(() => app.Find("PlayPauseButton").Current.Name == "Play", "the button to offer play again");
        var paused = app.Text("Elapsed");
        Thread.Sleep(1200);
        Assert.Equal(paused, app.Text("Elapsed"));

        app.Press("StopButton");
        Wait.For(() => app.Text("Elapsed") == "0:00", "stop to return to the start");
        Assert.Equal(0, app.Close());
    }

    [Fact]
    [Capability("PB-12")]
    [Capability("LIB-01")]
    public void APlaylistPlaysThroughAndThePaneShowsWhatIsPlaying()
    {
        using var app = AppProcess.Start([Song("Terminator", 1), Song("Sungba", 1)]);

        app.Toggle("PlaylistButton");
        Wait.For(() => app.IsShown("PlaylistView"), "the playlist pane");
        Wait.For(() => app.Text("NowPlaying") == "Sungba", "the second song to start");
        Assert.Equal(0, app.Close());
    }

    [Fact]
    [Capability("AU-05")]
    public void TheVolumeAndThePanesAreThereAgainNextTime()
    {
        string root;
        using (var first = AppProcess.Start())
        {
            root = first.Root;
            first.SetValue("VolumeBar", 40);
            first.Toggle("PlaylistButton");
            first.Toggle("ShuffleButton");
            Wait.For(() => first.IsShown("PlaylistView"), "the playlist pane");
            Assert.Equal(0, first.Close());
        }

        var saved = SettingsStore.Load(Path.Combine(root, "settings.json"));
        Assert.Equal((0.4, true, true), (saved.Volume, saved.PlaylistVisible, saved.Shuffle));

        using var second = AppProcess.Start(root: root);
        Assert.Equal(40, ((System.Windows.Automation.RangeValuePattern)second.Find("VolumeBar").GetCurrentPattern(System.Windows.Automation.RangeValuePattern.Pattern)).Current.Value);
        Assert.True(second.IsShown("PlaylistView"));
        Assert.Equal(0, second.Close());
    }

    [Fact]
    [Capability("UI-14")]
    public void TheFirstStartWelcomesAndAsksAboutLookingForUpdates()
    {
        using var app = AppProcess.Start(firstRun: true);

        app.Toggle("WelcomeUpdates");
        app.Press("CloseButton");
        Wait.For(() => File.Exists(app.SettingsPath) && app.SavedSettings.FirstRunDone, "the welcome to be remembered");

        var saved = app.SavedSettings;
        Assert.Equal((UpdateCadence.Off, AppProcess.Version), (saved.UpdateChecks, saved.LastSeenVersion));
        Assert.Equal(0, app.Close());
    }

    [Fact]
    [Capability("TOOL-06")]
    public void ASecondLaunchHandsItsFileToTheRunningPlayer()
    {
        using var app = AppProcess.Start([Song("Lonely At The Top", 30)]);
        Wait.For(() => app.Text("NowPlaying") == "Lonely At The Top", "the first song");

        Assert.Equal(0, AppProcess.Launch([Song("Terminator", 30)], app.Root));

        Wait.For(() => app.Text("NowPlaying") == "Terminator", "the handed-over song to play");
        Assert.Equal(0, app.Close());
    }

    [Fact]
    public void MediaThatWillNotPlaySaysWhyAndLeavesTheWindowWorking()
    {
        var broken = Path.Combine(_media, "broken.wav");
        File.WriteAllBytes(broken, [1, 2, 3, 4, 5, 6, 7, 8]);
        using var app = AppProcess.Start([broken]);

        Wait.For(() => app.Text("IdleHint").Length > 0 && app.Text("IdleTitle") == "broken", "the reason to show");
        // The error banner updates on the UI thread, but logging flushes asynchronously.
        Wait.For(() => app.LogText.Contains("broken could not be played", StringComparison.Ordinal), "the media failure to be logged");
        Assert.Equal(0, app.Close());
    }
}
