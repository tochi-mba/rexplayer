using System.IO;
using System.Windows.Automation;
using Rex.Media.AppCore.Captions;

namespace Rex.Media.App.Tests;

public sealed partial class AppWindowTests
{
    [Fact]
    public void ReadableCorrectionsSurviveGeneratedTranscriptCacheClearing()
    {
        var media = Song(30);
        var root = Path.Combine(_media, "edited-only-reader-data");
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "settings.json"), $$"""
            { "firstRunDone": true, "lastSeenVersion": "{{AppProcess.Version}}", "updateChecks": "Off" }
            """);
        var cache = new TranscriptCache(TranscriptCache.DefaultRoot(root));
        var corrected = new Transcript("en", "base.en",
            [new TranscriptWord("preserved correction", TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(5))]);
        cache.SaveEdited(media, new TranscriptRevision(corrected,
            Rex.Media.AppCore.Transcription.TimeRanges.Of([
                new Rex.Media.AppCore.Transcription.TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(30))]),
            TimeSpan.FromSeconds(30), true));
        Assert.Null(cache.Load(media, "base.en"));

        using var app = AppProcess.Start([media], root);
        var menu = (ExpandCollapsePattern)app.Find("SubtitleMenu")
            .GetCurrentPattern(ExpandCollapsePattern.Pattern);
        menu.Expand();
        app.Press("ReadTranscript");
        Wait.For(() => app.IsShown("TranscriptReaderCue-0") &&
            app.Text("TranscriptReaderCoverage").Contains("generated original is not available", StringComparison.Ordinal),
            "the standalone corrected transcript to be readable after original-cache clearing");
        Assert.Equal(0, app.Close());
    }

    [Fact]
    public void ReaderFindsSavedSpeechFromAnotherModelWithoutRetranscribingOrAutoplay()
    {
        var media = Song(30);
        var root = Path.Combine(_media, "other-model-reader-data");
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "settings.json"), $$"""
            { "firstRunDone": true, "lastSeenVersion": "{{AppProcess.Version}}", "updateChecks": "Off" }
            """);
        var cache = new TranscriptCache(TranscriptCache.DefaultRoot(root));
        cache.Save(media, new Transcript("en", "tiny.en",
            [new TranscriptWord("fallback", TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(3))]));

        using var app = AppProcess.Start([media], root);
        var menu = (ExpandCollapsePattern)app.Find("SubtitleMenu")
            .GetCurrentPattern(ExpandCollapsePattern.Pattern);
        menu.Expand();
        app.Press("ReadTranscript");
        Wait.For(() => app.IsShown("TranscriptReaderCue-0")
            && app.Text("TranscriptReaderCoverage").Contains("another locally saved model", StringComparison.Ordinal),
            "the reader to find existing words without a download or model switch");
        Assert.Equal(0, app.Close());
    }

    [Fact]
    public void IncompleteSpeechShowsPartialCoverageWithoutPretendingAllWordsWereHeard()
    {
        var media = Song(35);
        var root = Path.Combine(_media, "partial-speech-reader-data");
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "settings.json"), $$"""
            { "firstRunDone": true, "lastSeenVersion": "{{AppProcess.Version}}", "updateChecks": "Off" }
            """);
        var cache = new TranscriptCache(TranscriptCache.DefaultRoot(root));
        var partial = new Rex.Media.AppCore.Transcription.ProgressiveTranscript(
            "base.en", "en", TimeSpan.FromSeconds(35));
        partial.Add(new Rex.Media.AppCore.Transcription.SpeechChunk(
                new Rex.Media.AppCore.Transcription.TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(14)),
                new Rex.Media.AppCore.Transcription.TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(14))),
            [new TranscriptWord("partial", TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(3))],
            "en");
        cache.SavePartial(media, partial);
        var info = new FileInfo(media);
        var ledger = new Rex.Media.AppCore.Transcription.TranscriptionLedger(
            Rex.Media.AppCore.Transcription.TranscriptionLedger.DefaultPath(root));
        ledger.Set(new Rex.Media.AppCore.Transcription.TranscriptRecord
        {
            Path = media,
            Signature = Rex.Media.AppCore.Transcription.TranscriptSignature.Of(
                info.Length, info.LastWriteTimeUtc),
            Model = "base.en",
            State = Rex.Media.AppCore.Transcription.TranscriptState.Failed,
            Reason = "Model unavailable",
        });

        using var app = AppProcess.Start([media], root);
        Screenshots.PinViewport(app, 1000, 760);
        var menu = (ExpandCollapsePattern)app.Find("SubtitleMenu")
            .GetCurrentPattern(ExpandCollapsePattern.Pattern);
        menu.Expand();
        app.Press("ReadTranscript");
        Wait.For(() => app.Text("TranscriptReaderCoverage").Contains("Partial transcript", StringComparison.Ordinal)
            && app.Text("TranscriptReaderProblem") == "Transcription stopped: Model unavailable"
            && app.IsShown("TranscriptReaderCue-0"),
            "partial speech to remain readable with a clearly labeled failed transcription job");
        // Actual WinUI capture; CI artifacts provide the first review image. No mock is substituted.
        Screenshots.Match(app, "spoken-transcript-partial");
        Assert.Equal(0, app.Close());
    }

    [Fact]
    public void SavedSpokenTranscriptHasPaginatedKeyboardAccessibleReadingAndSearch()
    {
        var media = Song(40);
        var root = Path.Combine(_media, "transcript-reader-data");
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "settings.json"), $$"""
            { "firstRunDone": true, "lastSeenVersion": "{{AppProcess.Version}}", "updateChecks": "Off" }
            """);
        var cache = new TranscriptCache(TranscriptCache.DefaultRoot(root));
        cache.Save(media, new Transcript("en", "base.en",
            Enumerable.Range(0, 48).Select(number =>
                new TranscriptWord(number == 46 ? "target." : $"speech{number}.",
                    TimeSpan.FromMilliseconds(number * 600),
                    TimeSpan.FromMilliseconds(number * 600 + 350))).ToArray()));

        using var app = AppProcess.Start([media], root);
        var menu = (ExpandCollapsePattern)app.Find("SubtitleMenu").GetCurrentPattern(ExpandCollapsePattern.Pattern);
        menu.Expand();
        app.Press("ReadTranscript");
        Wait.For(() => app.Text("TranscriptReaderPage").Contains("1–35 of 48", StringComparison.Ordinal),
            "first page of readable transcript paragraphs");
        Assert.Contains("Saved transcript", app.Text("TranscriptReaderCoverage"), StringComparison.Ordinal);
        Assert.True(app.IsShown("TranscriptReaderCue-0"));
        app.Press("TranscriptReaderNext");
        Wait.For(() => app.Text("TranscriptReaderPage").Contains("36–48 of 48", StringComparison.Ordinal),
            "the second bounded page of transcript paragraphs");
        // Keyboard navigation must reveal paragraphs beyond the scroll viewport.
        app.Find("TranscriptReaderCue-46").SetFocus();
        Wait.For(() => app.IsShown("TranscriptReaderCue-46"),
            "the keyboard-focused paragraph on page two to scroll into view");

        var search = (ValuePattern)app.Find("TranscriptReaderSearch").GetCurrentPattern(ValuePattern.Pattern);
        search.SetValue("TARGET");
        app.Press("TranscriptReaderFind");
        Wait.For(() => app.Text("TranscriptReaderResult").Contains("Match at", StringComparison.Ordinal),
            "finding spoken words by their saved timing");
        app.Press("TranscriptReaderCue-46");
        search.SetValue("missing phrase");
        app.Press("TranscriptReaderFind");
        Wait.For(() => app.Text("TranscriptReaderResult").Contains("No matching speech", StringComparison.Ordinal),
            "a truthful no-results state");
        Assert.Equal(0, app.Close());
    }
}
