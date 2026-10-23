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
        Screenshots.Match(app, "lyrics-mode-" + mode, "Stage");
        Assert.Equal(0, app.Close());
    }
}
