using System.IO;
using System.Windows.Automation;
using Rex.Media.AppCore.Commands;
using Rex.Media.TestKit;

namespace Rex.Media.App.Tests;

/// <summary>Volume, mute, and choosing among a file's audio and subtitle tracks.</summary>
public sealed partial class AppWindowTests
{
    private static double Volume(AppProcess app) =>
        ((RangeValuePattern)app.Find("VolumeBar").GetCurrentPattern(RangeValuePattern.Pattern)).Current.Value;

    private static void Expand(AppProcess app, params string[] menus)
    {
        foreach (var menu in menus)
        {
            ((ExpandCollapsePattern)app.Find(menu).GetCurrentPattern(ExpandCollapsePattern.Pattern)).Expand();
        }
    }

    [Fact]
    [Capability("AU-05")]
    [Capability("AU-06")]
    public void TheVolumeStepsAndMuteSilencesWithoutLosingTheLevel()
    {
        using var app = AppProcess.Start([Named("Sungba", 30)]);
        Wait.For(() => app.Position > 0, "the song to play");

        app.SetValue("VolumeBar", 50);
        Wait.For(() => Volume(app) == 50, "the volume bar to be set");
        app.Run(CommandCatalog.VolumeUp);
        Wait.For(() => Volume(app) > 50 && app.LogText.Contains("Volume " + Volume(app) + " %", StringComparison.Ordinal), "the volume to step up");
        var raised = Volume(app);
        app.Run(CommandCatalog.VolumeDown);
        Wait.For(() => Volume(app) == 50, "the volume to step back down");
        Assert.True(raised > 50);

        app.Press("MuteButton");
        Wait.For(() => app.Find("MuteButton").Current.Name == "Unmute", "the mute button to offer the sound back");
        Assert.Contains("Muted", app.LogText, StringComparison.Ordinal);
        Assert.Equal(50, Volume(app));
        app.Press("MuteButton");
        Wait.For(() => app.Find("MuteButton").Current.Name == "Mute" && app.LogText.Contains("Sound on", StringComparison.Ordinal), "the sound to come back");
        Assert.Equal(50, Volume(app));
        Assert.Equal(0, app.Close());
    }

    [Fact]
    [Capability("PB-17")]
    public void AudioTracksAreChosenInTurnAndASingleTrackSaysSo()
    {
        using var app = AppProcess.Start([RepoPaths.Combine("tests", "fixtures", "mkv", "flac-mp3-pcm.mkv")]);
        Wait.For(() => app.Status().Title is { Length: > 0 }, "the film to open");

        foreach (var turn in new[] { "Audio track 2 of 3", "Audio track 3 of 3", "Audio track 1 of 3" })
        {
            app.Run(CommandCatalog.CycleAudioTrack);
            Wait.For(() => app.LogText.Contains(turn, StringComparison.Ordinal), turn);
        }

        app.Run(CommandCatalog.OpenLocation);
        ((ValuePattern)app.Find("LocationBox").GetCurrentPattern(ValuePattern.Pattern)).SetValue(Named("Sungba", 30));
        app.Press("PrimaryButton");
        Wait.For(() => app.Text("NowPlaying") == "Sungba", "the song to play");
        app.Run(CommandCatalog.CycleAudioTrack);
        Wait.For(() => app.LogText.Contains("There is only one audio track.", StringComparison.Ordinal), "the single track to be explained");
        Assert.Equal(0, app.Close());
    }

    [Fact]
    [Capability("PB-17")]
    [Capability("SUB-14")]
    public void SubtitleTracksAreChosenFromTheMenuAndInTurn()
    {
        var film = Path.Combine(_media, "Film.mp4");
        File.Copy(RepoPaths.Combine("tests", "fixtures", "mp4", "h264-aac.mp4"), film);
        File.WriteAllText(Path.Combine(_media, "Film.en.srt"), "1\n00:00:00,000 --> 00:10:00,000\nEnglish words\n");
        File.WriteAllText(Path.Combine(_media, "Film.fr.srt"), "1\n00:00:00,000 --> 00:10:00,000\nMots en français\n");
        using var app = AppProcess.Start([film]);
        Wait.For(() => app.IsShown("SubtitleText"), "the first subtitles to show");
        app.Press("PlayPauseButton");
        var first = app.Text("SubtitleText");

        Expand(app, "SubtitleMenu", "SubtitleTrackChoices");
        Wait.For(() => app.IsShown("SubtitleTrackChoice-1"), "the second track to be offered");
        app.Press("SubtitleTrackChoice-1");
        Wait.For(() => app.IsShown("SubtitleText") && app.Text("SubtitleText") != first, "the other track to show");

        Expand(app, "SubtitleMenu", "SubtitleTrackChoices");
        app.Press("SubtitleTrackOff");
        Wait.For(() => !app.IsShown("SubtitleText") && app.LogText.Contains("Subtitles off", StringComparison.Ordinal), "the subtitles to go");

        app.Run(CommandCatalog.CycleSubtitles);
        Wait.For(() => app.IsShown("SubtitleText") && app.Text("SubtitleText") == first, "cycling to start again at the first track");
        Assert.Equal(0, app.Close());
    }
}
