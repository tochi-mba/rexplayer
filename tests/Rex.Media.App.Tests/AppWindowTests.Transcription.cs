using System.IO;
using System.Windows.Automation;
using Rex.Media.AppCore.Captions;
using Rex.Media.AppCore.Transcription;
using Rex.Media.Settings;

namespace Rex.Media.App.Tests;

public sealed partial class AppWindowTests
{
    [Fact]
    public void TheCaptionRailSelectsExactModelCoverageEvenInFastestMode()
    {
        var media = Song(120);
        var root = Path.Combine(_media, "selector-data");
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "settings.json"), $$"""
            { "firstRunDone": true, "lastSeenVersion": "{{AppProcess.Version}}", "updateChecks": "Off",
              "performance": "Fastest", "kineticCaptions": { "transcriptModel": "Base" } }
            """);
        var cache = new TranscriptCache(TranscriptCache.DefaultRoot(root));
        cache.Save(media, new Transcript("en", "base.en", [new TranscriptWord("Default words", TimeSpan.Zero, TimeSpan.FromSeconds(1))]));
        var partial = new ProgressiveTranscript("small.en", "en", TimeSpan.FromSeconds(120));
        partial.Add(new SpeechChunk(new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(26)), new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(26))),
            [new TranscriptWord("Best words", TimeSpan.Zero, TimeSpan.FromSeconds(1))], "en");
        cache.SavePartial(media, partial);
        cache.Save(media, new Transcript("en", "tiny.en", [new TranscriptWord("Fast words", TimeSpan.Zero, TimeSpan.FromSeconds(1))]));
        using var app = AppProcess.Start([media], root);
        Wait.For(() => app.Text("SubtitleBufferState") == "All ready", "Default cached coverage",
            () => $"State: {app.Text("SubtitleBufferState")}; model: {app.Text("TranscriptModelButton")}; rail: {app.Text("TranscriptRail")}; log: {app.LogText}");
        Assert.Equal("Transcription quality: Default", app.Text("TranscriptModelButton"));
        app.Press("TranscriptModelButton");
        app.Press("TranscriptModelBest");
        Wait.For(() => app.Text("TranscriptModelButton") == "Transcription quality: Best", "Best selection without a performance downgrade");
        Wait.For(() => app.Text("TranscriptRail").Contains("0:26", StringComparison.Ordinal), "Best model partial coverage");

        // Best's saved original was offered as a variant while Default played; its live track replaces it.
        var subtitles = (ExpandCollapsePattern)app.Find("SubtitleMenu").GetCurrentPattern(ExpandCollapsePattern.Pattern);
        subtitles.Expand();
        ((ExpandCollapsePattern)app.Find("SubtitleTrackChoices").GetCurrentPattern(ExpandCollapsePattern.Pattern)).Expand();
        var bestOriginal = new PropertyCondition(AutomationElement.NameProperty, "Transcript Best — Original");
        var defaultOriginal = new PropertyCondition(AutomationElement.NameProperty, "Transcript Default — Original");
        Wait.For(() => app.Window.FindAll(TreeScope.Descendants, defaultOriginal).Count == 1
                && app.Window.FindAll(TreeScope.Descendants, bestOriginal).Count == 1,
            "one Best and one Default original after switching quality",
            () => $"Best: {app.Window.FindAll(TreeScope.Descendants, bestOriginal).Count}; Default: {app.Window.FindAll(TreeScope.Descendants, defaultOriginal).Count}");
        subtitles.Collapse();
        app.Press("TranscriptModelButton");
        app.Press("TranscriptModelFast");
        Wait.For(() => app.Text("TranscriptModelButton") == "Transcription quality: Fast" && app.Text("SubtitleBufferState") == "All ready", "Fast cached coverage");
        Assert.Equal(0, app.Close());
        Assert.Equal(TranscriptModelChoice.Tiny, app.SavedSettings.KineticCaptions.TranscriptModel);
    }
}
