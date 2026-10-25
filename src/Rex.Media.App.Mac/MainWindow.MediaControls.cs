using MediaPlayer;
using Rex.Media.AppCore.Commands;

namespace Rex.Media.App.Mac;

/// <summary>
/// Publish the active item and transport to macOS Control Center, headphones and media keys.
/// Remote commands marshal onto AppKit's UI thread before touching the shared player.
/// </summary>
public sealed partial class MainWindow
{
    private readonly List<(MPRemoteCommand Command, NSObject Target)> _mediaTargets = [];
    private DateTimeOffset _lastMediaPublish;
    private string? _lastMediaLocation;
    private bool _lastMediaPlaying;
    private byte[]? _lastNowPlayingArtSource;
    private NSImage? _lastNowPlayingArtImage;
    private MPMediaItemArtwork? _lastNowPlayingArtwork;

    private void StartMediaControls()
    {
        if (_mediaTargets.Count > 0)
        {
            return;
        }

        var commands = MPRemoteCommandCenter.Shared;
        ConnectMediaCommand(commands.TogglePlayPauseCommand, CommandCatalog.PlayPause);
        ConnectMediaCommand(commands.NextTrackCommand, CommandCatalog.Next);
        ConnectMediaCommand(commands.PreviousTrackCommand, CommandCatalog.Previous);
        ConnectMediaCommand(commands.StopCommand, CommandCatalog.Stop);
        ConnectMediaCommand(commands.PlayCommand, CommandCatalog.PlayPause, onlyWhenPaused: true);
        ConnectMediaCommand(commands.PauseCommand, CommandCatalog.PlayPause, onlyWhenPlaying: true);
        UpdateSystemNowPlaying(force: true);
    }

    private void ConnectMediaCommand(MPRemoteCommand command, string id,
        bool onlyWhenPaused = false, bool onlyWhenPlaying = false)
    {
        command.Enabled = true;
        var token = command.AddTarget(_ =>
        {
            OnWindowThread(() =>
            {
                if (_player.Item is null)
                {
                    return;
                }

                if ((!onlyWhenPaused || !_player.IsPlaying) &&
                    (!onlyWhenPlaying || _player.IsPlaying))
                {
                    Run(id);
                }

                UpdateSystemNowPlaying(force: true);
            });
            return MPRemoteCommandHandlerStatus.Success;
        });
        _mediaTargets.Add((command, token));
    }

    private void UpdateSystemNowPlaying(bool force = false)
    {
        var location = _player.Item?.Location;
        var playing = _player.IsPlaying;
        var now = DateTimeOffset.UtcNow;
        if (!force && location == _lastMediaLocation && playing == _lastMediaPlaying &&
            now - _lastMediaPublish < TimeSpan.FromSeconds(1))
        {
            return;
        }

        _lastMediaLocation = location;
        _lastMediaPlaying = playing;
        _lastMediaPublish = now;
        var center = MPNowPlayingInfoCenter.DefaultCenter;
        if (location is null)
        {
            center.NowPlaying = new MPNowPlayingInfo();
            center.PlaybackState = MPNowPlayingPlaybackState.Stopped;
            return;
        }

        var metadata = new MPNowPlayingInfo
        {
            Title = _player.Title,
            Artist = _player.Artist ?? "",
            ElapsedPlaybackTime = Math.Max(0, _player.Position.TotalSeconds),
            PlaybackRate = playing ? _player.Speed : 0,
        };
        if (_player.Duration > TimeSpan.Zero)
        {
            metadata.PlaybackDuration = _player.Duration.TotalSeconds;
        }

        if (!ReferenceEquals(_lastNowPlayingArtSource, _player.CoverArt))
        {
            _lastNowPlayingArtwork?.Dispose();
            _lastNowPlayingArtwork = null;
            _lastNowPlayingArtImage?.Dispose();
            _lastNowPlayingArtImage = null;
            _lastNowPlayingArtSource = _player.CoverArt;
            if (_lastNowPlayingArtSource is { Length: > 0 } art)
            {
                try
                {
                    using var bytes = NSData.FromArray(art);
                    var image = new NSImage(bytes);
                    _lastNowPlayingArtImage = image;
                    _lastNowPlayingArtwork = new MPMediaItemArtwork(image.Size, _ => image);
                }
                catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
                {
                    _log.Warning(LogSource, "Media-key artwork could not be decoded: " + ex.Message);
                }
            }
        }

        metadata.Artwork = _lastNowPlayingArtwork;
        center.NowPlaying = metadata;
        center.PlaybackState = playing ? MPNowPlayingPlaybackState.Playing
            : MPNowPlayingPlaybackState.Paused;
    }

    private void StopMediaControls()
    {
        foreach (var (command, target) in _mediaTargets)
        {
            command.RemoveTarget(target);
        }

        _mediaTargets.Clear();
        _lastNowPlayingArtwork?.Dispose();
        _lastNowPlayingArtwork = null;
        _lastNowPlayingArtImage?.Dispose();
        _lastNowPlayingArtImage = null;
        _lastNowPlayingArtSource = null;
        MPNowPlayingInfoCenter.DefaultCenter.NowPlaying = new MPNowPlayingInfo();
        MPNowPlayingInfoCenter.DefaultCenter.PlaybackState = MPNowPlayingPlaybackState.Stopped;
    }
}
