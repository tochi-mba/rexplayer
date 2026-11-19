using System.Diagnostics;
using Rex.Media.AppCore.Automation;
using Rex.Media.AppCore.Commands;
using Rex.Media.AppCore.Platform;
using Rex.Media.Settings;
using Rex.Media.TestKit;

namespace Rex.Media.Tests.AppCore;

public sealed partial class MacAppSmokeTests
{
    /// <summary>
    /// The detached lyrics window on the Mac: a song with an LRC beside it offers the window on
    /// the stage, the window opens with every line, the highlighted line moves when playback is
    /// sent elsewhere, the shared lyrics command hides the words there too, and closing the
    /// window leaves the song and the app as they were.
    /// </summary>
    [Fact(Skip = MacAppRequired, SkipUnless = nameof(CanDriveMacApp))]
    [Capability("META-09", CapabilityPlatform.MacOS)]
    public async Task TheMacLyricsWindowFollowsTheSongAndClosesCleanly()
    {
        var bundle = Environment.GetEnvironmentVariable("REXPLAYER_MAC_APP");
        var root = Path.Combine(Path.GetTempPath(), "rexplayer-mac-lyrics-" + Guid.NewGuid().ToString("N")[..8]);
        var startupRoots = new DataRoots(root);
        SettingsStore.Save(startupRoots.SettingsPath, new PlayerSettings
        {
            FirstRunDone = true,
            LastSeenVersion = "0.0.0",
            // The fixture lasts about six tenths of a second; repeating it keeps the song (and
            // its lyrics) loaded however long the steps below take on a slow runner.
            Repeat = RepeatMode.One,
        });
        var songs = Path.Combine(root, "songs");
        Directory.CreateDirectory(songs);
        var song = Path.Combine(songs, "Sample Song.mp3");
        File.Copy(RepoPaths.Combine("tests", "fixtures", "mp3", "stereo-44k-128k-cbr.mp3"), song);
        // The fixture lasts about six tenths of a second, so the second line starts inside it.
        File.WriteAllText(Path.Combine(songs, "Sample Song.lrc"),
            "[00:00.00]First local lyric\n[00:00.30]Second local lyric\n");
        var pipe = "rexplayer-mac-" + Guid.NewGuid().ToString("N")[..8];
        var start = new ProcessStartInfo(Path.Combine(bundle!, "Contents", "MacOS", "rexplayer"))
        {
            UseShellExecute = false,
        };
        start.Environment["REXPLAYER_ROOT"] = root;
        start.Environment["REXPLAYER_FAKE_AUDIO"] = "1";
        start.Environment["REXPLAYER_PIPE_NAME"] = pipe;
        using var app = Process.Start(start)!;
        try
        {
            await Until(() => Ask(pipe, new AutomationRequest { Command = "status" }), reply => reply is { Ok: true }, app, root);
            var opened = await Ask(pipe, new AutomationRequest { Command = "open", Paths = [song] });
            Assert.True(opened?.Ok == true, "The app did not acknowledge the open request: " + (opened?.Error ?? "no reply") + LogTail(root));
            var stage = await Until(() => Ask(pipe, new AutomationRequest { Command = "mac-music" }),
                reply => reply?.Text?.Contains(";lyrics=True", StringComparison.Ordinal) == true, app, root);
            Assert.Contains("companion=False", stage.Text, StringComparison.Ordinal);

            // Hold the song still so the checks below do not race its end; seeks still move it.
            // A pause sent while the repeat reopens the song can be lost, so it is sent again.
            await Until(async () =>
            {
                var now = await Ask(pipe, new AutomationRequest { Command = "status" });
                if (now is { State: "Playing" })
                {
                    await Ask(pipe, new AutomationRequest { Command = "run", Id = CommandCatalog.PlayPause });
                }

                return now;
            }, reply => reply is { State: "Paused" }, app, root);

            var companion = await Ask(pipe, new AutomationRequest { Command = "mac-music", Id = "companion-open" });
            Assert.True(companion?.Ok == true, "The stage did not open the lyrics window: " + (companion?.Error ?? "no reply") + LogTail(root));
            Assert.Contains("companion=True", companion!.Text, StringComparison.Ordinal);
            Assert.Contains("rows=2;mode=LineTimed", companion.Text, StringComparison.Ordinal);
            var stageWithWindow = await Ask(pipe, new AutomationRequest { Command = "mac-music" });
            Assert.EndsWith(";companion=True", stageWithWindow?.Text, StringComparison.Ordinal);

            // Sending playback to each line moves the window's highlight with it.
            await Ask(pipe, new AutomationRequest { Command = "seek", To = 0.4 });
            var second = await Until(() => Ask(pipe, new AutomationRequest { Command = "mac-music", Id = "companion" }),
                reply => reply?.Text?.Contains("line=1;", StringComparison.Ordinal) == true, app, root);
            Assert.Contains("current=Second local lyric", second.Text, StringComparison.Ordinal);
            await Ask(pipe, new AutomationRequest { Command = "seek", To = 0.05 });
            var first = await Until(() => Ask(pipe, new AutomationRequest { Command = "mac-music", Id = "companion" }),
                reply => reply?.Text?.Contains("line=0;", StringComparison.Ordinal) == true, app, root);
            Assert.Contains("current=First local lyric", first.Text, StringComparison.Ordinal);

            // The shared lyrics command hides the words in the window as well as on the stage.
            var hide = await Ask(pipe, new AutomationRequest { Command = "run", Id = CommandCatalog.ToggleLyrics });
            Assert.True(hide?.Ok == true, "The shared lyrics command was not handled: " + (hide?.Error ?? "no reply"));
            var hidden = await Ask(pipe, new AutomationRequest { Command = "mac-music", Id = "companion" });
            Assert.Contains("companion=True;line=-1;rows=0", hidden?.Text, StringComparison.Ordinal);
            await Ask(pipe, new AutomationRequest { Command = "run", Id = CommandCatalog.ToggleLyrics });
            var shownAgain = await Ask(pipe, new AutomationRequest { Command = "mac-music", Id = "companion" });
            Assert.Contains("rows=2", shownAgain?.Text, StringComparison.Ordinal);

            var closed = await Ask(pipe, new AutomationRequest { Command = "mac-music", Id = "companion-close" });
            Assert.Contains("companion=False", closed?.Text, StringComparison.Ordinal);
            var afterClose = await Ask(pipe, new AutomationRequest { Command = "status" });
            Assert.True(afterClose is { Ok: true, Title: not null },
                "Closing the lyrics window must leave the song open: " + Described(afterClose) + LogTail(root));

            var quit = await Ask(pipe, new AutomationRequest { Command = "run", Id = CommandCatalog.Quit });
            Assert.True(quit?.Ok == true || (app.WaitForExit(TimeSpan.FromSeconds(5)) && app.ExitCode == 0),
                "The app neither acknowledged quit nor exited cleanly: " + (quit?.Error ?? "no reply") + LogTail(root));
            Assert.True(app.WaitForExit(Patience), "rexplayer.app did not quit when asked.");
            Assert.Equal(0, app.ExitCode);
        }
        finally
        {
            if (!app.HasExited)
            {
                app.Kill();
                app.WaitForExit();
            }

            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
