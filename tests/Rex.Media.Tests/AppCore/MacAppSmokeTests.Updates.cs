using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using Rex.Media.AppCore.Automation;
using Rex.Media.AppCore.Commands;
using Rex.Media.Settings;
using Rex.Media.TestKit;

namespace Rex.Media.Tests.AppCore;

public sealed partial class MacAppSmokeTests
{
    private static readonly TimeSpan InstallPatience = TimeSpan.FromMinutes(2);

    /// <summary>
    /// The whole Mac update round trip (TOOL-09), driven the way a user lives it: a release feed
    /// offers a newer version, the app downloads and verifies the ZIP, stages it, hands the bundle
    /// swap to a helper that outlives it, and the replaced app comes back with the session it saved,
    /// paused. The feed is this test's own server, which the app trusts only because the operator
    /// names it in REXPLAYER_UPDATE_FEED.
    /// </summary>
    [Fact(Skip = MacAppRequired, SkipUnless = nameof(CanDriveMacApp))]
    [Capability("TOOL-09", CapabilityPlatform.MacOS)]
    public async Task TheMacUpdaterReplacesTheBundleAndRestoresTheSessionPaused()
    {
        var bundle = Environment.GetEnvironmentVariable("REXPLAYER_MAC_APP")!;
        var root = Path.Combine(Path.GetTempPath(), "rexplayer-mac-update-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(root);
        var startupRoots = new Rex.Media.AppCore.Platform.DataRoots(root);
        SettingsStore.Save(startupRoots.SettingsPath, new PlayerSettings
        {
            FirstRunDone = true,
            LastSeenVersion = "0.0.0",
            UpdateChecks = UpdateCadence.Off,
            UpdateMode = UpdateMode.NotifyOnly,
        });

        // The update on offer is this bundle again, told apart by a marker file: the assembly
        // version cannot change without a rebuild, so the proof of the swap is the marker, the
        // helper's record, and the session coming back paused.
        const string markerName = "rexplayer-update-smoke.txt";
        var release = Path.Combine(root, "release");
        var staged = Path.Combine(release, "rexplayer.app");
        Tool("ditto", bundle, staged);
        File.WriteAllText(Path.Combine(staged, "Contents", "Resources", markerName), "A newer rexplayer, as far as this journey cares.");
        Tool("codesign", "--force", "--deep", "--sign", "-", staged);
        var zip = Path.Combine(release, "rexplayer-mac-universal.zip");
        Tool("ditto", "-c", "-k", "--keepParent", staged, zip);
        var zipBytes = File.ReadAllBytes(zip);
        var checksum = Convert.ToHexString(SHA256.HashData(zipBytes)) + "  rexplayer-mac-universal.zip\n";

        using var feed = new HttpListener();
        var port = FreePort();
        feed.Prefixes.Add($"http://127.0.0.1:{port}/");
        feed.Start();
        var feedJson = $$"""
            {
              "tag_name": "v99.0.0",
              "html_url": "http://127.0.0.1:{{port}}/release",
              "body": "A stand-in release, served by the update smoke test.",
              "assets": [
                { "name": "rexplayer-mac-universal.zip",
                  "browser_download_url": "http://127.0.0.1:{{port}}/rexplayer-mac-universal.zip",
                  "size": {{zipBytes.Length}} },
                { "name": "rexplayer-mac-universal.zip.sha256",
                  "browser_download_url": "http://127.0.0.1:{{port}}/rexplayer-mac-universal.zip.sha256" }
              ]
            }
            """;
        var serving = Task.Run(() => Serve(feed, feedJson, zipBytes, checksum));

        var pipe = "rexplayer-mac-" + Guid.NewGuid().ToString("N")[..8];
        var start = new ProcessStartInfo(Path.Combine(bundle, "Contents", "MacOS", "rexplayer"))
        {
            UseShellExecute = false,
        };
        start.Environment["REXPLAYER_ROOT"] = root;
        start.Environment["REXPLAYER_FAKE_AUDIO"] = "1";
        start.Environment["REXPLAYER_PIPE_NAME"] = pipe;
        start.Environment["REXPLAYER_MAC_UPDATE_TESTS"] = "1";
        start.Environment["REXPLAYER_UPDATE_FEED"] = $"http://127.0.0.1:{port}/release.json";
        var app = Process.Start(start)!;
        try
        {
            await Until(() => Ask(pipe, new AutomationRequest { Command = "status" }), reply => reply is { Ok: true }, app, root);

            // Something must be mid-play for the restore to mean anything; paused, the download
            // also runs at full speed instead of playback's gentle limit.
            var video = RepoPaths.Combine("tests", "fixtures", "mp4", "h264-aac-minute.mp4");
            var opened = await Ask(pipe, new AutomationRequest { Command = "open", Paths = [video] });
            Assert.True(opened?.Ok == true, "The app did not acknowledge the open request: " + (opened?.Error ?? "no reply") + LogTail(root));
            await Until(() => Ask(pipe, new AutomationRequest { Command = "status" }),
                reply => reply is { Title: not null, State: "Playing" }, app, root);
            await Ask(pipe, new AutomationRequest { Command = "run", Id = CommandCatalog.PlayPause });
            await Until(() => Ask(pipe, new AutomationRequest { Command = "status" }),
                reply => reply is { State: "Paused" }, app, root);

            var checkStarted = await Ask(pipe, new AutomationRequest { Command = "mac-updates", Id = "check" });
            Assert.True(checkStarted?.Ok == true, "The Mac update check did not start: " + (checkStarted?.Error ?? "no reply") + LogTail(root));
            await Until(() => Ask(pipe, new AutomationRequest { Command = "mac-updates", Id = "status" }),
                reply => reply?.Text?.Contains("offer=99.0.0", StringComparison.Ordinal) == true, app, root);
            var downloadStarted = await Ask(pipe, new AutomationRequest { Command = "mac-updates", Id = "download" });
            Assert.True(downloadStarted?.Ok == true, "The Mac update download did not start: " + (downloadStarted?.Error ?? "no reply") + LogTail(root));
            await Until(() => Ask(pipe, new AutomationRequest { Command = "mac-updates", Id = "status" }),
                reply => reply?.Text?.Contains("ready=True", StringComparison.Ordinal) == true, app, root);

            var install = await Ask(pipe, new AutomationRequest { Command = "mac-updates", Id = "install-now" });
            Assert.True(install?.Ok == true, "The Mac update install did not start: " + (install?.Error ?? "no reply") + LogTail(root));
            Assert.True(app.WaitForExit(InstallPatience), "The app did not close for the update hand-off." + LogTail(root));

            // The helper outlives the app: it swaps the bundle, records how it went, and reopens
            // the new app. The reopened copy runs outside this test's data root, so it is closed
            // again; the proof it carries is read from the bundle and the record instead.
            var record = Path.Combine(startupRoots.UpdateFolder, "install-result.txt");
            var deadline = Stopwatch.StartNew();
            while (!File.Exists(record) || File.ReadAllText(record).Trim().Length == 0)
            {
                Assert.True(deadline.Elapsed < InstallPatience, "The update helper never recorded how the install ended." + LogTail(root));
                await Task.Delay(500, TestContext.Current.CancellationToken);
            }

            Assert.Equal("success", File.ReadAllText(record).Trim());
            Assert.True(File.Exists(Path.Combine(bundle, "Contents", "Resources", markerName)),
                "The bundle on disk is still the old one; the update was not installed in place.");
            await Task.Delay(2000, TestContext.Current.CancellationToken);
            Tool("pkill", exitCodes: [0, 1], "-f", Path.Combine(bundle, "Contents", "MacOS", "rexplayer"));

            // The replaced bundle, launched with the same data root, owes the user their session
            // back: the same video, paused where the install left it.
            var reopenedPipe = pipe + "-again";
            start.Environment["REXPLAYER_PIPE_NAME"] = reopenedPipe;
            using var reopened = Process.Start(start)!;
            try
            {
                var restored = await Until(() => Ask(reopenedPipe, new AutomationRequest { Command = "status" }),
                    reply => reply is { Ok: true, Title: not null, State: "Paused" }, reopened, root);
                Assert.NotNull(restored.Title);
                var quit = await Ask(reopenedPipe, new AutomationRequest { Command = "run", Id = CommandCatalog.Quit });
                Assert.True(quit?.Ok == true || (reopened.WaitForExit(TimeSpan.FromSeconds(5)) && reopened.ExitCode == 0),
                    "The updated Mac app could not quit: " + (quit?.Error ?? "no reply") + LogTail(root));
                Assert.True(reopened.WaitForExit(Patience), "The updated Mac app did not quit.");
                Assert.Equal(0, reopened.ExitCode);
            }
            finally
            {
                if (!reopened.HasExited)
                {
                    reopened.Kill();
                    reopened.WaitForExit();
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

            app.Dispose();
            Tool("pkill", exitCodes: [0, 1], "-f", Path.Combine(bundle, "Contents", "MacOS", "rexplayer"));
            feed.Stop();
            await serving;
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    /// <summary>Answers the stand-in feed's three files until the listener stops.</summary>
    private static void Serve(HttpListener feed, string releaseJson, byte[] zip, string checksum)
    {
        while (true)
        {
            HttpListenerContext context;
            try
            {
                context = feed.GetContext();
            }
            catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException or InvalidOperationException)
            {
                return;
            }

            byte[] body = context.Request.Url?.AbsolutePath switch
            {
                "/release.json" => System.Text.Encoding.UTF8.GetBytes(releaseJson),
                "/rexplayer-mac-universal.zip" => zip,
                "/rexplayer-mac-universal.zip.sha256" => System.Text.Encoding.UTF8.GetBytes(checksum),
                _ => [],
            };
            context.Response.StatusCode = body.Length == 0 ? 404 : 200;
            context.Response.ContentLength64 = body.Length;
            try
            {
                context.Response.OutputStream.Write(body);
                context.Response.Close();
            }
            catch (Exception ex) when (ex is HttpListenerException or IOException or ObjectDisposedException)
            {
                // The downloader may drop a connection it is done with; the next request matters, not this one.
            }
        }
    }

    /// <summary>Runs a macOS command-line tool and asserts it ends with an accepted exit code.</summary>
    private static void Tool(string name, params string[] arguments) => Tool(name, [0], arguments);

    private static void Tool(string name, int[] exitCodes, params string[] arguments)
    {
        var start = new ProcessStartInfo(name) { UseShellExecute = false };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start)!;
        process.WaitForExit();
        Assert.Contains(process.ExitCode, exitCodes);
    }

    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
