using Rex.Media.Engine;
using Rex.Media.Primitives;

namespace Rex.Media.App.Mac;

/// <summary>
/// Keep a movie's display awake and an audio-only session running while playing.
/// Inactive, paused, idle and ended sessions immediately release the operating-system activity.
/// </summary>
public sealed partial class MainWindow
{
    private NSObject? _playbackActivity;
    private bool _activityForVideo;

    private void UpdatePlaybackActivity()
    {
        var playing = _player.State is SessionState.Playing or SessionState.Buffering
            or SessionState.Seeking;
        var video = _player.Info?.FirstTrack(MediaKind.Video) is not null;
        if (_playbackActivity is not null && (!playing || _activityForVideo != video))
        {
            NSProcessInfo.ProcessInfo.EndActivity(_playbackActivity);
            _playbackActivity.Dispose();
            _playbackActivity = null;
        }

        if (!playing || _playbackActivity is not null)
        {
            return;
        }

        var policy = video
            ? NSActivityOptions.IdleDisplaySleepDisabled | NSActivityOptions.IdleSystemSleepDisabled
            : NSActivityOptions.IdleSystemSleepDisabled;
        _playbackActivity = NSProcessInfo.ProcessInfo.BeginActivity(policy,
            video ? "rexplayer video playback" : "rexplayer audio playback");
        _activityForVideo = video;
    }

    private void DisposePlaybackActivity()
    {
        if (_playbackActivity is not null)
        {
            NSProcessInfo.ProcessInfo.EndActivity(_playbackActivity);
            _playbackActivity.Dispose();
            _playbackActivity = null;
        }
    }
}
