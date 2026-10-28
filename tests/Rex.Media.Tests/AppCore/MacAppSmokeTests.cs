using System.Diagnostics;
using System.IO.Compression;
using Rex.Media.AppCore.Automation;
using Rex.Media.AppCore.Commands;
using Rex.Media.AppCore.Platform;
using Rex.Media.Library;
using Rex.Media.Settings;
using Rex.Media.TestKit;

namespace Rex.Media.Tests.AppCore;

/// <summary>
/// The real rexplayer.app, started and driven through its automation pipe as a script would
/// (TOOL-03), the way the Windows window suite drives rexplayer.exe: it must come up, open and play
/// a video, and quit cleanly when asked. Only on a Mac, and only when REXPLAYER_MAC_APP names a
/// built bundle (the macOS app job sets it); elsewhere it says why it did not run.
/// </summary>
[System.Runtime.Versioning.SupportedOSPlatform("macos")]
public sealed partial class MacAppSmokeTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);
    private const string MacAppRequired = "Set REXPLAYER_MAC_APP to a built rexplayer.app on a Mac to drive it.";

    // xUnit evaluates this gate before invoking a Mac-only test body.
    public static bool CanDriveMacApp => OperatingSystem.IsMacOS() &&
        !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("REXPLAYER_MAC_APP"));

    [Fact(Skip = MacAppRequired, SkipUnless = nameof(CanDriveMacApp))]
    [Capability("TOOL-03", CapabilityPlatform.MacOS)]
    [Capability("TOOL-06", CapabilityPlatform.MacOS)]
    [Capability("TOOL-10", CapabilityPlatform.MacOS)]
    [Capability("UI-09", CapabilityPlatform.MacOS)]
    [Capability("UI-05", CapabilityPlatform.MacOS)]
    [Capability("SUB-16", CapabilityPlatform.MacOS)]
    [Capability("OSD-04", CapabilityPlatform.MacOS)]
    [Capability("OSD-01", CapabilityPlatform.MacOS)]
    [Capability("OSD-03", CapabilityPlatform.MacOS)]
    [Capability("A11Y-02", CapabilityPlatform.MacOS)]
    [Capability("A11Y-01", CapabilityPlatform.MacOS)]
    [Capability("PB-05", CapabilityPlatform.MacOS)]
    [Capability("PB-10", CapabilityPlatform.MacOS)]
    [Capability("PB-11", CapabilityPlatform.MacOS)]
    [Capability("PB-16", CapabilityPlatform.MacOS)]
    [Capability("AU-06", CapabilityPlatform.MacOS)]
    [Capability("AU-15", CapabilityPlatform.MacOS)]
    [Capability("LIB-04", CapabilityPlatform.MacOS)]
    [Capability("LIB-08", CapabilityPlatform.MacOS)]
    [Capability("UI-15", CapabilityPlatform.MacOS)]
    [Capability("UI-14", CapabilityPlatform.MacOS)]
    [Capability("META-09", CapabilityPlatform.MacOS)]
    [Capability("AU-19", CapabilityPlatform.MacOS)]
    [Capability("UI-11", CapabilityPlatform.MacOS)]
    [Capability("LIB-03", CapabilityPlatform.MacOS)]
    [Capability("LIB-10", CapabilityPlatform.MacOS)]
    [Capability("LIB-04", CapabilityPlatform.MacOS)]
    [Capability("PB-12", CapabilityPlatform.MacOS)]
    [Capability("VID-03", CapabilityPlatform.MacOS)]
    [Capability("VID-27", CapabilityPlatform.MacOS)]
    [Capability("VID-07", CapabilityPlatform.MacOS)]
    [Capability("OSD-11", CapabilityPlatform.MacOS)]
    [Capability("OSD-07", CapabilityPlatform.MacOS)]
    [Capability("VID-26", CapabilityPlatform.MacOS)]
    [Capability("SUB-18", CapabilityPlatform.MacOS)]
    [Capability("SUB-19", CapabilityPlatform.MacOS)]
    [Capability("SUB-20", CapabilityPlatform.MacOS)]
    [Capability("SUB-21", CapabilityPlatform.MacOS)]
    [Capability("AU-13", CapabilityPlatform.MacOS)]
    [Capability("AU-07", CapabilityPlatform.MacOS)]
    [Capability("AU-11", CapabilityPlatform.MacOS)]
    [Capability("VID-05", CapabilityPlatform.MacOS)]
    [Capability("VID-04", CapabilityPlatform.MacOS)]
    [Capability("WIN-14", CapabilityPlatform.MacOS)]
    [Capability("VID-31", CapabilityPlatform.MacOS)]
    [Capability("LIB-09", CapabilityPlatform.MacOS)]
    [Capability("LIB-05", CapabilityPlatform.MacOS)]
    [Capability("LIB-02", CapabilityPlatform.MacOS)]
    [Capability("UI-04", CapabilityPlatform.MacOS)]
    [Capability("UI-01", CapabilityPlatform.MacOS)]
    [Capability("AU-18", CapabilityPlatform.MacOS)]
    [Capability("UI-16", CapabilityPlatform.MacOS)]
    public async Task TheMacAppComesUpPlaysAVideoAndQuitsWhenAScriptAsks()
    {
        var bundle = Environment.GetEnvironmentVariable("REXPLAYER_MAC_APP");
        var root = Path.Combine(Path.GetTempPath(), "rexplayer-mac-" + Guid.NewGuid().ToString("N")[..8]);
        var startupRoots = new DataRoots(root);
        SettingsStore.Save(startupRoots.SettingsPath, new PlayerSettings
        {
            FirstRunDone = true,
            LastSeenVersion = "0.0.0",
        });
        var pipe = "rexplayer-mac-" + Guid.NewGuid().ToString("N")[..8];
        var screenshots = RepoPaths.Combine("artifacts", "mac-queue-screenshots");
        Directory.CreateDirectory(screenshots);
        var libraryScreenshots = RepoPaths.Combine("artifacts", "mac-library-screenshots");
        Directory.CreateDirectory(libraryScreenshots);
        var libraryFixture = Path.Combine(root, "watched-media");
        Directory.CreateDirectory(libraryFixture);
        File.Copy(RepoPaths.Combine("tests", "fixtures", "mp3", "stereo-44k-128k-cbr.mp3"),
            Path.Combine(libraryFixture, "Sample Song.mp3"));
        File.Copy(RepoPaths.Combine("tests", "fixtures", "picture", "still.png"),
            Path.Combine(libraryFixture, "cover.png"));
        File.WriteAllText(Path.Combine(libraryFixture, "Sample Song.lrc"),
            "[00:00.00]First local lyric\n[00:01.00]Second local lyric\n");
        File.Copy(RepoPaths.Combine("tests", "fixtures", "mp4", "h264-aac.mp4"),
            Path.Combine(libraryFixture, "Sample Film.mp4"));
        var start = new ProcessStartInfo(Path.Combine(bundle!, "Contents", "MacOS", "rexplayer"))
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        start.Environment["REXPLAYER_ROOT"] = root;
        start.Environment["REXPLAYER_FAKE_AUDIO"] = "1";
        start.Environment["REXPLAYER_PIPE_NAME"] = pipe;
        start.Environment["REXPLAYER_MAC_QUEUE_TESTS"] = "1";
        start.Environment["REXPLAYER_MAC_SHORTCUT_TESTS"] = "1";
        start.Environment["REXPLAYER_MAC_PLAYLIST_TESTS"] = "1";
        start.Environment["REXPLAYER_MAC_QUEUE_SCREENSHOTS"] = screenshots;
        start.Environment["REXPLAYER_MAC_LIBRARY_TESTS"] = "1";
        start.Environment["REXPLAYER_MAC_LIBRARY_SCREENSHOTS"] = libraryScreenshots;
        using var app = Process.Start(start)!;
        // Native runtime assertions happen before the managed logger exists. Drain both pipes
        // immediately so a noisy startup cannot block, and include them if the smoke test fails.
        var stdout = app.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        var stderr = app.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        try
        {
            var up = await Until(() => Ask(pipe, new AutomationRequest { Command = "status" }), reply => reply is { Ok: true }, app, root);
            Assert.Equal("Idle", up.State);
            var whatsNew = await Ask(pipe, new AutomationRequest { Command = "mac-startup-guidance", Id = "status" });
            Assert.Contains("first=False;whats=True", whatsNew?.Text, StringComparison.Ordinal);
            Assert.Contains("seen=0.0.0", whatsNew?.Text, StringComparison.Ordinal);
            var dismissedWhatsNew = await Ask(pipe, new AutomationRequest { Command = "mac-startup-guidance", Id = "done-whats-new" });
            Assert.Contains("whats=False", dismissedWhatsNew?.Text, StringComparison.Ordinal);
            Assert.DoesNotContain("seen=0.0.0", dismissedWhatsNew?.Text, StringComparison.Ordinal);
            var notShownTwice = await Ask(pipe, new AutomationRequest { Command = "mac-startup-guidance", Id = "check-again" });
            Assert.Contains("whats=False", notShownTwice?.Text, StringComparison.Ordinal);
            var firstRun = await Ask(pipe, new AutomationRequest { Command = "mac-startup-guidance", Id = "show-first-run" });
            Assert.Contains("first=True", firstRun?.Text, StringComparison.Ordinal);
            Assert.Contains("metadataChoice=False", firstRun?.Text, StringComparison.Ordinal);
            Assert.Contains("updateChoice=True", firstRun?.Text, StringComparison.Ordinal);
            Assert.Contains("associations=True", firstRun?.Text, StringComparison.Ordinal);
            await Ask(pipe, new AutomationRequest { Command = "mac-startup-guidance", Id = "enable-metadata" });
            await Ask(pipe, new AutomationRequest { Command = "mac-startup-guidance", Id = "disable-updates" });
            var firstRunDone = await Ask(pipe, new AutomationRequest { Command = "mac-startup-guidance", Id = "finish-first-run" });
            Assert.Contains("first=False", firstRunDone?.Text, StringComparison.Ordinal);
            Assert.Contains("metadata=True", firstRunDone?.Text, StringComparison.Ordinal);
            Assert.Contains("updates=Off", firstRunDone?.Text, StringComparison.Ordinal);
            var handedOverVideo = RepoPaths.Combine("tests", "fixtures", "mp4", "h264-aac.mp4");
            var secondStart = new ProcessStartInfo(Path.Combine(bundle!, "Contents", "MacOS", "rexplayer"))
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            secondStart.Environment["REXPLAYER_ROOT"] = root;
            secondStart.Environment["REXPLAYER_FAKE_AUDIO"] = "1";
            secondStart.Environment["REXPLAYER_PIPE_NAME"] = pipe;
            secondStart.ArgumentList.Add(handedOverVideo);
            using (var second = Process.Start(secondStart)!)
            {
                await second.WaitForExitAsync(TestContext.Current.CancellationToken).WaitAsync(Patience, TestContext.Current.CancellationToken);
                Assert.Equal(0, second.ExitCode);
            }

            var handedOver = await Until(
                () => Ask(pipe, new AutomationRequest { Command = "status" }),
                reply => reply is { Ok: true, Title: not null }, app, root);
            Assert.Contains("h264-aac", handedOver.Title, StringComparison.OrdinalIgnoreCase);
            Assert.False(app.HasExited, "The original player exited when the later launch handed over its file.");
            var mainKeyboard = await Ask(pipe, new AutomationRequest { Command = "mac-keyboard" });
            Assert.True(mainKeyboard?.Ok == true, "The Mac main-window keyboard loop failed: " +
                (mainKeyboard?.Error ?? "no reply") + LogTail(root));
            Assert.Equal("tab=16;regions=3", mainKeyboard!.Text);
            var logShown = await Ask(pipe, new AutomationRequest { Command = "mac-diagnostics", Id = "show" });
            Assert.True(logShown?.Ok == true, "The Mac log console did not open.");
            var logControls = await Ask(pipe, new AutomationRequest { Command = "mac-diagnostics", Id = "verify-controls" });
            Assert.True(logControls?.Ok == true, "The Mac log console controls are incomplete.");
            var diagnosticsPath = Path.Combine(root, "diagnostics.zip");
            var diagnostics = await Ask(pipe, new AutomationRequest { Command = "mac-diagnostics", Id = "bundle", Path = diagnosticsPath });
            Assert.True(diagnostics?.Ok == true && File.Exists(diagnosticsPath), "The Mac diagnostics bundle was not saved.");
            using (var archive = ZipFile.OpenRead(diagnosticsPath))
            {
                Assert.Contains(archive.Entries, entry => entry.FullName == "settings.json");
                Assert.Contains(archive.Entries, entry => entry.FullName == "system.txt");
            }

            var logCleared = await Ask(pipe, new AutomationRequest { Command = "mac-diagnostics", Id = "clear" });
            Assert.True(logCleared?.Ok == true, "The Mac log console did not clear its view.");
            // Bind and restore a Mac menu shortcut through the real native editor.
            foreach (var action in new[] { "show", "bind", "global", "reset" })
            {
                var shortcut = await Ask(pipe, new AutomationRequest { Command = "mac-shortcuts", Id = action });
                Assert.True(shortcut?.Ok == true, "Mac shortcut editor failed to " + action + LogTail(root));
            }

            // Native file and named-playlist surfaces use the same persisted model as the engine.
            var savedPlaylists = await Ask(pipe, new AutomationRequest { Command = "mac-playlists", Id = "show" });
            Assert.True(savedPlaylists?.Ok == true, "Native saved-playlist window did not open.");
            foreach (var (extension, marker) in new[]
            {
                ("m3u8", "#EXTM3U"), ("xspf", "<playlist"), ("pls", "[playlist]"),
            })
            {
                var playlistFile = Path.Combine(root, "exported-playlist." + extension);
                var saveFile = await Ask(pipe, new AutomationRequest
                {
                    Command = "mac-playlists",
                    Id = "save-file",
                    Path = playlistFile,
                });
                Assert.True(saveFile?.Ok == true && File.Exists(playlistFile),
                    $"Mac {extension} playlist export did not create a readable file.");
                var playlistText = File.ReadAllText(playlistFile);
                Assert.Contains(marker, playlistText, StringComparison.OrdinalIgnoreCase);
                Assert.Contains("h264-aac.mp4", playlistText, StringComparison.OrdinalIgnoreCase);
            }
            var createNamed = await Ask(pipe, new AutomationRequest { Command = "mac-playlists", Id = "create" });
            Assert.True(createNamed?.Ok == true, "Native named-playlist creation failed.");
            foreach (var action in new[] { "rename", "duplicate", "append", "play", "delete" })
            {
                var changed = await Ask(pipe, new AutomationRequest { Command = "mac-playlists", Id = action });
                Assert.True(changed?.Ok == true, "Native named-playlist action failed: " + action);
            }

            var emptyLibrary = await Library(pipe, "show");
            Assert.Contains("0 items", emptyLibrary.Title, StringComparison.Ordinal);
            await Library(pipe, "capture-empty");
            // The reviewed "empty" baseline must be taken now, before adding
            // a watched folder or resizing the AppKit library to minimum width.
            await CaptureLibraryScreenshot(pipe, "empty", root);
            await Library(pipe, "add-folder", libraryFixture);
            var populated = await Until(
                () => Ask(pipe, new AutomationRequest { Command = "mac-library", Id = "status" }),
                reply => reply?.Title?.Contains("3 items", StringComparison.Ordinal) == true,
                app, root);
            Assert.Contains("1 folder", populated.Title, StringComparison.Ordinal);
            await Library(pipe, "capture-populated");
            var searched = await Library(pipe, "search-song");
            Assert.Contains("1 item", searched.Title, StringComparison.Ordinal);
            var missing = await Library(pipe, "search-missing");
            Assert.Contains("0 items", missing.Title, StringComparison.Ordinal);
            Assert.Contains("Search: No fixture with this name", missing.Title, StringComparison.Ordinal);
            await Library(pipe, "capture-search-empty");
            var restored = await Library(pipe, "clear-search");
            Assert.Contains("3 items", restored.Title, StringComparison.Ordinal);
            var songs = await Library(pipe, "songs");
            Assert.Contains("1 item", songs.Title, StringComparison.Ordinal);
            var onlyVideos = await Library(pipe, "videos");
            Assert.Contains("1 item", onlyVideos.Title, StringComparison.Ordinal);
            var continueWatching = await Library(pipe, "continue");
            Assert.Contains("1 item", continueWatching.Title, StringComparison.Ordinal);
            await Library(pipe, "capture-videos");
            await Library(pipe, "verify-accessibility");
            await Library(pipe, "minimum");
            await Library(pipe, "capture-minimum");
            foreach (var name in new[] { "empty", "populated", "search-empty", "videos", "minimum" })
            {
                var file = new FileInfo(Path.Combine(libraryScreenshots, name + ".png"));
                Assert.True(file.Exists && file.Length > 1024,
                    "No usable native library screenshot: " + file.FullName);
            }
            Assert.Contains("not a finalized-release screenshot",
                File.ReadAllText(Path.Combine(libraryScreenshots, "provenance.txt")), StringComparison.Ordinal);

            var empty = await Queue(pipe, "show");
            Assert.Contains("0 items", empty.Title, StringComparison.Ordinal);
            await Queue(pipe, "verify-missing");
            await Queue(pipe, "verify-list-drop");
            await Queue(pipe, "capture-empty");

            // Exercise closing and reopening the real AppKit library through the
            // shared View command. The catalogue is populated at this point.
            // Library smoke actions have already shown the AppKit window.
            // Close it first so this shared toggle really tests opening it.
            var closePreviouslyOpenLibrary = await Ask(pipe, new AutomationRequest { Command = "run", Id = CommandCatalog.ToggleLibrary });
            Assert.True(closePreviouslyOpenLibrary?.Ok == true, "Could not close the previously opened native library.");
            var library = await Ask(pipe, new AutomationRequest { Command = "run", Id = CommandCatalog.ToggleLibrary });
            Assert.True(library?.Ok == true, "The Mac library command did not open: " + (library?.Error ?? "no reply") + LogTail(root));
            Assert.True((await Ask(pipe, new AutomationRequest { Command = "status" }))?.Ok == true, "The app stopped answering with its library open." + LogTail(root));
            var back = await Ask(pipe, new AutomationRequest { Command = "run", Id = CommandCatalog.ToggleLibrary });
            Assert.True(back?.Ok == true, "The Mac library did not close through its shared command: " + (back?.Error ?? "no reply") + LogTail(root));

            var video = RepoPaths.Combine("tests", "fixtures", "mp4", "h264-aac.mp4");
            var opened = await Ask(pipe, new AutomationRequest { Command = "open", Paths = [video] });
            Assert.True(opened?.Ok == true, "The app did not acknowledge the open request: " + (opened?.Error ?? "no reply") + ". Its log ends:" + LogTail(root));
            var playing = await Until(() => Ask(pipe, new AutomationRequest { Command = "status" }), reply => reply is { Title: not null, State: "Playing" or "Ended" or "Paused" }, app, root);
            Assert.False(string.IsNullOrWhiteSpace(playing.Title), "The app opened the video but reports no title.");
            await Ask(pipe, new AutomationRequest { Command = "mac-resume", Id = "offer" });
            var resumeOffer = await Until(
                () => Ask(pipe, new AutomationRequest { Command = "mac-resume", Id = "status" }),
                reply => reply?.Text?.Contains("mode=Ask;pending=15;visible=True", StringComparison.Ordinal) == true,
                app, root);
            Assert.Contains("remembered=15", resumeOffer.Text, StringComparison.Ordinal);
            var resumed = await Ask(pipe, new AutomationRequest { Command = "mac-resume", Id = "accept" });
            Assert.Contains("pending=none;visible=False", resumed?.Text, StringComparison.Ordinal);
            await Ask(pipe, new AutomationRequest { Command = "mac-resume", Id = "offer" });
            await Until(
                () => Ask(pipe, new AutomationRequest { Command = "mac-resume", Id = "status" }),
                reply => reply?.Text?.Contains("visible=True", StringComparison.Ordinal) == true,
                app, root);
            var startedOver = await Ask(pipe, new AutomationRequest { Command = "mac-resume", Id = "start-over" });
            Assert.Contains("pending=none;visible=False;remembered=none", startedOver?.Text, StringComparison.Ordinal);
            var neverResume = await Ask(pipe, new AutomationRequest { Command = "mac-resume", Id = "never" });
            Assert.Contains("mode=Never;pending=none;visible=False", neverResume?.Text, StringComparison.Ordinal);
            var alwaysResume = await Ask(pipe, new AutomationRequest { Command = "mac-resume", Id = "always" });
            Assert.Contains("mode=Always;pending=none;visible=False", alwaysResume?.Text, StringComparison.Ordinal);
            var audioBeforeMute = await Ask(pipe, new AutomationRequest { Command = "mac-audio" });
            var mute = await Ask(pipe, new AutomationRequest { Command = "run", Id = CommandCatalog.Mute });
            Assert.True(mute?.Ok == true, "The Mac mute command was not handled.");
            var audioMuted = await Ask(pipe, new AutomationRequest { Command = "mac-audio" });
            Assert.Contains("muted=True", audioMuted?.Text, StringComparison.Ordinal);
            Assert.Equal(audioBeforeMute?.Text?.Replace("muted=False", "muted=True", StringComparison.Ordinal), audioMuted?.Text);
            await Ask(pipe, new AutomationRequest { Command = "run", Id = CommandCatalog.Mute });
            var audioRestored = await Ask(pipe, new AutomationRequest { Command = "mac-audio" });
            Assert.Equal(audioBeforeMute?.Text, audioRestored?.Text);
            var sought = await Ask(pipe, new AutomationRequest { Command = "seek", To = 0.25 });
            Assert.True(sought?.Ok == true && sought.Position is >= 0.2 and <= 0.5,
                "The Mac did not seek to the requested playback time: " +
                (sought?.Error ?? "unexpected position"));
            var slotSaved = await Ask(pipe, new AutomationRequest { Command = "mac-quick-slots", Id = "save-one" });
            Assert.Contains("h264-aac", slotSaved?.Text, StringComparison.OrdinalIgnoreCase);
            await Ask(pipe, new AutomationRequest { Command = "seek", To = 0 });
            var slotOpened = await Ask(pipe, new AutomationRequest { Command = "mac-quick-slots", Id = "open-one" });
            Assert.True(slotOpened?.Position is >= 0.2 and <= 0.5,
                "The native quick slot did not reopen its saved playback position.");
            var bookmarkAdded = await Ask(pipe, new AutomationRequest { Command = "mac-bookmarks", Id = "add" });
            Assert.Equal("Mac moment", bookmarkAdded?.Text);
            var bookmarkRenamed = await Ask(pipe, new AutomationRequest { Command = "mac-bookmarks", Id = "rename" });
            Assert.Equal("Mac renamed moment", bookmarkRenamed?.Text);
            await Ask(pipe, new AutomationRequest { Command = "seek", To = 0 });
            var bookmarkJumped = await Ask(pipe, new AutomationRequest { Command = "mac-bookmarks", Id = "jump" });
            Assert.True(bookmarkJumped?.Position is >= 0.2 and <= 0.5,
                "The native bookmark did not seek back to its saved position.");
            var bookmarkRemoved = await Ask(pipe, new AutomationRequest { Command = "mac-bookmarks", Id = "remove" });
            Assert.Equal("", bookmarkRemoved?.Text);
            var slow = await Ask(pipe, new AutomationRequest { Command = "mac-pitch-speed", Id = "minimum" });
            Assert.Contains("speed=0.25;preserve=True", slow?.Text, StringComparison.Ordinal);
            var chipmunk = await Ask(pipe, new AutomationRequest { Command = "mac-pitch-speed", Id = "toggle-pitch" });
            Assert.Contains("speed=0.25;preserve=False", chipmunk?.Text, StringComparison.Ordinal);
            var fast = await Ask(pipe, new AutomationRequest { Command = "mac-pitch-speed", Id = "maximum" });
            Assert.Contains("speed=4;preserve=False", fast?.Text, StringComparison.Ordinal);
            await Ask(pipe, new AutomationRequest { Command = "mac-pitch-speed", Id = "toggle-pitch" });
            var snapshotFolder = Path.Combine(root, "snapshots");
            foreach (var (format, extension) in new[] { ("png", ".png"), ("jpeg", ".jpg"), ("bmp", ".bmp") })
            {
                var output = Path.Combine(snapshotFolder, "source-frame" + extension);
                var snapshot = await Ask(pipe, new AutomationRequest
                {
                    Command = "mac-snapshot",
                    Id = format,
                    Path = output,
                });
                Assert.True(snapshot?.Ok == true && new FileInfo(output).Length > 64,
                    "The native Mac snapshot export failed for " + format + ": " +
                    (snapshot?.Error ?? "no reply"));
                Assert.Contains("width=", snapshot!.Text, StringComparison.Ordinal);
            }
            Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 },
                (await File.ReadAllBytesAsync(Path.Combine(snapshotFolder, "source-frame.png"), TestContext.Current.CancellationToken))[..4]);
            Assert.Equal(new byte[] { 0xFF, 0xD8 },
                (await File.ReadAllBytesAsync(Path.Combine(snapshotFolder, "source-frame.jpg"), TestContext.Current.CancellationToken))[..2]);
            Assert.Equal("BM", System.Text.Encoding.ASCII.GetString(
                (await File.ReadAllBytesAsync(Path.Combine(snapshotFolder, "source-frame.bmp"), TestContext.Current.CancellationToken))[..2]));
            var droppedSubtitles = Path.Combine(root, "drop-test.srt");
            await File.WriteAllTextAsync(droppedSubtitles,
                "1\n00:00:00,000 --> 00:00:02,000\nDropped subtitle\n",
                TestContext.Current.CancellationToken);
            var dropped = await Ask(pipe, new AutomationRequest { Command = "mac-drop", Paths = [video, droppedSubtitles] });
            Assert.True(dropped?.Ok == true, "The Mac did not accept a media and subtitle drop.");
            var subtitleState = await Ask(pipe, new AutomationRequest { Command = "mac-subtitles" });
            Assert.Contains("tracks=1", subtitleState?.Text, StringComparison.Ordinal);
            Assert.Contains("primary=drop-test.srt", subtitleState?.Text, StringComparison.Ordinal);
            var secondSubtitles = Path.Combine(root, "second-drop-test.srt");
            await File.WriteAllTextAsync(secondSubtitles,
                "1\n00:00:00,000 --> 00:00:02,000\nSecond dropped subtitle\n",
                TestContext.Current.CancellationToken);
            await Ask(pipe, new AutomationRequest { Command = "mac-drop", Paths = [secondSubtitles] });
            var dualSubtitles = await Ask(pipe, new AutomationRequest
            {
                Command = "mac-subtitles",
                Id = "select-secondary",
            });
            Assert.Contains("tracks=2", dualSubtitles?.Text, StringComparison.Ordinal);
            Assert.DoesNotContain("secondary=off", dualSubtitles?.Text, StringComparison.Ordinal);
            Assert.Contains("primaryPlacement=Bottom", dualSubtitles?.Text, StringComparison.Ordinal);
            Assert.Contains("secondaryPlacement=Top", dualSubtitles?.Text, StringComparison.Ordinal);
            var styledSubtitles = await Ask(pipe, new AutomationRequest
            {
                Command = "mac-subtitle-style",
                Id = "configure",
            });
            Assert.True(styledSubtitles?.Ok == true, styledSubtitles?.Error ?? "No subtitle-style reply.");
            Assert.Contains("font=Helvetica Neue;size=150;colour=F0E0D0;opacity=80", styledSubtitles!.Text, StringComparison.Ordinal);
            Assert.Contains("bold=True;outline=Thick;outlineColour=102030", styledSubtitles.Text, StringComparison.Ordinal);
            Assert.Contains("shadow=203040/55/4;box=304050/35", styledSubtitles.Text, StringComparison.Ordinal);
            Assert.Contains("margin=12;bars=True;forceBottom=False", styledSubtitles.Text, StringComparison.Ordinal);
            var titled = await Ask(pipe, new AutomationRequest { Command = "mac-window" });
            Assert.Contains(playing.Title, titled?.Text, StringComparison.Ordinal);
            Assert.Contains("minimal=False", titled?.Text, StringComparison.Ordinal);
            Assert.Contains("context=33", titled?.Text, StringComparison.Ordinal);
            var mainWindow = await Ask(pipe, new AutomationRequest { Command = "mac-main-window" });
            Assert.True(mainWindow?.Ok == true && mainWindow.Text?.Contains(
                "timeline=elapsed,remaining,chapters,buffer,preview", StringComparison.Ordinal) == true,
                "The Mac main window is incomplete: " + (mainWindow?.Error ?? "no reply"));
            var magnified = await Ask(pipe, new AutomationRequest
            {
                Command = "mac-video-geometry",
                Id = "magnify-and-pan",
            });
            Assert.Contains("zoom=2.00;pan=0.25,-0.50", magnified?.Text, StringComparison.Ordinal);
            var resetGeometry = await Ask(pipe, new AutomationRequest
            {
                Command = "mac-video-geometry",
                Id = "reset",
            });
            Assert.Contains("zoom=1.00;pan=0.00,0.00", resetGeometry?.Text, StringComparison.Ordinal);
            var customAspect = await Ask(pipe, new AutomationRequest
            {
                Command = "mac-video-geometry",
                Id = "custom",
            });
            Assert.Contains("aspect=2.333", customAspect?.Text, StringComparison.Ordinal);
            var autoSize = await Ask(pipe, new AutomationRequest
            {
                Command = "mac-window-sizing",
                Id = "configure",
            });
            Assert.Equal("automatic=True;scale=0.50", autoSize?.Text);
            var largerWindow = await Ask(pipe, new AutomationRequest
            {
                Command = "mac-window-sizing",
                Id = "larger",
            });
            Assert.Equal("automatic=True;scale=1.00", largerWindow?.Text);
            var savedWindowSize = SettingsStore.Load(startupRoots.SettingsPath);
            Assert.True(savedWindowSize.ResizeWindowToVideo);
            Assert.Equal(1, savedWindowSize.VideoWindowScale);
            var crashRecovery = await Ask(pipe, new AutomationRequest
            {
                Command = "mac-crash-recovery",
                Id = "enable-dumps",
            });
            Assert.Contains("handlers=True;resumeMarker=True;dumps=True;runtime=1;local=True",
                crashRecovery?.Text, StringComparison.Ordinal);
            Assert.True(SettingsStore.Load(startupRoots.SettingsPath).LocalCrashDumps);
            await Ask(pipe, new AutomationRequest { Command = "mac-stats", Id = "show" });
            var stats = await Until(
                () => Ask(pipe, new AutomationRequest { Command = "mac-stats", Id = "status" }),
                reply => reply?.Text?.Contains("Buffers   audio", StringComparison.Ordinal) == true,
                app, root);
            Assert.Contains("Decoder", stats.Text, StringComparison.Ordinal);
            Assert.Contains("Pictures", stats.Text, StringComparison.Ordinal);
            Assert.Contains("Colour", stats.Text, StringComparison.Ordinal);
            Assert.Contains("Buffers", stats.Text, StringComparison.Ordinal);
            var kinetic = await Ask(pipe, new AutomationRequest
            {
                Command = "mac-kinetic-captions",
                Id = "configure",
            });
            Assert.Equal("on=True;preset=Custom;energy=175;words=SubtitlesTimedByTranscript;" +
                "layout=Stairs;palette=Ocean;accent=12A4E0;glyphs=True", kinetic?.Text);
            var savedKinetic = SettingsStore.Load(startupRoots.SettingsPath).KineticCaptions;
            Assert.Equal((true, CaptionPreset.Custom, 175, CaptionWordSource.SubtitlesTimedByTranscript,
                CaptionLayoutChoice.Stairs, CaptionColors.Ocean, 0x12A4E0, true),
                (savedKinetic.On, savedKinetic.Preset, savedKinetic.Energy, savedKinetic.Words,
                    savedKinetic.Layout, savedKinetic.Colors, savedKinetic.Accent, savedKinetic.Glyphs));
            var osd = await Ask(pipe, new AutomationRequest { Command = "mac-osd", Id = "configure" });
            Assert.Equal("enabled=True;duration=4.5;position=Bottom;title=6.0;" +
                "overlay=Playback message proof", osd?.Text);
            var savedOsd = SettingsStore.Load(startupRoots.SettingsPath);
            Assert.Equal((true, 4.5, OnScreenMessagePosition.Bottom, 6.0),
                (savedOsd.OnScreenMessages, savedOsd.OnScreenMessageSeconds,
                    savedOsd.OnScreenMessagePosition, savedOsd.TitleSeconds));
            var disabledOsd = await Ask(pipe, new AutomationRequest { Command = "mac-osd", Id = "disable" });
            Assert.Contains("enabled=False", disabledOsd?.Text, StringComparison.Ordinal);
            Assert.EndsWith("overlay=", disabledOsd?.Text, StringComparison.Ordinal);
            var speech = await Ask(pipe, new AutomationRequest { Command = "mac-speech", Id = "configure" });
            Assert.Contains("visible=True;model=base;library=True;playing=True;paused=True", speech?.Text,
                StringComparison.Ordinal);
            var savedSpeech = SettingsStore.Load(startupRoots.SettingsPath);
            Assert.Equal((TranscriptModelChoice.Base, true, true, true),
                (savedSpeech.KineticCaptions.TranscriptModel, savedSpeech.Transcription.Library,
                    savedSpeech.Transcription.WhatPlays, savedSpeech.Transcription.Paused));
            var resumedSpeech = await Ask(pipe, new AutomationRequest { Command = "mac-speech", Id = "resume" });
            Assert.Contains("paused=False", resumedSpeech?.Text, StringComparison.Ordinal);
            var loudness = await Ask(pipe, new AutomationRequest { Command = "mac-loudness", Id = "configure" });
            Assert.Equal("mode=Album;preamp=3.5;fallback=-7.0;protect=True", loudness?.Text);
            var savedLoudness = SettingsStore.Load(startupRoots.SettingsPath);
            Assert.Equal((LoudnessChoice.Album, 3.5, -7.0, true),
                (savedLoudness.Loudness, savedLoudness.LoudnessPreamp,
                    savedLoudness.LoudnessUntaggedGain, savedLoudness.LoudnessPreventClipping));
            var equalizer = await Ask(pipe, new AutomationRequest { Command = "mac-equalizer", Id = "configure" });
            Assert.Equal("enabled=True;preamp=2.0;double=True;bands=-5,-4,-3,-2,-1,0,1,2,3,4;users=1",
                equalizer?.Text);
            var savedEqualizer = SettingsStore.Load(startupRoots.SettingsPath);
            Assert.True(savedEqualizer.EqualizerDoublePass);
            Assert.Equal("Mac listening room", savedEqualizer.EqualizerPreset);
            Assert.Equal([-5d, -4, -3, -2, -1, 0, 1, 2, 3, 4],
                savedEqualizer.EqualizerUserPresets["Mac listening room"]);
            var headphones = await Ask(pipe, new AutomationRequest { Command = "mac-stereo", Id = "headphones" });
            Assert.Equal("mode=Headphones", headphones?.Text);
            var matrix = await Ask(pipe, new AutomationRequest { Command = "mac-stereo", Id = "matrix" });
            Assert.Equal("mode=MatrixSurround", matrix?.Text);
            Assert.Equal(StereoChoice.MatrixSurround,
                SettingsStore.Load(startupRoots.SettingsPath).Stereo);
            var minimal = await Ask(pipe, new AutomationRequest { Command = "run", Id = CommandCatalog.MinimalInterface });
            Assert.True(minimal?.Ok == true, "The Mac minimal-interface command was not handled.");
            var hiddenChrome = await Ask(pipe, new AutomationRequest { Command = "mac-window" });
            Assert.Contains("minimal=True", hiddenChrome?.Text, StringComparison.Ordinal);
            Assert.Contains("context=33", hiddenChrome?.Text, StringComparison.Ordinal);
            await Ask(pipe, new AutomationRequest { Command = "run", Id = CommandCatalog.MinimalInterface });
            await Ask(pipe, new AutomationRequest { Command = "run", Id = CommandCatalog.ToggleAlwaysOnTop });
            var floating = await Ask(pipe, new AutomationRequest { Command = "mac-window" });
            Assert.Contains("floating=True", floating?.Text, StringComparison.Ordinal);
            await Ask(pipe, new AutomationRequest { Command = "run", Id = CommandCatalog.ToggleAlwaysOnTop });
            await Ask(pipe, new AutomationRequest { Command = "run", Id = CommandCatalog.ToggleFullScreen });
            await Until(
                () => Ask(pipe, new AutomationRequest { Command = "mac-window" }),
                reply => reply?.Text?.Contains("fullscreen=True", StringComparison.Ordinal) == true &&
                    reply.Text.Contains("context=33", StringComparison.Ordinal),
                app, root);
            var fullScreenController = await Ask(pipe, new AutomationRequest { Command = "mac-fullscreen-controller" });
            Assert.True(fullScreenController?.Ok == true &&
                fullScreenController.Text?.Contains("hide=1.5", StringComparison.Ordinal) == true,
                "The Mac full-screen controller is incomplete: " + (fullScreenController?.Error ?? "no reply"));
            await Ask(pipe, new AutomationRequest { Command = "run", Id = CommandCatalog.LeaveFullScreen });
            await Queue(pipe, "capture-current");
            // The short video fixture can finish before the queue interactions start.
            // Stop its session so synthetic (intentionally nonexistent) queue entries
            // are never opened by automatic next-item playback during the UI smoke test.
            var stopped = await Ask(pipe, new AutomationRequest { Command = "run", Id = "stop" });
            Assert.True(stopped?.Ok == true, "The app did not acknowledge the stop request.");
            var longQueue = await Queue(pipe, "fill-long");
            Assert.Contains("91 items", longQueue.Title, StringComparison.Ordinal);
            await Queue(pipe, "capture-long");
            var page2 = await Queue(pipe, "next-page");
            Assert.Contains("viewing 76", page2.Title, StringComparison.Ordinal);
            await Queue(pipe, "capture-page-2");
            await Queue(pipe, "previous-page");
            var promoted = await Queue(pipe, "last-play-next");
            Assert.Contains("Next: Queue sample 074", promoted.Title, StringComparison.Ordinal);
            var shuffled = await Queue(pipe, "shuffle");
            Assert.Contains("Shuffle: On", shuffled.Title, StringComparison.Ordinal);
            var repeated = await Queue(pipe, "repeat");
            Assert.Contains("Repeat: All", repeated.Title, StringComparison.Ordinal);
            await Queue(pipe, "capture-shuffled");
            await Queue(pipe, "verify-accessibility");
            await Queue(pipe, "minimum");
            await Queue(pipe, "capture-minimum");
            var tail = await Queue(pipe, "next-page");
            Assert.Contains("viewing 76", tail.Title, StringComparison.Ordinal);
            var shrunk = await Queue(pipe, "remove-last-page");
            Assert.Contains("75 items", shrunk.Title, StringComparison.Ordinal);
            Assert.DoesNotContain("viewing 76", shrunk.Title, StringComparison.Ordinal);
            await Queue(pipe, "capture-shrunk");
            foreach (var name in new[] { "empty", "current", "long", "page-2", "shuffled", "minimum", "shrunk" })
            {
                var file = new FileInfo(Path.Combine(screenshots, name + ".png"));
                Assert.True(file.Exists && file.Length > 1024, "No usable native queue screenshot: " + file.FullName);
            }

            await Library(pipe, "show");
            await Library(pipe, "songs");
            await Library(pipe, "play-first");
            var fromLibrary = await Until(() => Ask(pipe, new AutomationRequest { Command = "status" }),
                reply => reply is { Title: not null, State: "Playing" or "Paused" or "Ended" }, app, root);
            Assert.False(string.IsNullOrWhiteSpace(fromLibrary.Title),
                "The native library row should have started actual local playback.");
            var lyricsShown = await Until(
                () => Ask(pipe, new AutomationRequest { Command = "mac-music" }),
                reply => reply?.Text?.Contains("visible=True;art=True;heading=Sample Song;lyrics=True", StringComparison.Ordinal) == true &&
                    reply.Text.Contains("local lyric", StringComparison.Ordinal),
                app, root);
            Assert.Contains("local lyric", lyricsShown.Text, StringComparison.Ordinal);
            await Ask(pipe, new AutomationRequest { Command = "run", Id = CommandCatalog.ToggleLyrics });
            var lyricsHidden = await Ask(pipe, new AutomationRequest { Command = "mac-music" });
            Assert.Contains("visible=True;art=True;heading=Sample Song;lyrics=False", lyricsHidden?.Text, StringComparison.Ordinal);
            await Ask(pipe, new AutomationRequest { Command = "run", Id = CommandCatalog.ToggleLyrics });
            await Ask(pipe, new AutomationRequest { Command = "run", Id = CommandCatalog.CycleVisualizer });
            var visualizerShown = await Ask(pipe, new AutomationRequest { Command = "mac-music" });
            Assert.Contains("visualizer=Meters", visualizerShown?.Text, StringComparison.Ordinal);
            var visualisers = await Ask(pipe, new AutomationRequest { Command = "mac-music", Id = "visualisers" });
            Assert.True(visualisers?.Ok == true && visualisers.Text?.Contains("camera=", StringComparison.Ordinal) == true,
                "The native visualisation panel is incomplete: " + (visualisers?.Error ?? "no reply"));

            await Library(pipe, "show");
            var folders = await Library(pipe, "folders");
            Assert.Contains("1 item", folders.Title, StringComparison.Ordinal);
            await Library(pipe, "capture-folders");
            var backupRoot = Path.Combine(root, "backups");
            Directory.CreateDirectory(backupRoot);
            var backup = await Ask(pipe, new AutomationRequest
            {
                Command = "mac-library-backup",
                Id = "export",
                Path = backupRoot,
            });
            Assert.True(backup?.Ok == true && File.Exists(backup.Text),
                "The native Mac app did not export a readable library backup: " + (backup?.Error ?? "no reply"));
            var cleared = await Library(pipe, "remove-folder");
            Assert.Contains("0 items", cleared.Title, StringComparison.Ordinal);
            Assert.Contains("0 folders", cleared.Title, StringComparison.Ordinal);
            await Library(pipe, "capture-cleared");
            Assert.True(File.Exists(Path.Combine(libraryFixture, "Sample Song.mp3")),
                "Removing a watched folder must never delete an audio file.");
            Assert.True(File.Exists(Path.Combine(libraryFixture, "Sample Film.mp4")),
                "Removing a watched folder must never delete a video file.");
            var libraryRestored = await Ask(pipe, new AutomationRequest
            {
                Command = "mac-library-backup",
                Id = "restore",
                Path = backup!.Text,
            });
            Assert.True(libraryRestored?.Ok == true, "The native Mac app did not restore its library backup: " +
                (libraryRestored?.Error ?? "no reply"));
            Assert.Contains("folders=1", libraryRestored!.Text, StringComparison.Ordinal);
            var restoredFolders = await Library(pipe, "folders");
            Assert.Contains("1 item", restoredFolders.Title, StringComparison.Ordinal);
            foreach (var name in new[] { "folders", "cleared" })
            {
                var file = new FileInfo(Path.Combine(libraryScreenshots, name + ".png"));
                Assert.True(file.Exists && file.Length > 1024,
                    "No usable native watched-folder screenshot: " + file.FullName);
            }

            var accessible = await Ask(pipe, new AutomationRequest { Command = "mac-accessibility" });
            Assert.True(accessible?.Ok == true, "A reachable native Mac control has no accessible name: " +
                (accessible?.Text ?? accessible?.Error ?? "no reply"));
            Assert.Contains("missing-labels=0", accessible!.Text, StringComparison.Ordinal);

            var quitting = await Ask(pipe, new AutomationRequest { Command = "run", Id = "quit" });
            // The app may exit before the transport can deliver its reply. A clean exit
            // is the stronger proof that the quit command succeeded.
            Assert.True(quitting?.Ok == true || (app.WaitForExit(TimeSpan.FromSeconds(5)) && app.ExitCode == 0),
                "The app neither acknowledged quit nor exited cleanly: " + (quitting?.Error ?? "no reply") + ". Its log ends:" + LogTail(root));
            Assert.True(app.WaitForExit(Patience), "rexplayer.app did not quit when asked.");
            Assert.Equal(0, app.ExitCode);
            Assert.Contains("rexplayer starting", await File.ReadAllTextAsync(Path.Combine(root, "logs", "rexplayer.log"), TestContext.Current.CancellationToken), StringComparison.Ordinal);
        }
        catch (Exception error)
        {
            if (!app.HasExited)
            {
                app.Kill(entireProcessTree: true);
            }

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await app.WaitForExitAsync(timeout.Token);
            throw new InvalidOperationException(
                "Mac app smoke test failed. App log:" + LogTail(root)
                + Environment.NewLine + "Native stdout:" + Environment.NewLine + await stdout.WaitAsync(timeout.Token)
                + Environment.NewLine + "Native stderr:" + Environment.NewLine + await stderr.WaitAsync(timeout.Token), error);
        }
        finally
        {
            if (!app.HasExited)
            {
                app.Kill();
            }

            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact(Skip = MacAppRequired, SkipUnless = nameof(CanDriveMacApp))]
    public async Task TheMacAppShowsASavedLibraryEntryInItsNativeWindow()
    {
        var bundle = Environment.GetEnvironmentVariable("REXPLAYER_MAC_APP");
        var root = Path.Combine(Path.GetTempPath(), "rexplayer-mac-library-" + Guid.NewGuid().ToString("N")[..8]);
        var pipe = "rexplayer-mac-" + Guid.NewGuid().ToString("N")[..8];
        var mediaFolder = Path.Combine(root, "watched");
        Directory.CreateDirectory(mediaFolder);
        var mediaPath = Path.Combine(mediaFolder, "known-video.mp4");
        File.Copy(RepoPaths.Combine("tests", "fixtures", "mp4", "h264-aac.mp4"), mediaPath);
        var secondFolder = Path.Combine(root, "watched-next");
        Directory.CreateDirectory(secondFolder);
        File.Copy(RepoPaths.Combine("tests", "fixtures", "mp4", "h264-aac.mp4"),
            Path.Combine(secondFolder, "another-video.mp4"));

        var roots = new DataRoots(root);
        var library = new Rex.Media.Library.MediaLibrary(RexStore.Open(roots.LibraryStore));
        Assert.True(library.AddFolder(mediaFolder));
        var result = library.Scan(mediaFolder,
            [new LibraryFile(mediaPath, new FileInfo(mediaPath).Length, File.GetLastWriteTimeUtc(mediaPath))],
            _ => LibraryKind.Video);
        Assert.Equal(1, result.Added);

        var start = new ProcessStartInfo(Path.Combine(bundle!, "Contents", "MacOS", "rexplayer")) { UseShellExecute = false };
        start.Environment["REXPLAYER_ROOT"] = root;
        start.Environment["REXPLAYER_FAKE_AUDIO"] = "1";
        start.Environment["REXPLAYER_PIPE_NAME"] = pipe;
        // QueueSmokeAction is gated behind this test-only opt-in.
        // The library Play/Queue smoke assertions inspect queue contents below.
        start.Environment["REXPLAYER_MAC_QUEUE_TESTS"] = "1";
        using var app = Process.Start(start)!;
        try
        {
            await Until(() => Ask(pipe, new AutomationRequest { Command = "status" }), reply => reply is { Ok: true }, app, root);
            // The library window constructs its result rows from the persisted catalogue.
            var opened = await Ask(pipe, new AutomationRequest { Command = "run", Id = CommandCatalog.ToggleLibrary });
            Assert.True(opened?.Ok == true, "Opening the populated Mac library failed: " + (opened?.Error ?? "no reply") + LogTail(root));
            Assert.True((await Ask(pipe, new AutomationRequest { Command = "status" }))?.Ok == true, "The populated Mac library stopped responding." + LogTail(root));
            await CaptureLibraryScreenshot(pipe, "populated", root);
            var keys = await Ask(pipe, new AutomationRequest { Command = "ui.keyboard" });
            Assert.True(keys?.Ok == true, "Mac library AppKit Tab/Shift-Tab loop failed: " + (keys?.Error ?? "no reply") + LogTail(root));
            var tree = await Ask(pipe, new AutomationRequest { Command = "ui.tree" });
            Assert.True(tree?.Ok == true && tree.Controls is not null, "The native library did not expose its controls.");
            Assert.Contains(tree!.Controls!, control => control.Id == "LibrarySummary" &&
                control.Name.StartsWith("Library status: ", StringComparison.Ordinal) &&
                control.Name.Contains("1 total", StringComparison.Ordinal));
            Assert.Contains(tree.Controls!, control => control.Id == "LibraryPage" &&
                control.Name == "Library results Page 1 of 1");
            Assert.Contains(tree!.Controls!, control => control.Id == "LibrarySearch" && control.Kind == "search" && control.Name == "Search library");
            Assert.Contains(tree.Controls!, control => control.Id == "LibraryCategory" && control.Name == "Media category");
            Assert.Contains(tree.Controls!, control => control.Id == "LibraryResults" && control.Name == "Library results");
            Assert.Contains(tree.Controls!, control => control.Id == "LibraryAddFolder" && control.Name == "Add a folder to the library" && control.Enabled);
            Assert.Contains(tree.Controls!, control => control.Id == "LibraryRescan" && control.Name == "Rescan watched library folders" && control.Enabled);
            Assert.Contains(tree.Controls!, control => control.Id == "LibraryBack" && control.Name == "Back to player" && control.Enabled);
            Assert.Contains(tree.Controls!, control => control.Id == "LibraryPrevious" && control.Enabled == false && control.Name == "Previous results page");
            Assert.Contains(tree.Controls!, control => control.Id == "LibraryNext" && control.Enabled == false && control.Name == "Next results page");
            var backFromLibrary = await Ask(pipe, new AutomationRequest { Command = "ui.press", Target = "LibraryBack" });
            Assert.True(backFromLibrary?.Ok == true, "The native Back to player button failed.");
            var hidden = await Ask(pipe, new AutomationRequest { Command = "ui.tree" });
            Assert.True(hidden?.Ok == false, "The library was still visible after Back to player.");
            var returnToLibrary = await Ask(pipe, new AutomationRequest { Command = "run", Id = CommandCatalog.ToggleLibrary });
            Assert.True(returnToLibrary?.Ok == true, "The library could not reopen after Back to player.");
            var music = await Ask(pipe, new AutomationRequest { Command = "ui.set", Target = "LibraryCategory", Value = 1 });
            Assert.True(music?.Ok == true && music.Text?.Contains("0 matching", StringComparison.Ordinal) == true, "The native Music category did not filter videos.");
            var videos = await Ask(pipe, new AutomationRequest { Command = "ui.set", Target = "LibraryCategory", Value = 2 });
            Assert.True(videos?.Ok == true && videos.Text?.Contains("1 matching", StringComparison.Ordinal) == true, "The native Videos category did not restore the video.");
            var pictures = await Ask(pipe, new AutomationRequest { Command = "ui.set", Target = "LibraryCategory", Value = 3 });
            Assert.True(pictures?.Ok == true && pictures.Text?.Contains("0 matching", StringComparison.Ordinal) == true,
                "The Pictures category must not contain the saved video.");
            var recentlyAdded = await Ask(pipe, new AutomationRequest { Command = "ui.set", Target = "LibraryCategory", Value = 4 });
            Assert.True(recentlyAdded?.Ok == true && recentlyAdded.Text?.Contains("1 matching", StringComparison.Ordinal) == true,
                "The Recently added category did not show the newly scanned video.");
            var recentlyPlayed = await Ask(pipe, new AutomationRequest { Command = "ui.set", Target = "LibraryCategory", Value = 5 });
            Assert.True(recentlyPlayed?.Ok == true && recentlyPlayed.Text?.Contains("0 matching", StringComparison.Ordinal) == true,
                "The unplayed video unexpectedly appeared in Recently played.");
            videos = await Ask(pipe, new AutomationRequest { Command = "ui.set", Target = "LibraryCategory", Value = 2 });
            Assert.True(videos?.Ok == true && videos.Text?.Contains("1 matching", StringComparison.Ordinal) == true,
                "The Videos category did not restore after switching all six categories.");
            var small = await Ask(pipe, new AutomationRequest { Command = "ui.set", Target = "LibraryWidth", Value = 590 });
            Assert.True(small?.Ok == true, "The native library could not use its minimum window size: " + (small?.Error ?? "no reply"));
            await CaptureLibraryScreenshot(pipe, "minimum-width", root);
            var searched = await Ask(pipe, new AutomationRequest { Command = "ui.search", Target = "known-video" });
            Assert.True(searched?.Ok == true && searched.Text?.Contains("1 matching", StringComparison.Ordinal) == true,
                "Searching the native library failed: " + (searched?.Text ?? searched?.Error ?? "no reply") + LogTail(root));
            await CaptureLibraryScreenshot(pipe, "searched", root);
            var noMatches = await Ask(pipe, new AutomationRequest { Command = "ui.search", Target = "no-such-video-name" });
            Assert.True(noMatches?.Ok == true && noMatches.Text?.Contains("0 matching", StringComparison.Ordinal) == true,
                "An unmatched native search did not clear the results: " + (noMatches?.Text ?? noMatches?.Error ?? "no reply") + LogTail(root));
            await CaptureLibraryScreenshot(pipe, "no-matches", root);

            // Invoke the actual AppKit result buttons, not PlayerController directly.
            // This exercises native Play -> player -> library -> Queue wiring.
            tree = await Ask(pipe, new AutomationRequest { Command = "ui.tree" });
            Assert.DoesNotContain(tree!.Controls!, control => control.Id == "LibraryFirstPlay" && control.Enabled);
            Assert.DoesNotContain(tree.Controls!, control => control.Id == "LibraryFirstQueue" && control.Enabled);
            var folderResult = await Ask(pipe, new AutomationRequest { Command = "ui.search", Target = "watched" });
            Assert.True(folderResult?.Ok == true && folderResult.Text?.Contains("1 matching", StringComparison.Ordinal) == true,
                "Native library search did not match the saved folder name.");
            var uppercaseResult = await Ask(pipe, new AutomationRequest { Command = "ui.search", Target = "KNOWN-VIDEO" });
            Assert.True(uppercaseResult?.Ok == true && uppercaseResult.Text?.Contains("1 matching", StringComparison.Ordinal) == true,
                "Native library search did not match a title case-insensitively.");
            var restoreResult = await Ask(pipe, new AutomationRequest { Command = "ui.search", Target = "known-video" });
            Assert.True(restoreResult?.Ok == true, "The native library did not restore its searched result.");
            tree = await Ask(pipe, new AutomationRequest { Command = "ui.tree" });
            Assert.Contains(tree!.Controls!, control => control.Id == "LibraryFirstPlay" && control.Name.StartsWith("Play: ", StringComparison.Ordinal));
            Assert.Contains(tree.Controls!, control => control.Id == "LibraryFirstQueue" && control.Name.StartsWith("Add ", StringComparison.Ordinal) && control.Name.EndsWith(" to Up Next", StringComparison.Ordinal));
            var playResult = await Ask(pipe, new AutomationRequest { Command = "ui.press", Target = "LibraryFirstPlay" });
            Assert.True(playResult?.Ok == true, "The native Play result button failed: " + (playResult?.Error ?? "no reply") + LogTail(root));
            var selected = await Until(() => Ask(pipe, new AutomationRequest { Command = "status" }),
                reply => reply is { Ok: true, Title: not null }, app, root);
            var reopen = await Ask(pipe, new AutomationRequest { Command = "run", Id = CommandCatalog.ToggleLibrary });
            Assert.True(reopen?.Ok == true, "Could not return to the library from playback.");
            // Recently played must track what actually opened through the shared player,
            // not only entries that were already marked played in the persisted fixture.
            await Until(() => Ask(pipe, new AutomationRequest { Command = "ui.set", Target = "LibraryCategory", Value = 5 }),
                reply => reply is { Ok: true, Text: not null } &&
                    reply.Text.Contains("1 matching", StringComparison.Ordinal), app, root);
            var restoreVideos = await Ask(pipe, new AutomationRequest
            {
                Command = "ui.set",
                Target = "LibraryCategory",
                Value = 2,
            });
            Assert.True(restoreVideos?.Ok == true &&
                restoreVideos.Text?.Contains("1 matching", StringComparison.Ordinal) == true,
                "Returning from Recently played did not restore the queueable video result.");
            var queueResult = await Ask(pipe, new AutomationRequest { Command = "ui.press", Target = "LibraryFirstQueue" });
            Assert.True(queueResult?.Ok == true, "The native Queue result button failed: " + (queueResult?.Error ?? "no reply") + LogTail(root));
            var afterQueue = await Ask(pipe, new AutomationRequest { Command = "status" });
            Assert.Equal(selected.Title, afterQueue?.Title);
            var queued = await Queue(pipe, "show");
            Assert.Contains("2 items", queued.Title, StringComparison.Ordinal);

            // Exercise the same persisted add-and-scan path as the native Add folder picker.
            // A new folder is not preseeded in RexStore; the real running app must save it.
            var watched = await Ask(pipe, new AutomationRequest
            {
                Command = "ui.add-folder",
                Paths = [secondFolder],
            });
            Assert.True(watched?.Ok == true, "The Mac did not register the second watched folder: " +
                (watched?.Error ?? "no reply") + LogTail(root));
            await Until(() => Ask(pipe, new AutomationRequest { Command = "ui.text", Target = "LibrarySummary" }),
                reply => reply is { Ok: true, Text: not null } &&
                    reply.Text.Contains("2 folders", StringComparison.Ordinal) &&
                    reply.Text.Contains("2 total", StringComparison.Ordinal), app, root);
            var newResult = await Ask(pipe, new AutomationRequest { Command = "ui.search", Target = "another-video" });
            Assert.True(newResult?.Ok == true && newResult.Text?.Contains("1 matching", StringComparison.Ordinal) == true,
                "The scanner did not expose the newly added video: " + (newResult?.Text ?? newResult?.Error ?? "no reply"));

            // The watched folder remains active, so the native Rescan button must discover
            // changes made after the folder was first indexed.
            File.Copy(RepoPaths.Combine("tests", "fixtures", "mp4", "h264-aac.mp4"),
                Path.Combine(secondFolder, "rescanned-video.mp4"));
            var rescan = await Ask(pipe, new AutomationRequest { Command = "ui.press", Target = "LibraryRescan" });
            Assert.True(rescan?.Ok == true, "The native Rescan action did not run: " + (rescan?.Error ?? "no reply"));
            await Until(() => Ask(pipe, new AutomationRequest { Command = "ui.text", Target = "LibrarySummary" }),
                reply => reply is { Ok: true, Text: not null } &&
                    reply.Text.Contains("3 total", StringComparison.Ordinal), app, root);
            var rescanned = await Ask(pipe, new AutomationRequest { Command = "ui.search", Target = "rescanned-video" });
            Assert.True(rescanned?.Ok == true && rescanned.Text?.Contains("1 matching", StringComparison.Ordinal) == true,
                "The native Rescan action did not reveal the new file.");

            // A file can disappear between a scan and the user pressing Queue. The
            // stale result must not be enqueued, and the native scanner should purge it.
            File.Delete(Path.Combine(secondFolder, "rescanned-video.mp4"));
            var missingQueue = await Ask(pipe, new AutomationRequest { Command = "ui.press", Target = "LibraryFirstQueue" });
            Assert.True(missingQueue?.Ok == true, "Could not activate the stale native Queue row.");
            var afterMissing = await Queue(pipe, "show");
            Assert.Contains("2 items", afterMissing.Title, StringComparison.Ordinal);
            await Until(() => Ask(pipe, new AutomationRequest { Command = "ui.text", Target = "LibrarySummary" }),
                reply => reply is { Ok: true, Text: not null } &&
                    reply.Text.Contains("2 total", StringComparison.Ordinal), app, root);

            var quit = await Ask(pipe, new AutomationRequest { Command = "run", Id = CommandCatalog.Quit });
            Assert.True(quit?.Ok == true || (app.WaitForExit(TimeSpan.FromSeconds(5)) && app.ExitCode == 0),
                "The Mac app could not quit with the library shown: " + (quit?.Error ?? "no reply") + LogTail(root));
            Assert.True(app.WaitForExit(Patience), "The Mac app did not quit after showing the library.");
            Assert.Equal(0, app.ExitCode);

            // Quit and relaunch with the same local data root to prove both folders and
            // scanned media survived the process, not just its in-memory catalogue.
            var secondPipe = pipe + "-again";
            start.Environment["REXPLAYER_PIPE_NAME"] = secondPipe;
            using var reopenedApp = Process.Start(start)!;
            try
            {
                await Until(() => Ask(secondPipe, new AutomationRequest { Command = "status" }),
                    reply => reply is { Ok: true }, reopenedApp, root);
                var reopenedLibrary = await Ask(secondPipe, new AutomationRequest
                {
                    Command = "run",
                    Id = CommandCatalog.ToggleLibrary,
                });
                Assert.True(reopenedLibrary?.Ok == true, "The library did not reopen after restart.");
                var persisted = await Ask(secondPipe, new AutomationRequest
                {
                    Command = "ui.text",
                    Target = "LibrarySummary",
                });
                Assert.True(persisted?.Ok == true &&
                    persisted.Text?.Contains("2 folders", StringComparison.Ordinal) == true &&
                    persisted.Text.Contains("2 total", StringComparison.Ordinal),
                    "The watched folders or media did not survive restart: " + (persisted?.Text ?? "no reply"));
                var closeAgain = await Ask(secondPipe, new AutomationRequest
                {
                    Command = "run",
                    Id = CommandCatalog.Quit,
                });
                Assert.True(closeAgain?.Ok == true ||
                    (reopenedApp.WaitForExit(TimeSpan.FromSeconds(5)) && reopenedApp.ExitCode == 0));
                Assert.True(reopenedApp.WaitForExit(Patience), "The restarted Mac app did not quit.");
                Assert.Equal(0, reopenedApp.ExitCode);
            }
            finally
            {
                if (!reopenedApp.HasExited)
                {
                    reopenedApp.Kill();
                    reopenedApp.WaitForExit();
                }
            }
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

    [Fact(Skip = MacAppRequired, SkipUnless = nameof(CanDriveMacApp))]
    public async Task TheMacLibraryPaginatesMoreThanEightyFiles()
    {
        var bundle = Environment.GetEnvironmentVariable("REXPLAYER_MAC_APP");
        var root = Path.Combine(Path.GetTempPath(), "rexplayer-mac-pages-" + Guid.NewGuid().ToString("N")[..8]);
        var pipe = "rexplayer-mac-" + Guid.NewGuid().ToString("N")[..8];
        var folder = Path.Combine(root, "watched");
        Directory.CreateDirectory(folder);
        var longFolder = Path.Combine(folder, new string('f', 155));
        Directory.CreateDirectory(longFolder);
        var files = new List<LibraryFile>();
        for (var index = 0; index < 85; index++)
        {
            var filename = index == 84 ? "clip-084-" + new string('x', 155) + ".mp4" : $"clip-{index:000}.mp4";
            // A long folder name tests metadata truncation independently from the
            // long video title. Both must leave the Queue action inside the viewport.
            var file = Path.Combine(index == 83 ? longFolder : folder, filename);
            File.WriteAllBytes(file, [0]);
            files.Add(new LibraryFile(file, 1, File.GetLastWriteTimeUtc(file)));
        }

        var library = new Rex.Media.Library.MediaLibrary(RexStore.Open(new DataRoots(root).LibraryStore));
        Assert.True(library.AddFolder(folder));
        Assert.Equal(85, library.Scan(folder, files, _ => LibraryKind.Video).Added);

        var start = new ProcessStartInfo(Path.Combine(bundle!, "Contents", "MacOS", "rexplayer")) { UseShellExecute = false };
        start.Environment["REXPLAYER_ROOT"] = root;
        start.Environment["REXPLAYER_FAKE_AUDIO"] = "1";
        start.Environment["REXPLAYER_PIPE_NAME"] = pipe;
        using var app = Process.Start(start)!;
        try
        {
            await Until(() => Ask(pipe, new AutomationRequest { Command = "status" }), answer => answer is { Ok: true }, app, root);
            var open = await Ask(pipe, new AutomationRequest { Command = "run", Id = CommandCatalog.ToggleLibrary });
            Assert.True(open?.Ok == true, "The large Mac library did not open." + LogTail(root));
            var tree = await Ask(pipe, new AutomationRequest { Command = "ui.tree" });
            Assert.Contains(tree!.Controls!, control => control.Id == "LibraryPage" && control.Text == "Page 1 of 2");
            Assert.Contains(tree.Controls!, control => control.Id == "LibraryNext" && control.Enabled);
            Assert.Contains(tree.Controls!, control => control.Id == "LibraryPrevious" && !control.Enabled);
            var next = await Ask(pipe, new AutomationRequest { Command = "ui.press", Target = "LibraryNext" });
            Assert.Equal("Page 2 of 2", next?.Text);
            tree = await Ask(pipe, new AutomationRequest { Command = "ui.tree" });
            Assert.Contains(tree!.Controls!, control => control.Id == "LibraryPrevious" && control.Enabled);
            Assert.Contains(tree.Controls!, control => control.Id == "LibraryNext" && !control.Enabled);
            var secondPageKeys = await Ask(pipe, new AutomationRequest { Command = "ui.keyboard" });
            Assert.True(secondPageKeys?.Ok == true,
                "Native focus loop was broken by the paginated result change: " + (secondPageKeys?.Error ?? "no reply"));
            var previous = await Ask(pipe, new AutomationRequest { Command = "ui.press", Target = "LibraryPrevious" });
            Assert.Equal("Page 1 of 2", previous?.Text);
            var filtered = await Ask(pipe, new AutomationRequest { Command = "ui.search", Target = "clip-084" });
            Assert.True(filtered?.Text?.Contains("1 matching", StringComparison.Ordinal) == true, "The last catalogue entry was not searchable.");
            var page = await Ask(pipe, new AutomationRequest { Command = "ui.text", Target = "LibraryPage" });
            Assert.Equal("Page 1 of 1", page?.Text);
            var narrow = await Ask(pipe, new AutomationRequest { Command = "ui.set", Target = "LibraryWidth", Value = 590 });
            Assert.True(narrow?.Ok == true, "Could not resize the long-title result to minimum width.");
            var layout = await Ask(pipe, new AutomationRequest { Command = "ui.verify-layout" });
            Assert.True(layout?.Ok == true, "The long video filename clipped a native result action: " +
                (layout?.Error ?? "no reply") + LogTail(root));
            var longFolderResult = await Ask(pipe, new AutomationRequest { Command = "ui.search", Target = "clip-083" });
            Assert.True(longFolderResult?.Ok == true &&
                longFolderResult.Text?.Contains("1 matching", StringComparison.Ordinal) == true);
            var folderLayout = await Ask(pipe, new AutomationRequest { Command = "ui.verify-layout" });
            Assert.True(folderLayout?.Ok == true, "The long parent folder name clipped a native Queue action: " +
                (folderLayout?.Error ?? "no reply") + LogTail(root));
            var searchKeys = await Ask(pipe, new AutomationRequest { Command = "ui.keyboard" });
            Assert.True(searchKeys?.Ok == true, "Native focus loop was broken by search: " + (searchKeys?.Error ?? "no reply"));

            var quit = await Ask(pipe, new AutomationRequest { Command = "run", Id = CommandCatalog.Quit });
            Assert.True(quit?.Ok == true || (app.WaitForExit(TimeSpan.FromSeconds(5)) && app.ExitCode == 0));
            Assert.True(app.WaitForExit(Patience), "The large Mac library did not quit.");
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
