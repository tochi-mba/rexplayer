using System.Diagnostics;
using System.Globalization;
using Rex.Media.AppCore.Automation;
using Rex.Media.AppCore.Commands;
using Rex.Media.AppCore.Design;
using Rex.Media.AppCore.Platform;
using Rex.Media.AppCore.Player;
using Rex.Media.AppCore.Visuals;
using Rex.Media.Library;
using Rex.Media.Settings;
using Rex.Media.TestKit;

namespace Rex.Media.Tests.AppCore;

public sealed partial class MacAppSmokeTests
{
    /// <summary>
    /// The idle player window draws the welcome the Windows window draws, control for control and
    /// kind for kind, with the transport bar under it, and saves a picture of itself for review.
    /// </summary>
    [Fact(Skip = MacAppRequired, SkipUnless = nameof(CanDriveMacApp))]
    [Capability("LOOK-01", CapabilityPlatform.MacOS)]
    public async Task TheMacWelcomeScreenHasTheControlsWindowsHas()
    {
        var root = LookRoot("welcome");
        using var app = StartLookApp(root, out var pipe);
        try
        {
            await ReadyForALook(pipe, app, root);

            // With nothing open, ui.describe lists every visible control the window records, in the
            // order it built them: the welcome's six first, then the bar's, so the reply is the
            // welcome's description, a semicolon, then the transport bar's.
            var described = await Ask(pipe, new AutomationRequest { Command = "ui.describe" });
            Assert.True(described?.Ok == true, "ui.describe failed: " + (described?.Error ?? "no reply") + LogTail(root));
            AssertDescribes(described!.Text, ShellScreens.Welcome, ShellScreens.Transport);

            // Look for: the dark stage with WELCOME in the signal colour, the large rexplayer name,
            // the hint, the filled Open file button beside the outlined Browse library button, the
            // tagline, and the bar at the foot with its seek row and icon transport row.
            await SaveLookScreenshot(pipe, "welcome", root);
            await QuitLookApp(pipe, app, root);
        }
        finally
        {
            EndLookApp(app, root);
        }
    }

    /// <summary>
    /// With a video open and paused, the bar shows the seek row and the transport row the Windows
    /// bar shows, and none of the buttons kept for full screen.
    /// </summary>
    [Fact(Skip = MacAppRequired, SkipUnless = nameof(CanDriveMacApp))]
    [Capability("LOOK-02", CapabilityPlatform.MacOS)]
    public async Task TheMacTransportBarHasTheControlsWindowsHas()
    {
        var root = LookRoot("transport");
        using var app = StartLookApp(root, out var pipe);
        try
        {
            await ReadyForALook(pipe, app, root);
            var video = RepoPaths.Combine("tests", "fixtures", "mp4", "h264-aac-minute.mp4");
            var opened = await Ask(pipe, new AutomationRequest { Command = "open", Paths = [video] });
            Assert.True(opened?.Ok == true, "The app did not acknowledge the open request: " + (opened?.Error ?? "no reply") + LogTail(root));
            await Until(() => Ask(pipe, new AutomationRequest { Command = "status" }),
                reply => reply is { Title: not null, State: "Playing" }, app, root);
            await PauseWhatPlays(pipe, app, root);

            // Something is open, so the welcome is hidden and only the bar is described. ui.describe
            // leaves out hidden controls, and the three full-screen buttons are hidden out of it.
            var described = await Ask(pipe, new AutomationRequest { Command = "ui.describe" });
            Assert.True(described?.Ok == true, "ui.describe failed: " + (described?.Error ?? "no reply") + LogTail(root));
            AssertDescribes(described!.Text, ShellScreens.Transport);
            foreach (var fullScreenOnly in new[] { "FullScreenTracksButton", "FullScreenEffectsButton", "LeaveFullScreenButton" })
            {
                Assert.DoesNotContain(fullScreenOnly, described.Text, StringComparison.Ordinal);
            }

            // Look for: the bar's two rows, Start over and the clocks either side of the seek bar,
            // the round filled play button among the icon buttons, the video's title in the middle,
            // and the speed, mute and volume at the right; the picture fills the stage above.
            await SaveLookScreenshot(pipe, "player-video", root);
            await QuitLookApp(pipe, app, root);
        }
        finally
        {
            EndLookApp(app, root);
        }
    }

