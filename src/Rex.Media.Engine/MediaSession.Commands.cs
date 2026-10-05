using Rex.Media.Audio;
using Rex.Media.Codecs;
using Rex.Media.Containers;
using Rex.Media.Diagnostics;
using Rex.Media.IO;
using Rex.Media.Primitives;

namespace Rex.Media.Engine;

/// <summary>The commands, each run on the mailbox thread.</summary>
public sealed partial class MediaSession
{
    private void Open(IByteSource source, MediaTime startAt)
    {
        if (_state != SessionState.Idle)
        {
            Stop();
        }

        Volatile.Write(ref _finished, NewCompletion());
        MoveTo(SessionState.Opening);
        IDemuxer? demuxer = null;
        IAudioDecoder? decoder = null;
        IAudioSink? sink = null;
        try
        {
            demuxer = _options.Demuxers.Open(source, CancellationToken.None);
            var info = demuxer.Info;
            _log.Info(LogSource, $"Opened {source.Name} as {info.FormatName} with {info.Tracks.Count} track(s).");
            _events.Post(new MediaOpenedEvent(info));

            var audioTrack = ChooseAudioTrack(info);
            if (audioTrack is null)
            {
                throw new NotSupportedException($"{source.Name} has no audio rexplayer can play yet.");
            }

            var decoded = _options.Decoders.CreateAudio(audioTrack);
            if (decoded.Decoder is null)
            {
                _events.Post(new TrackFailedEvent(audioTrack.Id, decoded.Reason!));
                throw new NotSupportedException(decoded.Reason);
            }

            decoder = decoded.Decoder;
            sink = _options.AudioSinkFactory();
            var audio = audioTrack.Audio!;
            var sinkFormat = sink.Open(new AudioFormat(audio.SampleRate, audio.Channels, SampleFormat.F32, audio.Layout));
            _log.Info(LogSource, $"Audio: {audioTrack.Codec.DisplayName()} via {decoder.Name} into {sink.Name} at {sinkFormat}.");

            _playback = new Playback(this, source, demuxer, info, audioTrack, decoder, sink, sinkFormat);
            _playback.ApplyVolume(_volume, _muted);
            _events.Post(new TracksChangedEvent(audioTrack.Id, null, null));
            if (startAt > MediaTime.Zero)
            {
                _playback.RequestSeek(startAt, SeekMode.Precise);
            }

            _playback.Start(_options.AutoPlay);
            MoveTo(_options.AutoPlay ? SessionState.Playing : SessionState.Ready);
        }
        catch (Exception ex) when (ex is MediaFormatException or NotSupportedException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            sink?.Dispose();
            decoder?.Dispose();
            demuxer?.Dispose();
            source.Dispose();
            _playback = null;
            Fail(ex is MediaFormatException or NotSupportedException ? ex.Message : ExceptionDiagnostics.Summary(ex));
            throw;
        }
    }

    private static TrackInfo? ChooseAudioTrack(MediaInfo info)
    {
        var audio = info.Tracks.Where(track => track.Kind == MediaKind.Audio && track.Audio is not null).ToList();
        return audio.FirstOrDefault(track => track.IsDefault) ?? audio.FirstOrDefault();
    }

    private void Play()
    {
        var playback = RequirePlayback();
        switch (_state)
        {
            case SessionState.Ended:
                playback.RequestSeek(MediaTime.Zero, SeekMode.Precise);
                playback.Resume();
                MoveTo(SessionState.Seeking);
                break;
            case SessionState.Ready or SessionState.Paused:
                playback.Resume();
                MoveTo(SessionState.Playing);
                break;
            case SessionState.Seeking:
                playback.Resume();
                break;
        }
    }

    private void Pause()
    {
        var playback = RequirePlayback();
        if (_state is SessionState.Playing or SessionState.Buffering or SessionState.Ready)
        {
            playback.Pause();
            MoveTo(SessionState.Paused);
        }
        else if (_state == SessionState.Seeking)
        {
            playback.Pause();
        }
    }

    private void Seek(MediaTime target, SeekMode mode)
    {
        var playback = RequirePlayback();
        if (_state is SessionState.Stopping or SessionState.Faulted or SessionState.Opening)
        {
            return;
        }

        var duration = playback.Info.Duration;
        var clamped = MediaTime.Max(MediaTime.Zero, duration.IsKnown ? MediaTime.Min(target, duration) : target);
        playback.RequestSeek(clamped, mode);
        MoveTo(SessionState.Seeking);
    }

    private void Stop()
    {
        if (_playback is null)
        {
            if (_state is not SessionState.Idle)
            {
                if (SessionStateMachine.CanMove(_state, SessionState.Stopping))
                {
                    MoveTo(SessionState.Stopping);
                }

                MoveTo(SessionState.Idle);
            }

            return;
        }

        MoveTo(SessionState.Stopping);
        _playback.Dispose();
        _playback = null;
        MoveTo(SessionState.Idle);
    }

    private Playback RequirePlayback() => _playback ?? throw new InvalidOperationException("Nothing is open.");

    /// <summary>Reports from the pipeline threads, applied on the mailbox thread.</summary>
    private void OnSeekCompleted(Playback playback, long generation, MediaTime position)
    {
        Enqueue(new Command(
            () =>
            {
                if (!ReferenceEquals(playback, _playback) || generation != playback.Generation || _state != SessionState.Seeking)
                {
                    return;
                }

                _events.Post(new SeekCompletedEvent(position));
                MoveTo(playback.IsPaused ? SessionState.Paused : SessionState.Playing);
            },
            null));
    }

    private void OnEnded(Playback playback, long generation)
    {
        Enqueue(new Command(
            () =>
            {
                if (!ReferenceEquals(playback, _playback) || generation != playback.Generation)
                {
                    return;
                }

                // Paused counts too: a seek to the very end while paused has nothing left to play.
                if (_state is SessionState.Playing or SessionState.Seeking or SessionState.Buffering or SessionState.Paused)
                {
                    _events.Post(new EndedEvent());
                    MoveTo(SessionState.Ended);
                }
            },
            null));
    }

    private void OnFailed(Playback playback, string message)
    {
        Enqueue(new Command(
            () =>
            {
                if (ReferenceEquals(playback, _playback))
                {
                    Fail(message);
                }
            },
            null));
    }

    private void OnTrackFailed(int trackId, string reason) => _events.Post(new TrackFailedEvent(trackId, reason));

    private void OnPosition(MediaTime position, MediaTime duration) => _events.Post(new PositionEvent(position, duration));
}
