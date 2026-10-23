using System.IO;
using System.Windows.Automation;
using Rex.Media.AppCore.Captions;

namespace Rex.Media.App.Tests;

public sealed partial class AppWindowTests
{
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
        Assert.True(app.IsShown("TranscriptReaderCue-46"));

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