    /// <summary>
    /// A song draws the music stage with its spectrum's bars, and a visualisation that draws a
    /// scene (one that needs no camera) takes their place when it is chosen.
    /// </summary>
    [Fact(Skip = MacAppRequired, SkipUnless = nameof(CanDriveMacApp))]
    [Capability("LOOK-03", CapabilityPlatform.MacOS)]
    public async Task TheMacMusicStageDrawsItsVisualisation()
    {
        var root = LookRoot("music");
        var songs = Path.Combine(root, "songs");
        Directory.CreateDirectory(songs);
        var song = Path.Combine(songs, "Sample Song.mp3");
        File.Copy(RepoPaths.Combine("tests", "fixtures", "mp3", "stereo-44k-128k-cbr.mp3"), song);
        // The fixture lasts about six tenths of a second, so the second line starts inside it.
        File.WriteAllText(Path.Combine(songs, "Sample Song.lrc"),
            "[00:00.00]First local lyric\n[00:00.30]Second local lyric\n");
        // Repeating the short song keeps it (and its lyrics) loaded however long the steps below
        // take on a slow runner.
        using var app = StartLookApp(root, out var pipe, settings => settings with
        {
            Repeat = RepeatMode.One,
            Visualizer = VisualizerChoice.Spectrum,
        });
        try
        {
            await ReadyForALook(pipe, app, root);
            var opened = await Ask(pipe, new AutomationRequest { Command = "open", Paths = [song] });
            Assert.True(opened?.Ok == true, "The app did not acknowledge the open request: " + (opened?.Error ?? "no reply") + LogTail(root));
            await Until(() => Ask(pipe, new AutomationRequest { Command = "mac-music" }),
                reply => reply?.Text?.Contains("visible=True;", StringComparison.Ordinal) == true
                    && reply.Text.Contains(";lyrics=True", StringComparison.Ordinal), app, root);

            // meter is how many bars the spectrum drew last; it stays 0 until a frame has been drawn
            // from the sound, which happens only while the song plays, so it is waited for now.
            var drawing = await Until(() => Ask(pipe, new AutomationRequest { Command = "mac-music" }),
                reply => Meter(reply?.Text) > 0, app, root);
            Assert.Contains(";visualizer=" + VisualizerChoice.Spectrum + ";", drawing.Text, StringComparison.Ordinal);
            Assert.True(Meter(drawing.Text) > 0, "The spectrum drew no bars: " + drawing.Text);

            await PauseWhatPlays(pipe, app, root);
            var paused = await Ask(pipe, new AutomationRequest { Command = "mac-music" });
            Assert.Contains(";visualizer=" + VisualizerChoice.Spectrum + ";", paused?.Text, StringComparison.Ordinal);

            // Look for: the song's kicker and title on the dark stage, the band of rounded spectrum
            // bars over their faint baseline, and the two lyric lines at the foot.
            await SaveLookScreenshot(pipe, "player-music", root);

            // Each cycle moves to the next choice in the shared order, so from the spectrum the
            // cycles land on the vinyl record, the first choice that draws a scene, which needs no
            // camera (its label is the cover, and this song has none).
            const VisualizerChoice scene = VisualizerChoice.Vinyl;
            Assert.True(VisualScene.Draws(scene), "The chosen visualisation no longer draws a scene.");
            var choice = VisualizerChoice.Spectrum;
            while (choice != scene)
            {
                var cycled = await Ask(pipe, new AutomationRequest { Command = "run", Id = CommandCatalog.CycleVisualizer });
                Assert.True(cycled?.Ok == true, "Cycling the visualisation was not handled: " + (cycled?.Error ?? "no reply") + LogTail(root));
                choice = Visualizers.Next(choice);
                var now = await Ask(pipe, new AutomationRequest { Command = "mac-music" });
                Assert.Contains(";visualizer=" + choice + ";", now?.Text, StringComparison.Ordinal);
            }

            // A scene is drawn as a picture on the stage, not as bars, so no bars are counted.
            var sceneShown = await Ask(pipe, new AutomationRequest { Command = "mac-music" });
            Assert.Contains(";visualizer=" + scene + ";meter=0;", sceneShown?.Text, StringComparison.Ordinal);

            // Look for: the spinning record filling the band where the bars were, under the title.
            await SaveLookScreenshot(pipe, "player-scene", root);
            await QuitLookApp(pipe, app, root);
        }
        finally
        {
            EndLookApp(app, root);
        }
    }

