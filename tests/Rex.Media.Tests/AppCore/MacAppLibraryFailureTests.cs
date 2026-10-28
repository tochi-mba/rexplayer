using System.Diagnostics;
using Rex.Media.AppCore.Automation;
using Rex.Media.AppCore.Commands;
using Rex.Media.AppCore.Platform;
using Rex.Media.TestKit;

namespace Rex.Media.Tests.AppCore;

public sealed partial class MacAppSmokeTests
{
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

    private static void AssertNoScreenshotChanged(List<string> changedShots) =>
        Assert.True(changedShots.Count == 0, string.Join(Environment.NewLine, changedShots));

    private static async Task CaptureLibraryScreenshot(
        string pipe,
        string name,
        string root,
        List<string>? changedShots = null)
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
            if (!string.Equals(baseline, actual, StringComparison.Ordinal))
            {
                var message = "Native Mac screenshot " + target + " changed. Review the mac-library-screenshots CI artifact before approving a new baseline." +
                    Environment.NewLine + "Expected: " + baseline + Environment.NewLine + "Actual:   " + actual;
                if (changedShots is null)
                {
                    Assert.Fail(message);
                }

                changedShots.Add(message);
            }
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

            Assert.True(clock.Elapsed < Patience, "rexplayer.app never got there; its last answer: " + Described(reply) + ". Its log ends:" + LogTail(root));
            await Task.Delay(200, TestContext.Current.CancellationToken);
        }
    }

    /// <summary>
    /// Every field a reply carries, as a failure quotes it: a wait on a library's title or a control's
    /// text then says what it saw, not only the player's state.
    /// </summary>
    private static string Described(AutomationReply? reply) =>
        reply is null ? "none" : string.Join(", ", new[]
        {
            reply.Ok ? "ok" : "not ok",
            Field("state", reply.State), Field("title", reply.Title), Field("text", reply.Text), Field("error", reply.Error),
        }.Where(part => part.Length > 0));

    private static string Field(string name, string? value) => string.IsNullOrEmpty(value) ? "" : name + " \"" + value + "\"";

    private static string LogTail(string root)
    {
        var log = Path.Combine(root, "logs", "rexplayer.log");
        return File.Exists(log) ? Environment.NewLine + string.Join(Environment.NewLine, File.ReadLines(log).TakeLast(20)) : " (no log was written)";
    }
}
