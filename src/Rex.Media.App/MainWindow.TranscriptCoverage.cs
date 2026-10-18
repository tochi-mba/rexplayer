using Rex.Media.AppCore.Captions;
using Rex.Media.AppCore.Transcription;
using Rex.Media.Subtitles;

namespace Rex.Media.App;

/// <summary>Exact saved coverage for a selected model/edition, independent of what the worker is hearing.</summary>
public sealed partial class MainWindow
{
    private TranscriptVariant? _coverageVariant;
    private TimeRanges? _variantDone;
    private TimeSpan? _variantDuration;
    private int _variantGeneration;

    private TimeRanges? DisplayedCoverage => _coverageVariant is null ? _bufferDone : _variantDone;
    private TimeSpan DisplayedDuration => _coverageVariant is null
        ? _bufferDuration ?? _player.Duration : _variantDuration ?? _player.Duration;
    private bool DisplayedHearing => _coverageVariant is null && _bufferHearing;


    private void SelectTranscriptCoverage(SubtitleTrack? track)
    {
        var generation = ++_variantGeneration;
        var variant = track is null ? null : TranscriptVariant.FromTrackName(track.Name);
        var activeModel = TranscriptModel.Id;
        if (variant is null || (variant.Value.Model == activeModel && !variant.Value.Edited))
        {
            _coverageVariant = null;
            _variantDone = null;
            _variantDuration = null;
            ShowSubtitleBuffer();
            HoldForSubtitles();
            return;
        }

        _coverageVariant = variant;
        _variantDone = null;
        _variantDuration = null;
        // Never leave the previous edition's coverage on screen while the new one loads.
        ShowSubtitleBuffer();
        HoldForSubtitles();
        if (_player.Item?.Location is not { } media)
        {
            return;
        }

        var duration = _player.Duration;
        _ = Task.Run(() =>
        {
            if (variant.Value.Edited)
            {
                return _transcripts.LoadEdited(media, variant.Value.Model);
            }

            var complete = _transcripts.Load(media, variant.Value.Model);
            var partial = complete is null ? _transcripts.LoadPartial(media, variant.Value.Model) : null;
            if (complete is null && partial is null)
            {
                return null;
            }

            return new TranscriptRevision(complete ?? partial!.ToTranscript(),
                Rex.Media.AppCore.Transcription.SubtitleBuffer.Coverage(complete is not null, partial?.Done, requested: false, duration) ?? TimeRanges.Empty,
                partial?.Duration ?? duration, complete is not null);
        }).ContinueWith(task => OnWindowThread(() =>
        {
            if (!task.IsCompletedSuccessfully || generation != _variantGeneration ||
                _player.Item?.Location != media || _player.Subtitles?.Name != track?.Name)
            {
                return;
            }

            _variantDone = task.Result?.Done;
            _variantDuration = task.Result?.Duration;
            ShowSubtitleBuffer();
            HoldForSubtitles();
        }), TaskScheduler.Default);
    }
}