    /// <summary>
    /// The library and Up Next each open as a pane inside the player window, with the transport
    /// bar still under them, rather than as windows of their own.
    /// </summary>
    [Fact(Skip = MacAppRequired, SkipUnless = nameof(CanDriveMacApp))]
    [Capability("LOOK-05", CapabilityPlatform.MacOS)]
    public async Task TheMacLibraryAndUpNextOpenInsideThePlayerWindow()
    {
        var root = LookRoot("panes");
        var mediaFolder = Path.Combine(root, "watched");
        Directory.CreateDirectory(mediaFolder);
        var mediaPath = Path.Combine(mediaFolder, "known-video.mp4");
        File.Copy(RepoPaths.Combine("tests", "fixtures", "mp4", "h264-aac.mp4"), mediaPath);
        var library = new Rex.Media.Library.MediaLibrary(RexStore.Open(new DataRoots(root).LibraryStore));
        Assert.True(library.AddFolder(mediaFolder));
        Assert.Equal(1, library.Scan(mediaFolder,
            [new LibraryFile(mediaPath, new FileInfo(mediaPath).Length, File.GetLastWriteTimeUtc(mediaPath))],
            _ => LibraryKind.Video).Added);

        using var app = StartLookApp(root, out var pipe, queueTests: true);
        try
        {
            await ReadyForALook(pipe, app, root);
            var shown = await Ask(pipe, new AutomationRequest { Command = "run", Id = CommandCatalog.ToggleLibrary });
            Assert.True(shown?.Ok == true, "The library command was not handled: " + (shown?.Error ?? "no reply") + LogTail(root));
            // The summary answers only while the library pane is shown.
            await Until(() => Ask(pipe, new AutomationRequest { Command = "ui.text", Target = "LibrarySummary" }),
                reply => reply is { Ok: true, Text: not null } && reply.Text.Contains("1 total", StringComparison.Ordinal), app, root);

            // The pane covers the stage, so the welcome is not described, but the bar under it is:
            // the library is in the player window, not over it in another.
            var describedWithLibrary = await Ask(pipe, new AutomationRequest { Command = "ui.describe" });
            Assert.True(describedWithLibrary?.Ok == true, "ui.describe failed: " + (describedWithLibrary?.Error ?? "no reply") + LogTail(root));
            AssertDescribes(describedWithLibrary!.Text, ShellScreens.Transport);

            // Look for: the navigation column at the left, the page beside it on its raised panel
            // with the banner, the search, the view options and the one video's row, Back to player
            // at the page's top right, and the transport bar still across the foot of the window.
            await SaveLookScreenshot(pipe, "library", root);

            var queued = await Ask(pipe, new AutomationRequest { Command = "ui.press", Target = "LibraryFirstQueue" });
            Assert.True(queued?.Ok == true, "The row's Add to Up Next button failed: " + (queued?.Error ?? "no reply") + LogTail(root));
            var hidden = await Ask(pipe, new AutomationRequest { Command = "run", Id = CommandCatalog.ToggleLibrary });
            Assert.True(hidden?.Ok == true, "The library command did not hide the pane: " + (hidden?.Error ?? "no reply") + LogTail(root));
            var gone = await Ask(pipe, new AutomationRequest { Command = "ui.text", Target = "LibrarySummary" });
            Assert.True(gone?.Ok == false, "The library pane still answered after it was hidden.");

            var upNext = await Ask(pipe, new AutomationRequest { Command = "run", Id = CommandCatalog.TogglePlaylist });
            Assert.True(upNext?.Ok == true, "The playlist command was not handled: " + (upNext?.Error ?? "no reply") + LogTail(root));
            // mac-queue answers with the pane's own summary first: "1 item", then what plays next.
            var queue = await Until(() => Ask(pipe, new AutomationRequest { Command = "mac-queue", Id = "show" }),
                reply => reply?.Title?.StartsWith("1 item ", StringComparison.Ordinal) == true, app, root);
            Assert.DoesNotContain("viewing", queue.Title, StringComparison.Ordinal);

            // Up Next sits at the right of the stage, so the bar is still described in full.
            var describedWithQueue = await Ask(pipe, new AutomationRequest { Command = "ui.describe" });
            Assert.True(describedWithQueue?.Ok == true, "ui.describe failed: " + (describedWithQueue?.Error ?? "no reply") + LogTail(root));
            Assert.Contains(ShellScreens.Describe(ShellScreens.Transport), describedWithQueue!.Text, StringComparison.Ordinal);

            // Look for: the Up Next pane at the right edge, divided from the stage by a hairline,
            // with its summary and the one video's line, and the bar across the whole foot.
            await SaveLookScreenshot(pipe, "up-next", root);

            var alive = await Ask(pipe, new AutomationRequest { Command = "status" });
            Assert.True(alive?.Ok == true, "The app stopped answering with Up Next shown." + LogTail(root));
            await QuitLookApp(pipe, app, root);
        }
        finally
        {
            EndLookApp(app, root);
        }
    }

