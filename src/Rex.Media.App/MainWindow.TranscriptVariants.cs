using Rex.Media.AppCore.Captions;
using Rex.Media.Settings;
using Rex.Media.Subtitles;

namespace Rex.Media.App;

/// <summary>Loads each speech quality's independent original and edited tracks off the window thread.</summary>
public sealed partial class MainWindow
{
    private void QueueTranscriptVariants(string media, long generation)
    {
        var active = SpeechModels.For(_settings.KineticCaptions.TranscriptModel).Id;
        _ = Task.Run(() =>
        {
            var tracks = new List<SubtitleTrack>();
            foreach (var choice in Enum.GetValues<TranscriptModelChoice>())
            {
                var model = SpeechModels.For(choice).Id;
                // The active original is owned by the live worker and must not be added twice.
                if (model != active)
                {
                    var generated = _transcripts.Load(media, model)
                        ?? _transcripts.LoadPartial(media, model)?.ToTranscript();
                    if (generated is { IsEmpty: false })
                    {
                        tracks.Add(generated.ToTrack(new TranscriptVariant(model, false).TrackName));
                    }
                }

                if (_transcripts.LoadEdited(media, model) is { Content.IsEmpty: false } edited)
                {
                    tracks.Add(edited.Content.ToTrack(new TranscriptVariant(model, true).TrackName));
                }
            }

            return tracks;
        }).ContinueWith(task => OnWindowThread(() =>
        {
            if (!task.IsCompletedSuccessfully || generation != _transcriptGeneration || _player.Item?.Location != media)
            {
                return;
            }

            foreach (var track in task.Result)
            {
                _player.AddSubtitles(track, show: false);
            }
        }), TaskScheduler.Default);
    }
}
