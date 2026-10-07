using System.Globalization;
using Rex.Media.AppCore.Commands;
using Rex.Media.Engine;
using Rex.Media.IO;
using Rex.Media.Primitives;
using Rex.Media.Settings;

namespace Rex.Media.AppCore.Player;

public sealed partial class PlayerController
{
    /// <summary>Within this of the start, Previous goes to the previous item; later it restarts this one.</summary>
    public static readonly TimeSpan RestartThreshold = TimeSpan.FromSeconds(3);

    /// <summary>
    /// Runs a command that belongs to playback; returns false for commands the window handles itself
    /// (full screen, dialogs and the rest).
    /// </summary>
    public bool Execute(string commandId)
    {
        switch (commandId)
        {
            case CommandCatalog.PlayPause:
                PlayPause();
                break;
            case CommandCatalog.Stop:
                Stop();
                break;
            case CommandCatalog.Next:
                Start(Playlist.Next(automatic: false));
                break;
            case CommandCatalog.Previous:
                if (_session is not null && CanSeek && Position > RestartThreshold)
                {
                    Seek(TimeSpan.Zero);
                }
                else
                {
                    Start(Playlist.Previous());
                }

                break;
            case CommandCatalog.JumpForwardVeryShort or CommandCatalog.JumpBackVeryShort:
                Jump(Settings.VeryShortJumpSeconds, commandId == CommandCatalog.JumpForwardVeryShort);
                break;
            case CommandCatalog.JumpForwardShort or CommandCatalog.JumpBackShort:
                Jump(Settings.ShortJumpSeconds, commandId == CommandCatalog.JumpForwardShort);
                break;
            case CommandCatalog.JumpForwardMedium or CommandCatalog.JumpBackMedium:
                Jump(Settings.MediumJumpSeconds, commandId == CommandCatalog.JumpForwardMedium);
                break;
            case CommandCatalog.JumpForwardLong or CommandCatalog.JumpBackLong:
                Jump(Settings.LongJumpSeconds, commandId == CommandCatalog.JumpForwardLong);
                break;
            case CommandCatalog.VolumeUp or CommandCatalog.VolumeDown:
                var step = Settings.VolumeStepPercent / 100.0 * (commandId == CommandCatalog.VolumeUp ? 1 : -1);
                SetVolume(Math.Round((Volume + step) * 100) / 100);
                Say($"Volume {Math.Round(Volume * 100).ToString(CultureInfo.InvariantCulture)} %");
                break;
            case CommandCatalog.Mute:
                SetMuted(!Muted);
                Say(Muted ? "Muted" : "Sound on");
                break;
            case CommandCatalog.CycleRepeat:
                Playlist.Repeat = Playlist.Repeat switch { RepeatMode.Off => RepeatMode.All, RepeatMode.All => RepeatMode.One, _ => RepeatMode.Off };
                Say(Playlist.Repeat switch { RepeatMode.All => "Repeat all", RepeatMode.One => "Repeat one", _ => "Repeat off" });
                Changed?.Invoke(this, EventArgs.Empty);
                break;
            case CommandCatalog.ToggleShuffle:
                Playlist.Shuffle = !Playlist.Shuffle;
                Say(Playlist.Shuffle ? "Shuffle on" : "Shuffle off");
                Changed?.Invoke(this, EventArgs.Empty);
                break;
            case CommandCatalog.CycleAudioTrack:
                CycleAudioTrack();
                break;
            case CommandCatalog.ShowPosition:
                Say(Duration > TimeSpan.Zero ? $"{TimeText.Format(Position, Duration)} / {TimeText.Format(Duration)}" : TimeText.Format(Position));
                break;
            default:
                return false;
        }

        return true;
    }

    private void PlayPause()
    {
        if (_session is not null && State is not (SessionState.Idle or SessionState.Ended or SessionState.Faulted))
        {
            _ = Observe(_session.TogglePauseAsync());
            return;
        }

        // Nothing playing: start the current entry again, or the playlist from its top.
        if (Playlist.Current is { } current)
        {
            Start(current);
        }
        else if (Playlist.Items.Count > 0)
        {
            Start(Playlist.JumpTo(0));
        }
    }