    /// <summary>
    /// Asserts that <paramref name="described"/>, a ui.describe reply, is the given screens'
    /// descriptions one after another, each a contiguous run of "Id:kind" joined by semicolons.
    /// </summary>
    private static void AssertDescribes(string? described, params ShellScreen[] screens)
    {
        var expected = string.Join(";", screens.Select(ShellScreens.Describe));
        Assert.True(string.Equals(expected, described, StringComparison.Ordinal),
            "ui.describe did not describe " + string.Join(" then ", screens.Select(screen => screen.Name)) + "." +
            Environment.NewLine + "Expected: " + expected + Environment.NewLine + "Actual:   " + described);
        foreach (var screen in screens)
        {
            Assert.Contains(ShellScreens.Describe(screen), described, StringComparison.Ordinal);
        }
    }

    /// <summary>The bar count in a mac-music reply, or -1 when the reply carries none.</summary>
    private static int Meter(string? music)
    {
        const string field = ";meter=";
        var at = music?.IndexOf(field, StringComparison.Ordinal) ?? -1;
        if (at < 0)
        {
            return -1;
        }

        var digits = string.Concat(music![(at + field.Length)..].TakeWhile(char.IsAsciiDigit));
        return int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var bars) ? bars : -1;
    }

    private static string LookRoot(string name) =>
        Path.Combine(Path.GetTempPath(), "rexplayer-mac-look-" + name + "-" + Guid.NewGuid().ToString("N")[..8]);

    /// <summary>
    /// Starts the app on a data root whose first run is done, so no welcome window covers the
    /// player, with update checks off so nothing reaches the network.
    /// </summary>
    private static Process StartLookApp(string root, out string pipe,
        Func<PlayerSettings, PlayerSettings>? settings = null, bool queueTests = false)
    {
        var saved = new PlayerSettings
        {
            FirstRunDone = true,
            LastSeenVersion = "0.0.0",
            UpdateChecks = UpdateCadence.Off,
        };
        SettingsStore.Save(new DataRoots(root).SettingsPath, settings is null ? saved : settings(saved));
        pipe = "rexplayer-mac-" + Guid.NewGuid().ToString("N")[..8];
        var start = new ProcessStartInfo(Path.Combine(Environment.GetEnvironmentVariable("REXPLAYER_MAC_APP")!, "Contents", "MacOS", "rexplayer"))
        {
            UseShellExecute = false,
        };
        start.Environment["REXPLAYER_ROOT"] = root;
        start.Environment["REXPLAYER_FAKE_AUDIO"] = "1";
        start.Environment["REXPLAYER_PIPE_NAME"] = pipe;
        if (queueTests)
        {
            start.Environment["REXPLAYER_MAC_QUEUE_TESTS"] = "1";
        }

        return Process.Start(start)!;
    }

    /// <summary>Waits for the app to answer, then closes What's New so only the player window shows.</summary>
    private static async Task ReadyForALook(string pipe, Process app, string root)
    {
        await Until(() => Ask(pipe, new AutomationRequest { Command = "status" }), reply => reply is { Ok: true }, app, root);
        var whatsNew = await Ask(pipe, new AutomationRequest { Command = "mac-startup-guidance", Id = "done-whats-new" });
        Assert.Contains("whats=False", whatsNew?.Text, StringComparison.Ordinal);
    }

    /// <summary>
    /// Pauses what plays and waits until it is paused. A pause sent while a repeat reopens the
    /// item can be lost, so it is sent again while the app still reports playing.
    /// </summary>
    private static Task PauseWhatPlays(string pipe, Process app, string root) =>
        Until(async () =>
        {
            var now = await Ask(pipe, new AutomationRequest { Command = "status" });
            if (now is { State: "Playing" })
            {
                await Ask(pipe, new AutomationRequest { Command = "run", Id = CommandCatalog.PlayPause });
            }

            return now;
        }, reply => reply is { State: "Paused" }, app, root);

    /// <summary>Saves the whole player window as name.png where CI keeps the look screenshots.</summary>
    private static async Task SaveLookScreenshot(string pipe, string name, string root)
    {
        var directory = Environment.GetEnvironmentVariable("REXPLAYER_MAC_LOOK_SCREENSHOTS") ?? root;
        var file = Path.Combine(directory, name + ".png");
        var reply = await Ask(pipe, new AutomationRequest { Command = "ui.snapshot", Path = file });
        Assert.True(reply?.Ok == true, "Capturing the " + name + " screenshot failed: " + (reply?.Error ?? "no reply") + LogTail(root));
        Assert.True(File.Exists(file), "The player window did not save its " + name + " screenshot.");
        var bytes = await File.ReadAllBytesAsync(file, TestContext.Current.CancellationToken);
        Assert.True(bytes.Length > 1000, "The " + name + " screenshot is too small to show a window: " + bytes.Length + " bytes.");
        Assert.Equal(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, bytes.Take(8).ToArray());
    }

    private static async Task QuitLookApp(string pipe, Process app, string root)
    {
        var quit = await Ask(pipe, new AutomationRequest { Command = "run", Id = CommandCatalog.Quit });
        Assert.True(quit?.Ok == true || (app.WaitForExit(TimeSpan.FromSeconds(5)) && app.ExitCode == 0),
            "The app neither acknowledged quit nor exited cleanly: " + (quit?.Error ?? "no reply") + LogTail(root));
        Assert.True(app.WaitForExit(Patience), "rexplayer.app did not quit when asked.");
        Assert.Equal(0, app.ExitCode);
    }

    private static void EndLookApp(Process app, string root)
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
