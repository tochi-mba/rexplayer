using System.IO;
using System.Windows.Automation;
using Rex.Media.AppCore.Captions;

namespace Rex.Media.App.Tests;

public sealed partial class AppWindowTests
{
    [Fact]
    public void TranscriptEditorAutosavesCorrectedWordsWithoutChangingGeneratedSpeech()
    {
        var media = Song(30);
        var root = Path.Combine(_media, "transcript-editor-data");
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "settings.json"), $$"""
            { "firstRunDone": true, "lastSeenVersion": "{{AppProcess.Version}}", "updateChecks": "Off",
              "transcription": { "writeSrtBesideMedia": true } }
            """);
        var editedSidecar = Transcript.SidecarSrtPath(media, "base.en", edited: true);
        var cache = new TranscriptCache(TranscriptCache.DefaultRoot(root));
        cache.Save(media, new Transcript("en", "base.en",
            [new TranscriptWord("original", TimeSpan.Zero, TimeSpan.FromSeconds(1)),
             new TranscriptWord("words", TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2))]));

        using var app = AppProcess.Start([media], root);
        var menu = (ExpandCollapsePattern)app.Find("SubtitleMenu").GetCurrentPattern(ExpandCollapsePattern.Pattern);
        menu.Expand();
        app.Press("EditTranscript");
        var edit = (ValuePattern)app.Find("TranscriptEditWord-0").GetCurrentPattern(ValuePattern.Pattern);
        Assert.Equal("original", edit.Current.Value);

        // Search works on saved words, rather than requiring the whole transcript to be
        // materialized as editable controls; matching speech can also be sought precisely.
        var search = (ValuePattern)app.Find("TranscriptEditSearch").GetCurrentPattern(ValuePattern.Pattern);
        search.SetValue("words");
        app.Press("TranscriptEditFindNext");
        Wait.For(() => app.Text("TranscriptEditMatch").Contains("word 2 of 2", StringComparison.Ordinal),
            "the transcript search to locate the second timed word");
        Assert.True(app.IsShown("TranscriptEditJump"));
        app.Press("TranscriptEditJump");
        search.SetValue("absent phrase");
        app.Press("TranscriptEditFindNext");
        Wait.For(() => app.Text("TranscriptEditMatch").Contains("No matching speech", StringComparison.Ordinal),
            "a clear empty search result");

        // Find rebuilds the current page, so the old automation element is stale.
        var revised = (ValuePattern)app.Find("TranscriptEditWord-0").GetCurrentPattern(ValuePattern.Pattern);
        revised.SetValue("corrected");
        Wait.For(() => cache.LoadEdited(media, "base.en")?.Content.Words[0].Text == "corrected",
            "the corrected speech to be saved");
        Wait.For(() => File.Exists(editedSidecar) && File.ReadAllText(editedSidecar).Contains("corrected", StringComparison.Ordinal),
            "the edited sidecar to match its model-specific correction");
        Assert.Equal("original", cache.Load(media, "base.en")!.Words[0].Text);

        // Find works on original and edited speech while leaving the original
        // model file unchanged, and exposes the exact source timestamp.
        var searchAgain = (ValuePattern)app.Find("TranscriptEditSearch").GetCurrentPattern(ValuePattern.Pattern);
        searchAgain.SetValue("words");
        app.Press("TranscriptEditFindNext");
        Wait.For(() => app.Text("TranscriptEditMatch").Contains("word 2 of 2", StringComparison.Ordinal),
            "the transcript search to find its second source-timed word");
        Assert.True(app.IsShown("TranscriptEditJump"));

        app.Press("TranscriptEditUndo");
        Wait.For(() => cache.LoadEdited(media, "base.en") is null, "undo to restore the generated original");
        app.Press("TranscriptEditRedo");
        Wait.For(() => cache.LoadEdited(media, "base.en")?.Content.Words[0].Text == "corrected",
            "redo to restore the correction");
        app.Press("TranscriptEditReset");
        Wait.For(() => cache.LoadEdited(media, "base.en") is null, "reset to remove corrections");
        Wait.For(() => !File.Exists(editedSidecar), "reset to remove its edited sidecar");
        Assert.Equal("original", cache.Load(media, "base.en")!.Words[0].Text);
        Assert.Equal(0, app.Close());
    }

    [Fact]
    public void FailedTranscriptSidecarSaveIsExplainedAndCanBeRetriedBeforeClosing()
    {
        var media = Song(30);
        var root = Path.Combine(_media, "transcript-error-data");
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "settings.json"), $"""
            { "firstRunDone": true, "lastSeenVersion": "{{AppProcess.Version}}", "updateChecks": "Off",
              "transcription": { "writeSrtBesideMedia": true } }
            """);
        var cache = new TranscriptCache(TranscriptCache.DefaultRoot(root));
        cache.Save(media, new Transcript("en", "base.en",
            [new TranscriptWord("original", TimeSpan.Zero, TimeSpan.FromSeconds(1))]));

        var target = Transcript.SidecarSrtPath(media, "base.en", edited: true);
        Directory.CreateDirectory(target); // A directory at the destination rejects a file replacement.
        using var app = AppProcess.Start([media], root);
        ((ExpandCollapsePattern)app.Find("SubtitleMenu").GetCurrentPattern(ExpandCollapsePattern.Pattern)).Expand();
        app.Press("EditTranscript");
        var word = (ValuePattern)app.Find("TranscriptEditWord-0").GetCurrentPattern(ValuePattern.Pattern);
        word.SetValue("corrected");
        Wait.For(() => app.IsShown("TranscriptEditRetry"), "failed autosave to expose an explicit retry");
        Assert.StartsWith("The transcript edit could not be saved", app.Text("TranscriptEditSaveState"),
            StringComparison.Ordinal);

        app.Press("CloseButton");
        Wait.For(() => app.IsShown("TranscriptEditRetry") && app.IsShown("TranscriptEditWord-0"),
            "unsaved corrections to keep the dialog open");

        Directory.Delete(target);
        app.Press("TranscriptEditRetry");
        Wait.For(() => File.Exists(target)
            && app.Text("TranscriptEditSaveState") == "Transcript edits saved.",
            "the corrected transcript to save after its destination becomes writable");
        Assert.Contains("corrected", File.ReadAllText(target), StringComparison.Ordinal);
        Assert.Equal("original", cache.Load(media, "base.en")!.Words[0].Text);
        app.Press("CloseButton");
        Wait.For(() => !app.IsShown("TranscriptEditWord-0"), "the saved editor to dismiss");
        Assert.Equal(0, app.Close());
    }
}
