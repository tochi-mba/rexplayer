using System.IO;
using System.Windows.Automation;
using Rex.Media.AppCore.Commands;
using Rex.Media.TestKit;

namespace Rex.Media.App.Tests;

public sealed partial class AppWindowTests
{
    private static AutomationElement? DetachedLyricsOf(AppProcess app)
    {
        var processId = app.Window.Current.ProcessId;
        foreach (var window in AutomationElement.RootElement.FindAll(TreeScope.Children,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Window))
            .Cast<AutomationElement>())
        {
            try
            {
                if (window.Current.ProcessId == processId
                    && window.Current.Name.Contains("Lyrics · rexplayer", StringComparison.Ordinal))
                {
                    return window;
                }
            }
            catch (ElementNotAvailableException)
            {
                // A top-level window may close between enumeration and reading.
            }
        }

        return null;
    }

    [Fact]
    [Capability("META-09")]
    public void DetachedNativeLyricsCanCloseReopenSeekAndFollowTheNextTrackWithoutOwningPlayback()
    {
        var first = Song(90);
        var next = Path.Combine(_media, "Another.wav");
        File.WriteAllBytes(next, WavBuilder.Pcm(8000, 1, 16, Pcm.Int16(new float[8000 * 90])).Build());
        File.WriteAllText(Path.ChangeExtension(first, ".lrc"),
            "[00:00.00]<00:00.00>First <00:00.50>song\n[00:20.00]Second line");
        File.WriteAllText(Path.ChangeExtension(next, ".lrc"),
            "[00:00.00]Another lyric\n[00:30.00]Another line");
        var playlist = Path.Combine(_media, "Companion.m3u8");
        File.WriteAllText(playlist, "Sungba.wav\nAnother.wav\n");
        using var app = AppProcess.Start([playlist]);

        Wait.For(() => app.IsShown("LyricsDetach"), "the native detach control");
        app.Press("PlayPauseButton");
        app.Press("LyricsDetach");
        var companion = Wait.Until(() => DetachedLyricsOf(app));
        var line = Wait.Until(() => companion.FindFirst(TreeScope.Descendants,
            new PropertyCondition(AutomationElement.AutomationIdProperty, "CompanionLine-1")));
        Assert.Contains("Go to 0:20", line.Current.Name, StringComparison.Ordinal);
        Assert.InRange(companion.Current.BoundingRectangle.Width, 200, 1500);
        ((InvokePattern)line.GetCurrentPattern(InvokePattern.Pattern)).Invoke();
        Wait.For(() => Math.Abs(app.Position - 20) < 1, "a detached lyric to seek its original player");

        ((WindowPattern)companion.GetCurrentPattern(WindowPattern.Pattern)).Close();
        Wait.For(() => DetachedLyricsOf(app) is null, "the detached window to close independently");
        Assert.NotEqual("Idle", app.Status().State);
        app.Press("LyricsDetach");
        var reopened = Wait.Until(() => DetachedLyricsOf(app));
        var firstLine = Wait.Until(() => reopened.FindFirst(TreeScope.Descendants,
            new PropertyCondition(AutomationElement.AutomationIdProperty, "CompanionLine-0")));
        firstLine.SetFocus();

        app.Run(CommandCatalog.Next);
        Wait.For(() => app.Text("NowPlaying") == "Another", "the next track to load");
        Wait.For(() => reopened.FindFirst(TreeScope.Descendants,
                new AndCondition(new PropertyCondition(AutomationElement.AutomationIdProperty, "CompanionLine-0"),
                    new PropertyCondition(AutomationElement.NameProperty, "Go to 0:00: Another lyric"))) is not null,
            "the detached window to show only the new track's lyrics");
        Wait.For(() => reopened.FindFirst(TreeScope.Descendants,
            new PropertyCondition(AutomationElement.AutomationIdProperty, "CompanionLine-0"))!.Current.HasKeyboardFocus,
            "keyboard focus to remain in the detached lyrics window after track changes");

        // The owner closes its companion first. This must not leave an orphan
        // window or a player listener on the second native top-level window.
        Assert.Equal(0, app.Close());
    }
}
