using System.Diagnostics;
using Rex.Media.AppCore.Automation;
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

    [Fact]
    [Capability("TOOL-03", CapabilityPlatform.MacOS)]
    public async Task TheMacAppComesUpPlaysAVideoAndQuitsWhenAScriptAsks()
    {
        var bundle = Environment.GetEnvironmentVariable("REXPLAYER_MAC_APP");
        Assert.SkipWhen(!OperatingSystem.IsMacOS() || string.IsNullOrEmpty(bundle), "Set REXPLAYER_MAC_APP to a built rexplayer.app on a Mac to drive it.");
        var root = Path.Combine(Path.GetTempPath(), "rexplayer-mac-" + Guid.NewGuid().ToString("N")[..8]);
        var pipe = "rexplayer-mac-" + Guid.NewGuid().ToString("N")[..8];
        var screenshots = RepoPaths.Combine("artifacts", "mac-queue-screenshots");
        Directory.CreateDirectory(screenshots);
        var start = new ProcessStartInfo(Path.Combine(bundle!, "Contents", "MacOS", "rexplayer")) { UseShellExecute = false };
        start.Environment["REXPLAYER_ROOT"] = root;
        start.Environment["REXPLAYER_FAKE_AUDIO"] = "1";
        start.Environment["REXPLAYER_PIPE_NAME"] = pipe;
        start.Environment["REXPLAYER_MAC_QUEUE_TESTS"] = "1";
        start.Environment["REXPLAYER_MAC_QUEUE_SCREENSHOTS"] = screenshots;
        using var app = Process.Start(start)!;
        try
        {
            var up = await Until(() => Ask(pipe, new AutomationRequest { Command = "status" }), reply => reply is { Ok: true }, app, root);
            Assert.Equal("Idle", up.State);
            var empty = await Queue(pipe, "show");
            Assert.Contains("0 items", empty.Title, StringComparison.Ordinal);
            await Queue(pipe, "capture-empty");

            var video = RepoPaths.Combine("tests", "fixtures", "mp4", "h264-aac.mp4");
            var opened = await Ask(pipe, new AutomationRequest { Command = "open", Paths = [video] });
            Assert.True(opened?.Ok == true, "The app did not acknowledge the open request: " + (opened?.Error ?? "no reply") + ". Its log ends:" + LogTail(root));
            var playing = await Until(() => Ask(pipe, new AutomationRequest { Command = "status" }), reply => reply is { Title: not null, State: "Playing" or "Ended" or "Paused" }, app, root);
            Assert.False(string.IsNullOrWhiteSpace(playing.Title), "The app opened the video but reports no title.");
            await Queue(pipe, "capture-current");
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
            foreach (var name in new[] { "empty", "current", "long", "page-2", "shuffled" })
            {
                var file = new FileInfo(Path.Combine(screenshots, name + ".png"));
                Assert.True(file.Exists && file.Length > 1024, "No usable native queue screenshot: " + file.FullName);
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
        catch (Exception ex)
        {
            throw new InvalidOperationException("Mac app smoke run failed; app log:" + LogTail(root), ex);
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

    private static async Task<AutomationReply> Queue(string pipe, string action)
    {
        var reply = await Ask(pipe, new AutomationRequest { Command = "mac-queue", Id = action });
        Assert.True(reply?.Ok == true, "Mac queue action failed: " + action + ": " + (reply?.Error ?? "no reply"));
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
