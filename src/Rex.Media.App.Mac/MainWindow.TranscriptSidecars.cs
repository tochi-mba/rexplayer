using System.Collections.Concurrent;
using System.Text;
using Rex.Media.AppCore.Captions;
using Rex.Media.AppCore.Transcription;
using Rex.Media.IO;
using Rex.Media.Settings;

namespace Rex.Media.App.Mac;

/// <summary>
/// Keeps the model-labelled original and edited SubRip copies beside transcribed media when the
/// setting asks for them, named and cleaned up by the portable transcript rules, as on Windows.
/// </summary>
public sealed partial class MainWindow
{
    private readonly ConcurrentDictionary<string, byte> _macSrtWriteFailures = new(StringComparer.OrdinalIgnoreCase);

    private void WriteMacTranscriptSrt(TranscriptionWorker worker, string path)
    {
        if (!_settings.Transcription.WriteSrtBesideMedia || worker.Snapshot(path) is not { IsEmpty: false } transcript)
        {
            return;
        }

        WriteMacTranscriptSrt(path, transcript);
    }

    private void WriteMacTranscriptSrt(string path, Transcript transcript, bool edited = false)
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

            _macSrtWriteFailures.TryRemove(path, out _);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            _log.Warning(LogSource, $"Writing the transcript SRT beside {path} failed: {error.Message}");
            if (_macSrtWriteFailures.TryAdd(path, 0))
            {
                OnWindowThread(() => Say($"The transcript could not be saved beside {Path.GetFileName(path)}: {error.Message}"));
            }
        }
    }

    /// <summary>Writes the copies of every transcript already saved, once the setting is turned on.</summary>
    private void ExportMacCachedTranscriptSrts()
    {
        foreach (var record in _macLedger.All)
        {
            if (_macTranscriptCache.Load(record.Path, record.Model) is { IsEmpty: false } transcript)
            {
                WriteMacTranscriptSrt(record.Path, transcript);
            }

            if (_macTranscriptCache.LoadEdited(record.Path, record.Model) is { Content.IsEmpty: false } revision)
            {
                WriteMacTranscriptSrt(record.Path, revision.Content, edited: true);
            }
        }
    }

    /// <summary>Only known media and models are eligible for removal; arbitrary folders are never searched.</summary>
    private void CleanMacGeneratedTranscriptSrts()
    {
        var records = _macLedger.All.ToArray();
        var media = records.Select(record => record.Path)
            .Concat((_mediaLibrary?.Entries ?? []).Select(entry => entry.Path))
            .Append(_player.Item?.Location ?? "")
            .ToArray();
        var models = Enum.GetValues<TranscriptModelChoice>()
            .Select(choice => SpeechModels.For(choice).Id)
            .Concat(records.Select(record => record.Model))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        _ = Task.Run(() => TranscriptSidecarCleanup.Remove(media, models)).ContinueWith(task => OnWindowThread(() =>
        {
            if (task.IsCompletedSuccessfully)
            {
                var result = task.Result;
                Say($"Removed {result.Removed} generated SRT copies" + (result.Failed > 0 ? $"; {result.Failed} could not be removed." : "."));
            }
            else
            {
                _log.Warning(LogSource, "Cleaning generated SRT copies failed: " + task.Exception?.GetBaseException().Message);
                Say("The generated SRT copies could not be cleaned up.");
            }
        }), TaskScheduler.Default);
    }
}
