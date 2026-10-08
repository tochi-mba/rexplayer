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

    private const string LogSource = "player";

    /// <summary>What the log says about media as it opens: its container, length and every track.</summary>
    internal static string Describe(MediaInfo info) =>
        $"{info.FormatName}, {(info.Duration.IsKnown ? TimeText.Format(info.Duration.ToTimeSpan(), info.Duration.ToTimeSpan()) : "length unknown")}"
        + (info.IsSeekable ? "" : ", not seekable") + ". Tracks: "
        + string.Join("; ", info.Tracks.Select(track => $"#{track.Id} {track.Kind.ToString().ToLowerInvariant()} {track.Codec.DisplayName()}"
            + (track.Audio is { } audio ? $" {audio.SampleRate} Hz {audio.Channels} ch" : "")
            + (track.Video is { } video ? $" {video.Width}x{video.Height}" : "")
            + (track.Language is { } language ? $" [{language}]" : "")
            + (track.Title is { Length: > 0 } title ? $" \"{title}\"" : "")
            + (track.IsDefault ? " default" : "")
            + (track.IsForced ? " forced" : "")));

    /// <summary>
    /// Runs a command that belongs to playback; returns false for commands the window handles itself
    /// (full screen, dialogs and the rest).
    /// </summary>
    public bool Execute(string commandId)
    {
        _log.Debug(LogSource, "Command " + commandId);
        if (ExecuteSubtitles(commandId) || ExecuteMemory(commandId) || ExecuteTimers(commandId))
        {
            return true;
        }

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
            case CommandCatalog.Faster:
                SetSpeed(SpeedSteps.FirstOrDefault(step => step > Speed + 0.001, SpeedSteps[^1]));
                break;
            case CommandCatalog.Slower:
                SetSpeed(SpeedSteps.LastOrDefault(step => step < Speed - 0.001, SpeedSteps[0]));
                break;
            case CommandCatalog.NormalSpeed:
                SetSpeed(1);
                break;
            case CommandCatalog.SlightlyFaster or CommandCatalog.SlightlySlower:
                SetSpeed(Speed + (commandId == CommandCatalog.SlightlyFaster ? 0.1 : -0.1));
                break;
            case CommandCatalog.AudioEarlier or CommandCatalog.AudioLater:
                SetAudioDelay(AudioDelay + TimeSpan.FromMilliseconds(commandId == CommandCatalog.AudioLater ? 50 : -50));
                break;
            case CommandCatalog.ResetAudioDelay:
                SetAudioDelay(TimeSpan.Zero);
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

    /// <summary>The speeds Faster and Slower step through.</summary>
    public static IReadOnlyList<double> SpeedSteps { get; } = [0.25, 0.5, 0.75, 1, 1.25, 1.5, 2, 3, 4];

    /// <summary>
    /// Plays at <paramref name="speed"/> (kept between 0.25 and 4, to two places), pitch unchanged,
    /// for this item and the ones after it.
    /// </summary>
    public void SetSpeed(double speed)
    {
        Speed = Math.Round(Math.Clamp(double.IsFinite(speed) ? speed : 1, SpeedSteps[0], SpeedSteps[^1]), 2);
        if (_session is not null)
        {
            _ = Observe(_session.SetSpeedAsync(Speed));
        }

        Say(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"Speed {Speed:0.##}\u00D7"));
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Hears the sound <paramref name="delay"/> after the pictures (negative: before), within ten seconds either way.</summary>
    public void SetAudioDelay(TimeSpan delay)
    {
        AudioDelay = delay < -MediaSession.MaxAudioDelay ? -MediaSession.MaxAudioDelay : delay > MediaSession.MaxAudioDelay ? MediaSession.MaxAudioDelay : delay;
        if (_session is not null)
        {
            _session.AudioDelay = AudioDelay;
        }

        Say(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"Audio delay {AudioDelay.TotalMilliseconds:0} ms"));
        Changed?.Invoke(this, EventArgs.Empty);
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
                _log.Debug(LogSource, $"{changed.From} -> {changed.To}");
                State = changed.To;
                Changed?.Invoke(this, EventArgs.Empty);
                if (changed.To == SessionState.Faulted && Item is { } failed)
                {
                    OnFailed(failed, session.FailureReason ?? "It could not be played.");
                }

                break;
            case MediaOpenedEvent opened:
                if (_openedInSession && _queued is not null)
                {
                    // The queued item has begun, without a gap: the playlist moves on with it.
                    Item = Playlist.Next(automatic: true);
                    _log.Info(LogSource, $"Carried on without a gap into {Item!.Location}.");
                    _queued = null;
                    FindSubtitles(Item!);
                    if (HoldsAtTheEnd)
                    {
                        // Asked for after it was queued: the next item has begun, so it stops or pauses at once.
                        if (TakeAfter() == AfterItem.Stop)
                        {
                            Stop();
                            return;
                        }

                        _ = Observe(session.PauseAsync());
                    }
                }

                _queueTried = false;
                _openedInSession = true;
                _failuresInARow = 0;
                Info = opened.Info;
                _log.Info(LogSource, "Opened " + Describe(opened.Info));
                Duration = PartDuration(opened.Info.Duration);
                Memory.Played(Item!.Location);
                CountPlay(Item);
                FindPresentation(Item, opened.Info);
                AddEmbeddedSubtitles(opened.Info);
                Changed?.Invoke(this, EventArgs.Empty);
                if (Settings.TitleSeconds > 0)
                {
                    Say(Title);
                }

                // A track that could not be decoded (reported as the item opened): the rest plays, and the window says why.
                foreach (var trackFailure in _trackFailures)
                {
                    var kind = opened.Info.Tracks.FirstOrDefault(track => track.Id == trackFailure.TrackId)?.Kind;
                    Say((kind == MediaKind.Video ? "Playing without pictures: " : "Playing without sound: ") + trackFailure.Reason);
                }

                _trackFailures.Clear();

                // An item shorter than the queueing distance may end before its first position report.
                QueueNextIfNearTheEnd(session);
                break;
            case TrackFailedEvent trackFailed:
                _log.Warning(LogSource, $"Track {trackFailed.TrackId} cannot be played: {trackFailed.Reason}");
                _trackFailures.Add(trackFailed);
                break;
            case SubtitleCueEvent subtitle:
                Embedded(subtitle.Item, subtitle.TrackId).Add(subtitle.Cue);
                break;
            case TracksChangedEvent tracks:
                AudioTrack = tracks.AudioTrack;
                break;
            case PositionEvent position:
                var heard = position.Position.ToTimeSpan();
                if (Item?.End is { } end && heard >= end)
                {
                    ReachedEndOfPart(heard);
                    break;
                }

                Position = heard > Item!.Start ? heard - Item.Start : TimeSpan.Zero;
                if (position.Duration.IsKnown)
                {
                    Duration = PartDuration(position.Duration);
                }

                PositionChanged?.Invoke(this, EventArgs.Empty);
                QueueNextIfNearTheEnd(session);
                break;
            case ItemSkippedEvent skipped:
                // The queued item would not open: the playlist steps past it, and if the session has
                // already ended (it was queued too late to follow on) the item after it starts here.
                Item = Playlist.Next(automatic: true);
                _queued = null;
                _log.Warning(LogSource, $"Skipped {skipped.Name}: {skipped.Reason}");
                Say($"{skipped.Name} could not be played: {skipped.Reason}");
                if (State == SessionState.Ended)
                {
                    StartNext();
                }

                break;
            case EndedEvent:
                // The file ended: parts of it that ran on from this one (a cue sheet's later tracks)
                // have played too, even when their last position report was merged away.
                while (Item?.End is { } partEnd && Playlist.PeekNext(automatic: true) is { } carriedOn && carriedOn != Item
                    && carriedOn.Location == Item.Location && carriedOn.Start == partEnd)
                {
                    Item = Playlist.Next(automatic: true)!;
                    Duration = PartDuration(Info?.Duration ?? MediaTime.Unknown);
                    Changed?.Invoke(this, EventArgs.Empty);
                    if (Settings.TitleSeconds > 0)
                    {
                        Say(Title);
                    }
                }

                Position = Duration;
                _log.Info(LogSource, $"Reached the end of {Item?.Location}.");
                RememberPosition();
                PositionChanged?.Invoke(this, EventArgs.Empty);

                // With an item still queued the engine starts it (or reports it skipped) by itself.
                if (_queued is null)
                {
                    FinishItem(StartNext);
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

    /// <summary>How long the current item lasts: all of a file of <paramref name="whole"/>, or its part of it.</summary>
    private TimeSpan PartDuration(MediaTime whole)
    {
        var start = Item?.Start ?? TimeSpan.Zero;
        var end = Item?.End ?? (whole.IsKnown ? whole.ToTimeSpan() : null);
        return end is { } last && last > start ? last - start : TimeSpan.Zero;
    }

    /// <summary>
    /// A part of a file has played to its end. When the next item carries on in the same file from
    /// there, as a cue sheet's tracks do, it simply becomes the item, without a gap; otherwise this
    /// item is over, as if its file had ended.
    /// </summary>
    private void ReachedEndOfPart(TimeSpan heard)
    {
        var current = Item!;
        Position = Duration;
        RememberPosition();
        if (!HoldsAtTheEnd && Playlist.PeekNext(automatic: true) is { } next && next != current && next.Location == current.Location && next.Start == current.End)
        {
            Item = Playlist.Next(automatic: true)!;
            Position = heard - Item.Start;
            Duration = PartDuration(Info?.Duration ?? MediaTime.Unknown);
            Changed?.Invoke(this, EventArgs.Empty);
            PositionChanged?.Invoke(this, EventArgs.Empty);
            if (Settings.TitleSeconds > 0)
            {
                Say(Title);
            }

            return;
        }

        PositionChanged?.Invoke(this, EventArgs.Empty);
        FinishItem(() =>
        {
            if (Playlist.Next(automatic: true) is { } following)
            {
                Start(following);
                return;
            }

            EndSession();
            State = SessionState.Ended;
            Changed?.Invoke(this, EventArgs.Empty);
        });
    }

    private void QueueNextIfNearTheEnd(MediaSession session)
    {
        // A part that ends inside its file hands over by itself; an item that starts inside one cannot be queued.
        if (_queued is not null || _queueTried || HoldsAtTheEnd || Duration <= TimeSpan.Zero || Duration - Position > QueueAhead || Item?.End is not null
            || Playlist.PeekNext(automatic: true) is not { } next || next.Start > TimeSpan.Zero)
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
