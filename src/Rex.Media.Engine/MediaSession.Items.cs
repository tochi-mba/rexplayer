using Rex.Media.Codecs;
using Rex.Media.Containers;
using Rex.Media.IO;
using Rex.Media.Primitives;

namespace Rex.Media.Engine;

public sealed partial class MediaSession
{
    /// <summary>
    /// One piece of media in a gapless run: its source, demuxer and decoder and what they found.
    /// The demux thread reads from the newest item while the audio thread may still be decoding
    /// the one before, so an item is released only once both have moved past it; the playback's
    /// item list owns each one and releases it exactly once.
    /// </summary>
    private sealed class MediaItem : IDisposable
    {
        public required IByteSource Source { get; init; }

        public required IDemuxer Demuxer { get; init; }

        public required MediaInfo Info { get; init; }

        public required TrackInfo AudioTrack { get; init; }

        public required IAudioDecoder Decoder { get; init; }

        /// <summary>The picture track and its decoder, when pictures are wanted and one opened.</summary>
        public TrackInfo? VideoTrack { get; init; }

        public IVideoDecoder? VideoDecoder { get; init; }

        public void Dispose()
        {
            VideoDecoder?.Dispose();
            Decoder.Dispose();
            Demuxer.Dispose();
            Source.Dispose();
        }
    }

    /// <summary>
    /// Opens <paramref name="source"/> as an item: picks its demuxer, its audio track and a decoder.
    /// On failure everything opened so far (and the source) is released and the reason thrown.
    /// </summary>
    private MediaItem OpenItem(IByteSource source)
    {
        IDemuxer? demuxer = null;
        try
        {
            demuxer = _options.Demuxers.Open(source, CancellationToken.None);
            var info = demuxer.Info;
            _log.Info(LogSource, $"Opened {source.Name} as {info.FormatName} with {info.Tracks.Count} track(s).");
            var audioTrack = ChooseAudioTrack(info) ?? throw new NotSupportedException($"{source.Name} has no audio rexplayer can play yet.");
            var decoded = _options.Decoders.CreateAudio(audioTrack);
            if (decoded.Decoder is null)
            {
                _events.Post(new TrackFailedEvent(audioTrack.Id, decoded.Reason!));
                throw new NotSupportedException(decoded.Reason);
            }

            var (videoTrack, videoDecoder) = OpenVideo(info);
            return new MediaItem { Source = source, Demuxer = demuxer, Info = info, AudioTrack = audioTrack, Decoder = decoded.Decoder, VideoTrack = videoTrack, VideoDecoder = videoDecoder };
        }
        catch (Exception ex) when (ex is MediaFormatException or NotSupportedException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            demuxer?.Dispose();
            source.Dispose();
            throw;
        }
    }

    /// <summary>
    /// The picture track and a decoder for it, when pictures are wanted. A picture track that cannot
    /// be decoded is reported and left out: the sound still plays.
    /// </summary>
    private (TrackInfo? Track, IVideoDecoder? Decoder) OpenVideo(MediaInfo info)
    {
        var video = info.Tracks.Where(track => track.Kind == MediaKind.Video && track.Video is not null).ToList();
        if (_options.VideoPresenterFactory is null || (video.FirstOrDefault(track => track.IsDefault) ?? video.FirstOrDefault()) is not { } track)
        {
            return (null, null);
        }

        var decoded = _options.Decoders.CreateVideo(track);
        if (decoded.Decoder is null)
        {
            _log.Warning(LogSource, "The pictures cannot be shown: " + decoded.Reason);
            _events.Post(new TrackFailedEvent(track.Id, decoded.Reason!));
            return (null, null);
        }

        return (track, decoded.Decoder);
    }

    private static TrackInfo? ChooseAudioTrack(MediaInfo info)
    {
        var audio = info.Tracks.Where(track => track.Kind == MediaKind.Audio && track.Audio is not null).ToList();
        return audio.FirstOrDefault(track => track.IsDefault) ?? audio.FirstOrDefault();
    }

    /// <summary>Adds media to play straight after what is playing, or opens it if nothing is.</summary>
    private void QueueNext(IByteSource source)
    {
        if (_playback is null || _state is SessionState.Idle or SessionState.Faulted or SessionState.Stopping)
        {
            Open(source, MediaTime.Zero, []);
            return;
        }

        _playback.QueueNext(source);
    }

    /// <summary>The audio thread has moved on to the next item: the session now describes it.</summary>
    private void OnItemStarted(Playback playback, MediaInfo info, int audioTrack)
    {
        Enqueue(new Command(
            () =>
            {
                if (!ReferenceEquals(playback, _playback))
                {
                    return;
                }

                _events.Post(new MediaOpenedEvent(info));
                _events.Post(new TracksChangedEvent(audioTrack, null, null));
                if (_state == SessionState.Ended)
                {
                    // Queued after the last item had already finished: playing again, with a new finish.
                    Volatile.Write(ref _finished, NewCompletion());
                    MoveTo(SessionState.Playing);
                }
            },
            null));
    }

    private void OnItemSkipped(string name, string reason)
    {
        _log.Warning(LogSource, $"Skipped {name}: {reason}");
        _events.Post(new ItemSkippedEvent(name, reason));
    }
}
