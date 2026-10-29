using System.Diagnostics;
using Rex.Media.AppCore.Automation;
using Rex.Media.AppCore.Commands;
using Rex.Media.Library;
using Rex.Media.Settings;
using Rex.Media.TestKit;

namespace Rex.Media.Tests.AppCore;

[System.Runtime.Versioning.SupportedOSPlatform("macos")]
public sealed partial class MacAppSmokeTests
{
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