    /// <summary>Moves to the next audio track, carrying on from the same moment.</summary>
    private void CycleAudioTrack()
    {
        var tracks = AudioTracks;
        if (tracks.Count < 2 || Item is null)
        {
            Say(tracks.Count == 1 ? "There is only one audio track." : "There are no audio tracks to choose from.");
            return;
        }

        var current = tracks.ToList().FindIndex(track => track.Id == AudioTrack);
        var next = tracks[(current + 1) % tracks.Count];
        var position = Position;
        Start(Item, position, next.Id);
        var name = next.Title ?? next.Language ?? next.Codec.DisplayName();
        Say($"Audio track {tracks.ToList().IndexOf(next) + 1} of {tracks.Count}: {name}");
    }

    private void Jump(int seconds, bool forward)
    {
        if (CanSeek)
        {
            Seek(Position + TimeSpan.FromSeconds(forward ? seconds : -seconds));
            Say(TimeText.Format(Position, Duration));
        }
    }

    private void Say(string text) => Message?.Invoke(this, text);

    private void OnEvent(MediaSession session, SessionEvent sessionEvent)
    {
        if (session != _session)
        {
            return;
        }

        switch (sessionEvent)
        {
            case StateChangedEvent changed:
                State = changed.To;
                Changed?.Invoke(this, EventArgs.Empty);
                break;
            case MediaOpenedEvent opened:
                if (_openedInSession && _queued is not null)
                {
                    // The queued item has begun, without a gap: the playlist moves on with it.
                    Item = Playlist.Next(automatic: true);
                    _queued = null;
                }

                _queueTried = false;
                _openedInSession = true;
                _failuresInARow = 0;
                Info = opened.Info;
                Duration = opened.Info.Duration.IsKnown ? opened.Info.Duration.ToTimeSpan() : TimeSpan.Zero;
                Changed?.Invoke(this, EventArgs.Empty);
                if (Settings.TitleSeconds > 0)
                {
                    Say(Title);
                }

                // An item shorter than the queueing distance may end before its first position report.
                QueueNextIfNearTheEnd(session);
                break;
            case TracksChangedEvent tracks:
                AudioTrack = tracks.AudioTrack;
                break;
            case PositionEvent position:
                Position = position.Position.ToTimeSpan();
                if (position.Duration.IsKnown)
                {
                    Duration = position.Duration.ToTimeSpan();
                }

                PositionChanged?.Invoke(this, EventArgs.Empty);
                QueueNextIfNearTheEnd(session);
                break;
            case ItemSkippedEvent skipped:
                // The queued item would not open: the playlist steps past it, and if the session has
                // already ended (it was queued too late to follow on) the item after it starts here.
                Item = Playlist.Next(automatic: true);
                _queued = null;
                Say($"{skipped.Name} could not be played: {skipped.Reason}");
                if (State == SessionState.Ended)
                {
                    StartNext();
                }

                break;
            case EndedEvent:
                Position = Duration;
                PositionChanged?.Invoke(this, EventArgs.Empty);

                // With an item still queued the engine starts it (or reports it skipped) by itself.
                if (_queued is null)
                {
                    StartNext();
                }

                break;
        }
    }

    private void StartNext()
    {
        if (Playlist.Next(automatic: true) is { } next)
        {
            Start(next);
        }
    }

    private void QueueNextIfNearTheEnd(MediaSession session)
    {
        if (_queued is not null || _queueTried || Duration <= TimeSpan.Zero || Duration - Position > QueueAhead || Playlist.PeekNext(automatic: true) is not { } next)
        {
            return;
        }

        IByteSource source;
        try
        {
            source = _openSource(next.Location);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            // Not tried again on every tick: at the end of this item, starting it on its own reports the failure.
            _queueTried = true;
            return;
        }

        _queueTried = true;
        _queued = next;
        _ = Observe(session.QueueNextAsync(source));
    }
}
