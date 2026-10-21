using System.Collections.Concurrent;
using Rex.Media.AppCore.Captions;
using Rex.Media.AppCore.Transcription;

namespace Rex.Media.App;

/// <summary>Writes live and cached model-labelled SubRip sidecars without changing media playback.</summary>
public sealed partial class MainWindow
{
    private readonly ConcurrentDictionary<string, byte> _srtWriteFailures = new(StringComparer.OrdinalIgnoreCase);

    private void WriteTranscriptSrt(TranscriptionWorker worker, string path)
    {
        if (!_settings.Transcription.WriteSrtBesideMedia || worker.Snapshot(path) is not { IsEmpty: false } transcript)
        {
            return;
        }

        WriteTranscriptSrt(path, transcript);
    }

    private void WriteTranscriptSrt(string path, Transcript transcript)
    {
        try
        {
            transcript.WriteSidecarSrt(path);
            _srtWriteFailures.TryRemove(path, out _);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            App.Log.Warning(LogSource, $"Writing the transcript SRT beside {path} failed: {error}");
            if (_srtWriteFailures.TryAdd(path, 0))
            {
                OnWindowThread(() => Say($"The transcript could not be saved beside {System.IO.Path.GetFileName(path)}: {error.Message}"));
            }
        }
    }

    private void ExportCachedTranscriptSrts()
    {
        foreach (var record in _ledger.All)
        {
            if (_transcripts.Load(record.Path, record.Model) is { IsEmpty: false } transcript)
            {
                WriteTranscriptSrt(record.Path, transcript);
            }
        }
    }
}
