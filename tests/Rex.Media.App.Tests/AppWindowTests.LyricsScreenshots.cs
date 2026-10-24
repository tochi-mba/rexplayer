using System.IO;
using System.Windows.Automation;
using Rex.Media.AppCore.Commands;
using Rex.Media.TestKit;

namespace Rex.Media.App.Tests;

public sealed partial class AppWindowTests
{
    /// <summary>
    /// Five factual local-lyrics states on an actual WinUI stage, with fixed source content
    /// and a disabled visualizer to avoid a moving picture contaminating comparisons.
    /// Record-only until each native image has been reviewed and approved as a baseline.
    /// </summary>
    [Theory]
    [Capability("META-09")]
    [InlineData("none", null, "No lyrics found in this song or beside it.")]
    [InlineData("untimed", "No timings were supplied", "Untimed lyrics; no line or word times were supplied.")]
    [InlineData("line", "[00:00.00]First line\\n[00:20.00]Second line", "Timed lyrics; only line starts were supplied.")]
    [InlineData("partial", "[00:00.00]<00:00.00>First <00:00.50>word\\n[00:20.00]Second line", "Partly word-timed lyrics; untimed words are shown without invented timing.")]
    [InlineData("word", "[00:00.00]<00:00.00>First <00:00.50>word\\n[00:20.00]<00:20.00>Last", "Word-timed lyrics; highlights follow exact source timestamps.")]
    public void ActualLyricsModesHaveNativeStageCaptures(string mode, string? text, string expectedStatus)
    {
        var song = Song(40);
        var sidecar = Path.ChangeExtension(song, ".lrc");
        if (text is not null)
        {
            File.WriteAllText(sidecar, text.Replace("\\n", "\n", StringComparison.Ordinal));
        }

        using var app = AppProcess.Start([song], RootShowing("Off"));
        var transform = (TransformPattern)app.Window.GetCurrentPattern(TransformPattern.Pattern);
        transform.Resize(900, 700);
        Wait.For(() => Math.Abs(app.Window.Current.BoundingRectangle.Width - 900) < 2 &&
            Math.Abs(app.Window.Current.BoundingRectangle.Height - 700) < 2, "the fixed lyrics viewport");
        Wait.For(() => app.Text("LyricsStatus") == expectedStatus,
            "the exact mode to be announced before capturing " + mode);
        app.Run(CommandCatalog.PlayPause);
        Wait.For(() => app.Text("LyricsStatus") == expectedStatus,
            "the lyric label to survive pausing");

        // A missing source must keep its accessible status without pretending
        // there are clickable timestamps. Plain source text must likewise never
        // invent a seek target; every timed mode must expose the real LRC line.
        Assert.Equal(mode != "none", app.IsShown("Lyrics"));
        Assert.Equal(mode is "line" or "partial" or "word", app.IsShown("LyricLine-0"));
        if (mode is "line" or "partial" or "word")
        {
            Assert.StartsWith("Go to 0:00: First", app.Find("LyricLine-0").Current.Name,
                StringComparison.Ordinal);
            Assert.Contains("Second", app.Find("LyricLine-1").Current.Name,
                StringComparison.Ordinal);
        }

        Screenshots.Match(app, "lyrics-mode-" + mode, "Stage");
        // Capture each actual mode under compact wrapping as well as the standard
        // view. The same underlying audio and source remain paused and stable.
        transform.Resize(720, 640);
        Wait.For(() => Math.Abs(app.Window.Current.BoundingRectangle.Width - 720) < 2 &&
            Math.Abs(app.Window.Current.BoundingRectangle.Height - 640) < 2,
            "the compact lyric viewport");
        Wait.For(() => app.Text("LyricsStatus") == expectedStatus,
            "the lyric mode to survive a compact-window resize");
        Screenshots.Match(app, "lyrics-mode-" + mode + "-compact", "Stage");
        Assert.Equal(0, app.Close());
    }
    [Fact]
    [Capability("META-09")]
    public void TimedLyricActionsSeekFromAccessibleNativeButtonsAndKeepTheModeHonest()
    {
        var song = Song(40);
        File.WriteAllText(Path.ChangeExtension(song, ".lrc"),
            "[00:00.00]First line\n[00:20.00]Second line");
        using var app = AppProcess.Start([song], RootShowing("Off"));
        var transform = (TransformPattern)app.Window.GetCurrentPattern(TransformPattern.Pattern);
        transform.Resize(900, 700);
        Wait.For(() => app.Text("LyricsStatus") == "Timed lyrics; only line starts were supplied.",
            "the line-timed mode from its real LRC source");
        app.Run(CommandCatalog.PlayPause);
        Wait.For(() => app.IsShown("LyricLine-1"), "the line-start action to be available");
        var target = app.Find("LyricLine-1");
        Assert.Contains("Go to 0:20:", target.Current.Name, StringComparison.Ordinal);
        app.Press("LyricLine-1");
        Wait.For(() => Math.Abs(app.Position - 20) < 1,
            "the player's seek position to follow the real source timestamp");
        Assert.Equal("Timed lyrics; only line starts were supplied.", app.Text("LyricsStatus"));
        Screenshots.Match(app, "lyrics-line-seek-action", "Stage");
        Assert.Equal(0, app.Close());
    }

}
