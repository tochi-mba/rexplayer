using System.Diagnostics;
using Rex.Media.AppCore.Automation;
using Rex.Media.AppCore.Commands;
using Rex.Media.Settings;
using Rex.Media.TestKit;

namespace Rex.Media.Tests.AppCore;

public sealed partial class MacAppSmokeTests
{
    /// <summary>
    /// The Mac Preferences window as sections (UI-10), driven the way a user lives it: opening it
    /// shows the rail of sections with the first one's page, choosing a section shows its page, a
    /// search leads to the section holding the match and owns up when nothing does, and a value
    /// changed through its control survives Save into the settings the next opening reads.
    /// </summary>
    [Fact(Skip = MacAppRequired, SkipUnless = nameof(CanDriveMacApp))]
    [Capability("UI-10", CapabilityPlatform.MacOS)]
    public async Task TheMacPreferencesSectionsSearchAndSaveRoundTrip()
    {
        var bundle = Environment.GetEnvironmentVariable("REXPLAYER_MAC_APP");
        var root = Path.Combine(Path.GetTempPath(), "rexplayer-mac-preferences-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(root);
        var pipe = "rexplayer-mac-" + Guid.NewGuid().ToString("N")[..8];
        SettingsStore.Save(new Rex.Media.AppCore.Platform.DataRoots(root).SettingsPath, new PlayerSettings
        {
            FirstRunDone = true,
            UpdateChecks = UpdateCadence.Off,
        });
        var start = new ProcessStartInfo(Path.Combine(bundle!, "Contents", "MacOS", "rexplayer"))
        {
            UseShellExecute = false,
        };
        start.Environment["REXPLAYER_ROOT"] = root;
        start.Environment["REXPLAYER_FAKE_AUDIO"] = "1";
        start.Environment["REXPLAYER_PIPE_NAME"] = pipe;
        start.Environment["REXPLAYER_MAC_PREFERENCES_TESTS"] = "1";
        using var app = Process.Start(start)!;
        try
        {
            await Until(() => Ask(pipe, new AutomationRequest { Command = "status" }),
                reply => reply is { Ok: true }, app, root);

            var shown = await Ask(pipe, new AutomationRequest { Command = "mac-preferences", Id = "show" });
            Assert.True(shown?.Ok == true, "Preferences did not open: " + (shown?.Error ?? "no reply") + LogTail(root));
            Assert.Contains(
                "sections=Playback,Video and pictures,Snapshots,Window,History and resume,Opening files,Diagnostics",
                shown!.Text);
            Assert.Contains("selected=Playback", shown.Text);

            var selected = await Ask(pipe, new AutomationRequest { Command = "mac-preferences", Id = "select:Window" });
            Assert.True(selected?.Ok == true, "The Window section was not chosen: " + (selected?.Error ?? "no reply") + LogTail(root));
            Assert.Contains("selected=Window", selected!.Text);

            // A search for a setting, not a section name: it must lead back to Playback's volume slider.
            var searched = await Ask(pipe, new AutomationRequest { Command = "mac-preferences", Id = "search:volume" });
            Assert.True(searched?.Ok == true, "The Preferences search failed: " + (searched?.Error ?? "no reply") + LogTail(root));
            Assert.Contains("matching=Playback", searched!.Text);
            Assert.Contains("selected=Playback", searched.Text);
            Assert.DoesNotContain("matches=0", searched.Text);

            var empty = await Ask(pipe, new AutomationRequest { Command = "mac-preferences", Id = "search:nothing says this" });
            Assert.True(empty?.Ok == true, "The empty Preferences search failed: " + (empty?.Error ?? "no reply") + LogTail(root));
            Assert.Contains("matches=0", empty!.Text);
            Assert.Contains("selected=none", empty.Text);

            // A real round trip: the Playback slider moves, Save writes it, and the window rebuilt
            // from what was saved shows the moved value.
            var moved = await Ask(pipe, new AutomationRequest { Command = "mac-preferences", Id = "set-max-volume:150" });
            Assert.True(moved?.Ok == true, "The volume slider was not moved: " + (moved?.Error ?? "no reply") + LogTail(root));
            Assert.Contains("maxVolume=150", moved!.Text);
            var saved = await Ask(pipe, new AutomationRequest { Command = "mac-preferences", Id = "save" });
            Assert.True(saved?.Ok == true, "Preferences were not saved: " + (saved?.Error ?? "no reply") + LogTail(root));
            Assert.Contains("maxVolume=150", saved!.Text);
            var reopened = await Ask(pipe, new AutomationRequest { Command = "mac-preferences", Id = "show" });
            Assert.True(reopened?.Ok == true, "Preferences did not reopen: " + (reopened?.Error ?? "no reply") + LogTail(root));
            Assert.Contains("maxVolume=150", reopened!.Text);
            Assert.Contains("selected=Playback", reopened.Text);

            var quit = await Ask(pipe, new AutomationRequest { Command = "run", Id = CommandCatalog.Quit });
            Assert.True(quit?.Ok == true || (app.WaitForExit(TimeSpan.FromSeconds(5)) && app.ExitCode == 0),
                "The Mac app could not quit: " + (quit?.Error ?? "no reply") + LogTail(root));
            Assert.True(app.WaitForExit(Patience));
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
