using System.Collections.Concurrent;
using System.Text;
using Rex.Media.AppCore.Captions;
using Rex.Media.AppCore.Transcription;
using Rex.Media.IO;

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

    private void WriteTranscriptSrt(string path, Transcript transcript, bool edited = false)
    {
        try
        {
            if (edited)
            {
                AtomicFile.Write(Transcript.SidecarSrtPath(path, transcript.Model, edited: true), Encoding.UTF8.GetBytes(transcript.ToSrt()));
            }
            else
            {
                transcript.WriteSidecarSrt(path);
            }
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

    /// <summary>Only known media and models are eligible for removal; never search arbitrary folders.</summary>
    private void CleanGeneratedTranscriptSrts()
    {
        var records = _ledger.All.ToArray();
        var media = records.Select(record => record.Path)
            .Concat((_library?.Entries ?? []).Select(entry => entry.Path))
            .Append(_player.Item?.Location ?? "")
            .Where(File.Exists)
            .ToArray();
        var models = Enum.GetValues<Rex.Media.Settings.TranscriptModelChoice>()
            .Select(choice => SpeechModels.For(choice).Id)
            .Concat(records.Select(record => record.Model))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        Forget(Task.Run(() => TranscriptSidecarCleanup.Remove(media, models)).ContinueWith(task => OnWindowThread(() =>
        {
            if (task.IsCompletedSuccessfully)
            {
                var result = task.Result;
                Say($"Removed {result.Removed} generated SRT copies" + (result.Failed > 0 ? $"; {result.Failed} could not be removed." : "."));
            }
            else
            {
                App.Log.Warning(LogSource, "Cleaning generated SRT copies failed: " + task.Exception);
                Say("The generated SRT copies could not be cleaned up.");
            }
        }), TaskScheduler.Default));
    }

    private void ExportCachedTranscriptSrts()
    {
        foreach (var record in _ledger.All)
        {
            if (_transcripts.Load(record.Path, record.Model) is { IsEmpty: false } transcript)
            {
                WriteTranscriptSrt(record.Path, transcript);
            }

            if (_transcripts.LoadEdited(record.Path, record.Model) is { Content.IsEmpty: false } revision)
            {
                WriteTranscriptSrt(record.Path, revision.Content, edited: true);
            }
        }
    }
}
