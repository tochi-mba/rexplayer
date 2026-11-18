using System.Diagnostics;
using Rex.Media.AppCore.Automation;
using Rex.Media.AppCore.Commands;
using Rex.Media.AppCore.Platform;
using Rex.Media.Library;
using Rex.Media.TestKit;

namespace Rex.Media.Tests.AppCore;

public sealed partial class MacAppSmokeTests
{
    /// <summary>
    /// The native library sidebar lists the sections the portable rules derive from a seeded
    /// catalogue of one song and one video, a selected section switches the shared list page to its
    /// view, and Back returns through the sections visited, restoring each view's results.
    /// </summary>
    [Fact(Skip = MacAppRequired, SkipUnless = nameof(CanDriveMacApp))]
    [Capability("LIB-06", CapabilityPlatform.MacOS)]
    public async Task TheMacLibrarySidebarListsSectionsSwitchesViewsAndReturns()
    {
        var bundle = Environment.GetEnvironmentVariable("REXPLAYER_MAC_APP");
        var root = Path.Combine(Path.GetTempPath(), "rexplayer-mac-sidebar-" + Guid.NewGuid().ToString("N")[..8]);
        var pipe = "rexplayer-mac-" + Guid.NewGuid().ToString("N")[..8];
        var mediaFolder = Path.Combine(root, "watched");
        Directory.CreateDirectory(mediaFolder);
        var songPath = Path.Combine(mediaFolder, "Sidebar Song.mp3");
        File.Copy(RepoPaths.Combine("tests", "fixtures", "mp3", "stereo-44k-128k-cbr.mp3"), songPath);
        var filmPath = Path.Combine(mediaFolder, "Sidebar Film.mp4");
        File.Copy(RepoPaths.Combine("tests", "fixtures", "mp4", "h264-aac.mp4"), filmPath);

        var roots = new DataRoots(root);
        var library = new Rex.Media.Library.MediaLibrary(RexStore.Open(roots.LibraryStore));
        Assert.True(library.AddFolder(mediaFolder));
        var result = library.Scan(mediaFolder,
            [
                new LibraryFile(songPath, new FileInfo(songPath).Length, File.GetLastWriteTimeUtc(songPath)),
                new LibraryFile(filmPath, new FileInfo(filmPath).Length, File.GetLastWriteTimeUtc(filmPath)),
            ],
            path => path.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase) ? LibraryKind.Music : LibraryKind.Video);
        Assert.Equal(2, result.Added);

        var start = new ProcessStartInfo(Path.Combine(bundle!, "Contents", "MacOS", "rexplayer")) { UseShellExecute = false };
        start.Environment["REXPLAYER_ROOT"] = root;
        start.Environment["REXPLAYER_FAKE_AUDIO"] = "1";
        start.Environment["REXPLAYER_PIPE_NAME"] = pipe;
        start.Environment["REXPLAYER_MAC_LIBRARY_TESTS"] = "1";
        using var app = Process.Start(start)!;
        try
        {
            await Until(() => Ask(pipe, new AutomationRequest { Command = "status" }), reply => reply is { Ok: true }, app, root);
            var shown = await Library(pipe, "show");
            Assert.Contains("2 items", shown.Title, StringComparison.Ordinal);
            Assert.Contains("1 folder", shown.Title, StringComparison.Ordinal);

            // The first look is immediate, but a startup rescan may still be settling; ask until
            // the sidebar lists what the seeded song, video and watched folder make present.
            const string seededSections = "sections=home,songs,videos,recentlyadded,folders";
            await Until(() => Ask(pipe, new AutomationRequest { Command = "mac-library", Id = "sidebar" }),
                reply => reply is { Ok: true } && reply.Title == seededSections + ";selected=home", app, root);

            var songs = await Library(pipe, "sidebar-select:songs");
            Assert.Contains("1 item", songs.Title, StringComparison.Ordinal);
            var selectedSongs = await Library(pipe, "sidebar");
            Assert.Equal(seededSections + ";selected=songs", selectedSongs.Title);

            var videos = await Library(pipe, "sidebar-select:videos");
            Assert.Contains("1 item", videos.Title, StringComparison.Ordinal);
            var selectedVideos = await Library(pipe, "sidebar");
            Assert.Equal(seededSections + ";selected=videos", selectedVideos.Title);

            // A section the catalogue gives the Mac no page for must not be selectable.
            var refused = await Ask(pipe, new AutomationRequest { Command = "mac-library", Id = "sidebar-select:albums" });
            Assert.True(refused?.Ok == false, "The sidebar accepted a section it does not list.");

            var backToSongs = await Library(pipe, "back");
            Assert.Contains("1 item", backToSongs.Title, StringComparison.Ordinal);
            var afterFirstBack = await Library(pipe, "sidebar");
            Assert.Equal(seededSections + ";selected=songs", afterFirstBack.Title);

            var backHome = await Library(pipe, "back");
            Assert.Contains("2 items", backHome.Title, StringComparison.Ordinal);
            var afterSecondBack = await Library(pipe, "sidebar");
            Assert.Equal(seededSections + ";selected=home", afterSecondBack.Title);

            // With every stop consumed there is nothing left to return to.
            var exhausted = await Ask(pipe, new AutomationRequest { Command = "mac-library", Id = "back" });
            Assert.True(exhausted?.Ok == false, "Back succeeded with no previous library view left.");

            var quit = await Ask(pipe, new AutomationRequest { Command = "run", Id = CommandCatalog.Quit });
            Assert.True(quit?.Ok == true || (app.WaitForExit(TimeSpan.FromSeconds(5)) && app.ExitCode == 0),
                "The Mac app could not quit with the library sidebar shown: " + (quit?.Error ?? "no reply") + LogTail(root));
            Assert.True(app.WaitForExit(Patience), "The Mac app did not quit after driving the library sidebar.");
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
