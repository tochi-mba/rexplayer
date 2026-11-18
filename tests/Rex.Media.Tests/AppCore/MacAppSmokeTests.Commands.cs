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
    /// The four window commands that used to answer "comes to the Mac in a later version", driven
    /// through the pipe as any command is: the zoom navigator shows, hides and recentres the view;
    /// saving a transcript with none heard fails gracefully; the shortcut sheet opens with grouped
    /// lines and closes the way Esc closes it; and clearing the playlist leaves Up Next empty.
    /// </summary>
    [Fact(Skip = MacAppRequired, SkipUnless = nameof(CanDriveMacApp))]
    public async Task TheMacHandlesTheNavigatorTranscriptShortcutSheetAndClearCommands()
    {
        var bundle = Environment.GetEnvironmentVariable("REXPLAYER_MAC_APP");
        var root = Path.Combine(Path.GetTempPath(), "rexplayer-mac-commands-" + Guid.NewGuid().ToString("N")[..8]);
        var startupRoots = new DataRoots(root);
        SettingsStore.Save(startupRoots.SettingsPath, new PlayerSettings
        {
            FirstRunDone = true,
            LastSeenVersion = "0.0.0",
        });
        var pipe = "rexplayer-mac-" + Guid.NewGuid().ToString("N")[..8];
        var start = new ProcessStartInfo(Path.Combine(bundle!, "Contents", "MacOS", "rexplayer"))
        {
            UseShellExecute = false,
        };
        start.Environment["REXPLAYER_ROOT"] = root;
        start.Environment["REXPLAYER_FAKE_AUDIO"] = "1";
        start.Environment["REXPLAYER_PIPE_NAME"] = pipe;
        start.Environment["REXPLAYER_MAC_QUEUE_TESTS"] = "1";
        start.Environment["REXPLAYER_MAC_SHORTCUT_TESTS"] = "1";
        using var app = Process.Start(start)!;
        try
        {
            await Until(() => Ask(pipe, new AutomationRequest { Command = "status" }), reply => reply is { Ok: true }, app, root);

            // The navigator needs a picture on the stage; the tiny conformance clip can finish
            // before the probes round trip, so the longer fixture plays and is paused in place.
            var video = RepoPaths.Combine("tests", "fixtures", "mp4", "h264-aac-minute.mp4");
            var opened = await Ask(pipe, new AutomationRequest { Command = "open", Paths = [video] });
            Assert.True(opened?.Ok == true, "The app did not acknowledge the open request: " + (opened?.Error ?? "no reply") + LogTail(root));
            await Until(() => Ask(pipe, new AutomationRequest { Command = "status" }),
                reply => reply is { Title: not null, State: "Playing" }, app, root);
            await Ask(pipe, new AutomationRequest { Command = "run", Id = CommandCatalog.PlayPause });
            await Until(() => Ask(pipe, new AutomationRequest { Command = "status" }),
                reply => reply is { State: "Paused" }, app, root);

            // Zoomed in with the default setting on, the navigator is owed and shown.
            var magnified = await Ask(pipe, new AutomationRequest { Command = "mac-video-geometry", Id = "magnify-and-pan" });
            Assert.Contains("zoom=2.00;pan=0.25,-0.50", magnified?.Text, StringComparison.Ordinal);
            var navigator = await Ask(pipe, new AutomationRequest { Command = "mac-video-geometry", Id = "navigator" });
            Assert.Contains("visible=True;setting=True", navigator?.Text, StringComparison.Ordinal);
            var hidden = await Ask(pipe, new AutomationRequest { Command = "run", Id = CommandCatalog.ToggleNavigator });
            Assert.True(hidden?.Ok == true, "The Mac navigator toggle was not handled: " + (hidden?.Error ?? "no reply") + LogTail(root));
            var navigatorOff = await Ask(pipe, new AutomationRequest { Command = "mac-video-geometry", Id = "navigator" });
            Assert.Contains("visible=False;setting=False", navigatorOff?.Text, StringComparison.Ordinal);
            Assert.False(SettingsStore.Load(startupRoots.SettingsPath).ShowNavigator,
                "Turning the navigator off was not remembered in the settings.");
            await Ask(pipe, new AutomationRequest { Command = "run", Id = CommandCatalog.ToggleNavigator });
            var navigatorBack = await Ask(pipe, new AutomationRequest { Command = "mac-video-geometry", Id = "navigator" });
            Assert.Contains("visible=True;setting=True", navigatorBack?.Text, StringComparison.Ordinal);
            // Centring on the top-left quarter through the navigator's own path pans fully there.
            var centred = await Ask(pipe, new AutomationRequest { Command = "mac-video-geometry", Id = "navigator-center" });
            Assert.Contains("zoom=2.00;pan=-1.00,-1.00", centred?.Text, StringComparison.Ordinal);
            await Ask(pipe, new AutomationRequest { Command = "mac-video-geometry", Id = "reset" });
            var navigatorAtRest = await Ask(pipe, new AutomationRequest { Command = "mac-video-geometry", Id = "navigator" });
            Assert.Contains("visible=False;setting=True;zoom=1.00", navigatorAtRest?.Text, StringComparison.Ordinal);

            // With nothing transcribed, saving must decline politely and leave the app answering.
            var transcript = await Ask(pipe, new AutomationRequest { Command = "run", Id = CommandCatalog.SaveTranscript });
            Assert.True(transcript?.Ok == true, "The Mac save-transcript command was not handled: " + (transcript?.Error ?? "no reply") + LogTail(root));
            Assert.True((await Ask(pipe, new AutomationRequest { Command = "status" }))?.Ok == true,
                "The app stopped answering after declining to save a transcript." + LogTail(root));

            // The sheet opens with grouped lines, and the Esc path closes it again.
            var sheetShown = await Ask(pipe, new AutomationRequest { Command = "run", Id = CommandCatalog.ShortcutSheet });
            Assert.True(sheetShown?.Ok == true, "The Mac shortcut sheet command was not handled: " + (sheetShown?.Error ?? "no reply") + LogTail(root));
            var sheet = await Ask(pipe, new AutomationRequest { Command = "mac-shortcuts", Id = "sheet" });
            Assert.True(sheet?.Ok == true, "The shortcut sheet is not visible with grouped rows: " + (sheet?.Error ?? "no reply") + LogTail(root));
            var sheetClosed = await Ask(pipe, new AutomationRequest { Command = "mac-shortcuts", Id = "sheet-escape" });
            Assert.True(sheetClosed?.Ok == true, "Esc did not close the shortcut sheet: " + (sheetClosed?.Error ?? "no reply") + LogTail(root));
            var sheetGone = await Ask(pipe, new AutomationRequest { Command = "mac-shortcuts", Id = "sheet" });
            Assert.True(sheetGone?.Ok == false, "The shortcut sheet still reports itself visible after closing.");

            // Clearing the playlist stops playback and leaves Up Next empty.
            var clearedQueue = await Ask(pipe, new AutomationRequest { Command = "run", Id = CommandCatalog.ClearPlaylist });
            Assert.True(clearedQueue?.Ok == true, "The Mac clear-playlist command was not handled: " + (clearedQueue?.Error ?? "no reply") + LogTail(root));
            await Until(() => Ask(pipe, new AutomationRequest { Command = "status" }),
                reply => reply is { State: "Idle" }, app, root);
            var emptyQueue = await Queue(pipe, "show");
            Assert.Contains("0 items", emptyQueue.Title, StringComparison.Ordinal);

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
