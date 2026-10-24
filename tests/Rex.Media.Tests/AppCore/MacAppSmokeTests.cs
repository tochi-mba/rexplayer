using System.Diagnostics;
using Rex.Media.AppCore.Automation;
using Rex.Media.AppCore.Commands;
using Rex.Media.AppCore.Platform;
using Rex.Media.Library;
using Rex.Media.TestKit;

namespace Rex.Media.Tests.AppCore;

/// <summary>
/// The real rexplayer.app, started and driven through its automation pipe as a script would
/// (TOOL-03), the way the Windows window suite drives rexplayer.exe: it must come up, open and play
/// a video, and quit cleanly when asked. Only on a Mac, and only when REXPLAYER_MAC_APP names a
/// built bundle (the macOS app job sets it); elsewhere it says why it did not run.
/// </summary>
[System.Runtime.Versioning.SupportedOSPlatform("macos")]
public sealed class MacAppSmokeTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);
    private const string MacAppRequired = "Set REXPLAYER_MAC_APP to a built rexplayer.app on a Mac to drive it.";

    // xUnit evaluates this gate before invoking a Mac-only test body.
    public static bool CanDriveMacApp => OperatingSystem.IsMacOS() &&
        !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("REXPLAYER_MAC_APP"));

    [Fact(Skip = MacAppRequired, SkipUnless = nameof(CanDriveMacApp))]
    [Capability("TOOL-03", CapabilityPlatform.MacOS)]
    public async Task TheMacAppComesUpPlaysAVideoAndQuitsWhenAScriptAsks()
    {
        var bundle = Environment.GetEnvironmentVariable("REXPLAYER_MAC_APP");
        var root = Path.Combine(Path.GetTempPath(), "rexplayer-mac-" + Guid.NewGuid().ToString("N")[..8]);
        var pipe = "rexplayer-mac-" + Guid.NewGuid().ToString("N")[..8];
        var screenshots = RepoPaths.Combine("artifacts", "mac-queue-screenshots");
        Directory.CreateDirectory(screenshots);
        var libraryScreenshots = RepoPaths.Combine("artifacts", "mac-library-screenshots");
        Directory.CreateDirectory(libraryScreenshots);
        var libraryFixture = Path.Combine(root, "watched-media");
        Directory.CreateDirectory(libraryFixture);
        File.Copy(RepoPaths.Combine("tests", "fixtures", "mp3", "stereo-44k-128k-cbr.mp3"),
            Path.Combine(libraryFixture, "Sample Song.mp3"));
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
            var emptyLibrary = await Library(pipe, "show");
            Assert.Contains("0 items", emptyLibrary.Title, StringComparison.Ordinal);
            await Library(pipe, "capture-empty");
            // The reviewed "empty" baseline must be taken now, before adding
            // a watched folder or resizing the AppKit library to minimum width.
            await CaptureLibraryScreenshot(pipe, "empty", root);
            await Library(pipe, "add-folder", libraryFixture);
            var populated = await Until(
                () => Ask(pipe, new AutomationRequest { Command = "mac-library", Id = "status" }),
                reply => reply?.Title?.Contains("2 items", StringComparison.Ordinal) == true,
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
            Assert.Contains("2 items", restored.Title, StringComparison.Ordinal);
            var songs = await Library(pipe, "songs");
            Assert.Contains("1 item", songs.Title, StringComparison.Ordinal);
            var onlyVideos = await Library(pipe, "videos");
            Assert.Contains("1 item", onlyVideos.Title, StringComparison.Ordinal);
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

            await Library(pipe, "show");
            var folders = await Library(pipe, "folders");
            Assert.Contains("1 item", folders.Title, StringComparison.Ordinal);
            await Library(pipe, "capture-folders");
            var cleared = await Library(pipe, "remove-folder");
            Assert.Contains("0 items", cleared.Title, StringComparison.Ordinal);
            Assert.Contains("0 folders", cleared.Title, StringComparison.Ordinal);
            await Library(pipe, "capture-cleared");
            Assert.True(File.Exists(Path.Combine(libraryFixture, "Sample Song.mp3")),
                "Removing a watched folder must never delete an audio file.");
            Assert.True(File.Exists(Path.Combine(libraryFixture, "Sample Film.mp4")),
                "Removing a watched folder must never delete a video file.");
            foreach (var name in new[] { "folders", "cleared" })
            {
                var file = new FileInfo(Path.Combine(libraryScreenshots, name + ".png"));
                Assert.True(file.Exists && file.Length > 1024,
                    "No usable native watched-folder screenshot: " + file.FullName);
            }

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

    [Fact(Skip = MacAppRequired, SkipUnless = nameof(CanDriveMacApp))]
    public async Task TheMacLibraryWarnsWhenTheStoreCannotBeRead()
    {
        var bundle = Environment.GetEnvironmentVariable("REXPLAYER_MAC_APP");
        var root = Path.Combine(Path.GetTempPath(), "rexplayer-mac-read-error-" + Guid.NewGuid().ToString("N")[..8]);
        var pipe = "rexplayer-mac-" + Guid.NewGuid().ToString("N")[..8];
        var watched = Path.Combine(root, "watched");
        Directory.CreateDirectory(watched);
        var store = new DataRoots(root).LibraryStore;
        File.WriteAllText(store, "An unreadable library store");
        File.SetUnixFileMode(store, UnixFileMode.UserWrite);
        var start = new ProcessStartInfo(Path.Combine(bundle!, "Contents", "MacOS", "rexplayer"))
        {
            UseShellExecute = false,
        };
        start.Environment["REXPLAYER_ROOT"] = root;
        start.Environment["REXPLAYER_FAKE_AUDIO"] = "1";
        start.Environment["REXPLAYER_PIPE_NAME"] = pipe;
        try
        {
            using var app = Process.Start(start)!;
            try
            {
                await Until(() => Ask(pipe, new AutomationRequest { Command = "status" }),
                    reply => reply is { Ok: true }, app, root);
                var opened = await Ask(pipe, new AutomationRequest { Command = "run", Id = CommandCatalog.ToggleLibrary });
                Assert.True(opened?.Ok == true, "The library could not open with an unreadable persistent store.");
                var summary = await Ask(pipe, new AutomationRequest { Command = "ui.text", Target = "LibrarySummary" });
                Assert.Contains("Changes will not be saved", summary?.Text ?? "");
                var added = await Ask(pipe, new AutomationRequest { Command = "ui.add-folder", Paths = [watched] });
                Assert.True(added?.Ok == false && added.Error?.Contains("forgotten", StringComparison.Ordinal) == true,
                    "The temporary catalogue incorrectly promised persistent watched folders.");
                var quit = await Ask(pipe, new AutomationRequest { Command = "run", Id = CommandCatalog.Quit });
                Assert.True(quit?.Ok == true || (app.WaitForExit(TimeSpan.FromSeconds(5)) && app.ExitCode == 0));
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
            }
        }
        finally
        {
            File.SetUnixFileMode(store, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact(Skip = MacAppRequired, SkipUnless = nameof(CanDriveMacApp))]
    public async Task TheMacLibraryReportsWhenFolderChangesCannotBeSaved()
    {
        var bundle = Environment.GetEnvironmentVariable("REXPLAYER_MAC_APP");
        var root = Path.Combine(Path.GetTempPath(), "rexplayer-mac-write-error-" + Guid.NewGuid().ToString("N")[..8]);
        var pipe = "rexplayer-mac-" + Guid.NewGuid().ToString("N")[..8];
        var folder = Path.Combine(root, "watched");
        Directory.CreateDirectory(folder);
        // Obstruct the expected catalogue file with a directory. RexStore accepts the
        // initial missing file, but reports the failure when AddFolder attempts to write.
        Directory.CreateDirectory(new DataRoots(root).LibraryStore);
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
            await Until(() => Ask(pipe, new AutomationRequest { Command = "status" }),
                answer => answer is { Ok: true }, app, root);
            var open = await Ask(pipe, new AutomationRequest
            {
                Command = "run",
                Id = CommandCatalog.ToggleLibrary,
            });
            Assert.True(open?.Ok == true, "Unable to open the library in write-failure test." + LogTail(root));
            var add = await Ask(pipe, new AutomationRequest { Command = "ui.add-folder", Paths = [folder] });
            Assert.True(add?.Ok == false && add.Error?.Contains("only in memory", StringComparison.Ordinal) == true,
                "The Mac silently accepted an unsaved folder: " + (add?.Text ?? add?.Error ?? "no reply"));
            var summary = await Ask(pipe, new AutomationRequest { Command = "ui.text", Target = "LibrarySummary" });
            Assert.Contains("Changes could not be saved", summary?.Text ?? "");
            var quit = await Ask(pipe, new AutomationRequest { Command = "run", Id = CommandCatalog.Quit });
            Assert.True(quit?.Ok == true || (app.WaitForExit(TimeSpan.FromSeconds(5)) && app.ExitCode == 0));
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

    private static async Task CaptureLibraryScreenshot(string pipe, string name, string root)
    {
        var directory = Environment.GetEnvironmentVariable("REXPLAYER_MAC_SCREENSHOTS") ?? root;
        var file = Path.Combine(directory, "mac-library-" + name + ".png");
        var reply = await Ask(pipe, new AutomationRequest { Command = "ui.snapshot", Path = file });
        Assert.True(reply?.Ok == true, "Capturing the " + name + " library failed: " + (reply?.Error ?? "no reply") + LogTail(root));
        Assert.True(File.Exists(file), "The native library did not save its " + name + " screenshot.");
        var bytes = await File.ReadAllBytesAsync(file, TestContext.Current.CancellationToken);
        Assert.True(bytes.Length > 100, "The native library screenshot is empty.");
        Assert.Equal(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, bytes.Take(8).ToArray());

        // Exact image baselines apply only to the controlled Apple Silicon CI lane.
        // Store and review PNG artifacts whenever the renderer changes before updating
        // the committed hashes. Other machines may vary in fonts and display scaling.
        if (Environment.GetEnvironmentVariable("REXPLAYER_MAC_SCREENSHOT_BASELINES") == "1")
        {
            var target = "mac-library-" + name + ".png";
            var baseline = File.ReadLines(RepoPaths.Combine("tests", "mac-library-screenshots.sha256"))
                .Where(line => !line.StartsWith('#') && line.EndsWith("  " + target, StringComparison.Ordinal))
                .Select(line => line.Split(' ', 2)[0])
                .Single();
            var actual = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();
            Assert.True(string.Equals(baseline, actual, StringComparison.Ordinal),
                "Native Mac screenshot " + target + " changed. Review the mac-library-screenshots CI artifact before approving a new baseline." +
                Environment.NewLine + "Expected: " + baseline + Environment.NewLine + "Actual:   " + actual);
        }
    }

    private static async Task<AutomationReply> Queue(string pipe, string action)
    {
        var reply = await Ask(pipe, new AutomationRequest { Command = "mac-queue", Id = action });
        Assert.True(reply?.Ok == true, "Mac queue action failed: " + action + ": " + (reply?.Error ?? "no reply"));
        return reply!;
    }

    private static async Task<AutomationReply> Library(string pipe, string action, string? folder = null)
    {
        var reply = await Ask(pipe, new AutomationRequest
        {
            Command = "mac-library",
            Id = action,
            Paths = folder is null ? [] : [folder],
        });
        Assert.True(reply?.Ok == true, "Mac library action failed: " + action + ": " + (reply?.Error ?? "no reply"));
        return reply!;
    }

    private static Task<AutomationReply?> Ask(string pipe, AutomationRequest request) =>
        AutomationPipe.SendAsync(pipe, request, TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

    /// <summary>Asks again until the reply is what is wanted; fails at once if the app has gone, quoting the end of its log.</summary>
    private static async Task<AutomationReply> Until(Func<Task<AutomationReply?>> ask, Func<AutomationReply?, bool> wanted, Process app, string root)
    {
        var clock = Stopwatch.StartNew();
        while (true)
        {
            Assert.False(app.HasExited, $"rexplayer.app exited with {(app.HasExited ? app.ExitCode : 0)}. Its log ends:" + LogTail(root));
            var reply = await ask();
            if (wanted(reply))
            {
                return reply!;
            }

            Assert.True(clock.Elapsed < Patience, "rexplayer.app never got there; its last answer: " + (reply?.State ?? reply?.Error ?? "none") + ". Its log ends:" + LogTail(root));
            await Task.Delay(200, TestContext.Current.CancellationToken);
        }
    }

    private static string LogTail(string root)
    {
        var log = Path.Combine(root, "logs", "rexplayer.log");
        return File.Exists(log) ? Environment.NewLine + string.Join(Environment.NewLine, File.ReadLines(log).TakeLast(20)) : " (no log was written)";
    }
}
