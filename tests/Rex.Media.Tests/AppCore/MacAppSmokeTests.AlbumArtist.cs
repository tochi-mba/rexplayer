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
    /// The native library opens an album's page over the list, with its artist, discs and songs as
    /// the portable album rules arrange a seeded two-disc album; the artist's page lists their album;
    /// an album the catalogue does not hold cannot open; and Back returns through the album page to
    /// the list it was opened from.
    /// </summary>
    [Fact(Skip = MacAppRequired, SkipUnless = nameof(CanDriveMacApp))]
    [Capability("LIB-05", CapabilityPlatform.MacOS)]
    public async Task TheMacLibraryOpensAlbumAndArtistPagesAndReturns()
    {
        var bundle = Environment.GetEnvironmentVariable("REXPLAYER_MAC_APP");
        var root = Path.Combine(Path.GetTempPath(), "rexplayer-mac-album-" + Guid.NewGuid().ToString("N")[..8]);
        var pipe = "rexplayer-mac-" + Guid.NewGuid().ToString("N")[..8];
        var mediaFolder = Path.Combine(root, "watched");
        Directory.CreateDirectory(mediaFolder);
        var fixture = RepoPaths.Combine("tests", "fixtures", "mp3", "stereo-44k-128k-cbr.mp3");
        var firstPath = Path.Combine(mediaFolder, "Opening.mp3");
        var secondPath = Path.Combine(mediaFolder, "Encore.mp3");
        File.Copy(fixture, firstPath);
        File.Copy(fixture, secondPath);

        var roots = new DataRoots(root);
        var library = new Rex.Media.Library.MediaLibrary(RexStore.Open(roots.LibraryStore));
        Assert.True(library.AddFolder(mediaFolder));
        var result = library.Scan(mediaFolder,
            [
                new LibraryFile(firstPath, new FileInfo(firstPath).Length, File.GetLastWriteTimeUtc(firstPath)),
                new LibraryFile(secondPath, new FileInfo(secondPath).Length, File.GetLastWriteTimeUtc(secondPath)),
            ],
            _ => LibraryKind.Music);
        Assert.Equal(2, result.Added);
        // The fixture carries no album tags, so the album is given as a person's own details,
        // which the library keeps over the file's tags through the app's own rescan.
        foreach (var (path, title, disc) in new[] { (firstPath, "Opening", "1"), (secondPath, "Encore", "2") })
        {
            library.SetOverride([path], new DetailOverride
            {
                Title = title,
                Artist = "Smoke Artist",
                AlbumArtist = "Smoke Artist",
                Album = "Smoke Album",
                Disc = disc,
                Track = "1",
                Year = "2024",
            });
        }

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

            const string albumPage = "album=Smoke Album;artist=Smoke Artist;discs=2;tracks=2";
            var album = await Library(pipe, "open-album:Smoke Album");
            Assert.Equal(albumPage, album.Title);

            var missing = await Ask(pipe, new AutomationRequest { Command = "mac-library", Id = "open-album:No Such Album" });
            Assert.True(missing?.Ok == false, "The library opened a page for an album it does not hold.");

            var artist = await Library(pipe, "open-artist:Smoke Artist");
            Assert.Equal("artist=Smoke Artist;albums=1", artist.Title);

            var backToAlbum = await Library(pipe, "back");
            Assert.Equal(albumPage, backToAlbum.Title);

            var backToList = await Library(pipe, "back");
            Assert.Contains("2 items", backToList.Title, StringComparison.Ordinal);

            var quit = await Ask(pipe, new AutomationRequest { Command = "run", Id = CommandCatalog.Quit });
            Assert.True(quit?.Ok == true || (app.WaitForExit(TimeSpan.FromSeconds(5)) && app.ExitCode == 0),
                "The Mac app could not quit after the album and artist pages: " + (quit?.Error ?? "no reply") + LogTail(root));
            Assert.True(app.WaitForExit(Patience), "The Mac app did not quit after driving the album and artist pages.");
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
