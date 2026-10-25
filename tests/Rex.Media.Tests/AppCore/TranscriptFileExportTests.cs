using Rex.Media.AppCore.Captions;
using Rex.Media.TestKit;

namespace Rex.Media.Tests.AppCore;

public sealed class TranscriptFileExportTests
{
    private static Transcript Example => new("en", "base.en",
    [
        new TranscriptWord("مرحبا", TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1.5)),
        new TranscriptWord("🎵 world", TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2.7)),
    ]);

    [Fact]
    [Capability("SUB-19")]
    public void SrtVttLrcAndJsonExportsAreCompleteAndNeverChangeTheOriginalModel()
    {
        var folder = Path.Combine(Path.GetTempPath(), "rexplayer-export-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(folder);
            var original = Example;
            foreach (var extension in new[] { ".srt", ".vtt", ".lrc", ".json" })
            {
                var path = Path.Combine(folder, "words" + extension);
                TranscriptFileExport.Write(path, original);
                var result = File.ReadAllText(path);
                Assert.Contains("مرحبا", result, StringComparison.Ordinal);
                Assert.Contains("world", result, StringComparison.Ordinal);
                if (extension == ".vtt")
                {
                    Assert.StartsWith("WEBVTT", result, StringComparison.Ordinal);
                }
                else if (extension == ".json")
                {
                    Assert.Contains("\"base.en\"", result, StringComparison.Ordinal);
                }
                else if (extension == ".lrc")
                {
                    Assert.Contains("[00:01.00]<00:01.00>مرحبا", result, StringComparison.Ordinal);
                }
                else
                {
                    Assert.Contains("-->", result, StringComparison.Ordinal);
                }
            }

            Assert.Equal(["مرحبا", "🎵 world"], original.Words.Select(word => word.Text));
            Assert.Throws<NotSupportedException>(() =>
                TranscriptFileExport.Write(Path.Combine(folder, "words.exe"), original));
            Assert.False(File.Exists(Path.Combine(folder, "words.exe")));
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }

    [Fact]
    [Capability("SUB-19")]
    public void ExportFailureDoesNotPublishAnIncompleteTranscriptOverTheDestination()
    {
        var folder = Path.Combine(Path.GetTempPath(), "rexplayer-export-error-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(folder);
            var blocked = Path.Combine(folder, "cannot-replace.srt");
            Directory.CreateDirectory(blocked);
            var error = Record.Exception(() => TranscriptFileExport.Write(blocked, Example));
            Assert.True(error is IOException or UnauthorizedAccessException,
                $"Expected a storage failure instead of a silently published transcript: {error}");
            Assert.True(Directory.Exists(blocked));
            Assert.False(File.Exists(blocked));
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }
}
