using System.Diagnostics;
using System.Globalization;
using Rex.Media.AppCore.Automation;
using Rex.Media.AppCore.Captions;
using Rex.Media.AppCore.Commands;
using Rex.Media.AppCore.Platform;
using Rex.Media.Settings;
using Rex.Media.TestKit;

namespace Rex.Media.Tests.AppCore;

public sealed partial class MacAppSmokeTests
{
    /// <summary>
    /// A saved transcript, seeded in the cache for a copy of the minute-long video with no speech
    /// model at all, is what the Mac reads: its coverage lies on the seek bar, the reader lists its
    /// lines, a search finds a seeded word, choosing a line moves playback there, and a second saved
    /// quality shows as a variant.
    /// </summary>
    [Fact(Skip = MacAppRequired, SkipUnless = nameof(CanDriveMacApp))]
    [Capability("SUB-19", CapabilityPlatform.MacOS)]
    public async Task TheMacReadsSearchesAndSeeksThroughASavedTranscript()
    {
        var bundle = Environment.GetEnvironmentVariable("REXPLAYER_MAC_APP");
        var root = Path.Combine(Path.GetTempPath(), "rexplayer-mac-reader-" + Guid.NewGuid().ToString("N")[..8]);
        var startupRoots = new DataRoots(root);
        SettingsStore.Save(startupRoots.SettingsPath, new PlayerSettings
        {
            FirstRunDone = true,
            LastSeenVersion = "0.0.0",
        });
        var folder = Path.Combine(root, "media");
        Directory.CreateDirectory(folder);
        var video = Path.Combine(folder, "reader-minute.mp4");
        File.Copy(RepoPaths.Combine("tests", "fixtures", "mp4", "h264-aac-minute.mp4"), video);
        var words = new List<TranscriptWord>();
        for (var sentence = 0; sentence < 15; sentence++)
        {
            var at = TimeSpan.FromSeconds(sentence * 4);
            var middle = sentence == 9 ? "lighthouse" : "number";
            words.Add(new TranscriptWord("Sentence", at, at + TimeSpan.FromSeconds(0.5)));
            words.Add(new TranscriptWord(middle, at + TimeSpan.FromSeconds(0.6), at + TimeSpan.FromSeconds(1.1)));
            words.Add(new TranscriptWord(sentence.ToString(CultureInfo.InvariantCulture) + ".", at + TimeSpan.FromSeconds(1.2), at + TimeSpan.FromSeconds(1.6)));
        }

        var cache = new TranscriptCache(TranscriptCache.DefaultRoot(root));
        var preferred = SpeechModels.For(new KineticCaptionSettings().TranscriptModel).Id;
        var other = SpeechModels.For(TranscriptModelChoice.Tiny).Id;
        cache.Save(video, new Transcript("en", preferred, words));
        cache.Save(video, new Transcript("en", other, words.Take(6).ToList()));
        var pipe = "rexplayer-mac-" + Guid.NewGuid().ToString("N")[..8];
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
            await Until(() => Ask(pipe, new AutomationRequest { Command = "status" }), reply => reply is { Ok: true }, app, root);
            var opened = await Ask(pipe, new AutomationRequest { Command = "open", Paths = [video] });
            Assert.True(opened?.Ok == true, "The app did not acknowledge the open request: " + (opened?.Error ?? "no reply") + LogTail(root));
            await Until(() => Ask(pipe, new AutomationRequest { Command = "status" }),
                reply => reply is { Title: not null, State: "Playing" }, app, root);
            await Ask(pipe, new AutomationRequest { Command = "run", Id = CommandCatalog.PlayPause });
            await Until(() => Ask(pipe, new AutomationRequest { Command = "status" }),
                reply => reply is { State: "Paused" }, app, root);

            // A complete saved transcript covers the whole timeline in one span.
            var coverage = await Until(() => Ask(pipe, new AutomationRequest { Command = "mac-speech", Id = "coverage" }),
                reply => reply?.Text is { } text && !text.StartsWith("spans=0", StringComparison.Ordinal), app, root);
            Assert.Contains("spans=1;covered=1.00", coverage.Text, StringComparison.Ordinal);

            var reader = await Until(() => Ask(pipe, new AutomationRequest { Command = "mac-speech", Id = "reader" }),
                reply => reply?.Text is { } text && text.StartsWith("visible=True;", StringComparison.Ordinal)
                    && !text.Contains(";lines=0;", StringComparison.Ordinal), app, root);
            Assert.Contains(";lines=15;", reader.Text, StringComparison.Ordinal);

            var search = await Ask(pipe, new AutomationRequest { Command = "mac-speech", Id = "reader-search:lighthouse" });
            Assert.Equal("matches=1", search?.Text);
            var filtered = await Ask(pipe, new AutomationRequest { Command = "mac-speech", Id = "reader" });
            Assert.Contains(";lines=1;", filtered?.Text, StringComparison.Ordinal);

            // Line 9 begins at 36 seconds; choosing it moves the paused playhead there.
            var seek = await Ask(pipe, new AutomationRequest { Command = "mac-speech", Id = "reader-seek:9" });
            Assert.True(seek?.Ok == true, "Choosing a transcript line did not seek: " + (seek?.Error ?? "no reply") + LogTail(root));
            await Until(() => Ask(pipe, new AutomationRequest { Command = "status" }),
                reply => reply?.Position is >= 35 and <= 40, app, root);

            var variants = await Until(() => Ask(pipe, new AutomationRequest { Command = "mac-speech", Id = "variants" }),
                reply => reply?.Text is { } text && !text.StartsWith("count=0", StringComparison.Ordinal)
                    && !text.StartsWith("count=1;", StringComparison.Ordinal), app, root);
            Assert.StartsWith("count=2;active=0", variants.Text, StringComparison.Ordinal);

            var quit = await Ask(pipe, new AutomationRequest { Command = "run", Id = CommandCatalog.Quit });
            Assert.True(quit?.Ok == true || (app.WaitForExit(TimeSpan.FromSeconds(5)) && app.ExitCode == 0),
                "The app neither acknowledged quit nor exited cleanly: " + (quit?.Error ?? "no reply") + LogTail(root));
            Assert.True(app.WaitForExit(Patience), "rexplayer.app did not quit when asked.");
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
