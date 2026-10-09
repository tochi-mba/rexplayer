using Rex.Media.Codecs;
using Rex.Media.Containers;
using Rex.Media.IO;
using Rex.Media.Primitives;
using Rex.Media.Video;

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

        /// <summary>Readers for the subtitle tracks rexplayer can read, by track id.</summary>
        public IReadOnlyDictionary<int, Rex.Media.Subtitles.SubtitlePackets> Subtitles { get; init; } = new Dictionary<int, Rex.Media.Subtitles.SubtitlePackets>();

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
    /// Opens <paramref name="source"/> as an item: picks its demuxer, its audio track and a decoder,
    /// and a picture decoder when <paramref name="presenter"/> gives somewhere to show pictures (asked
    /// only when the media has a picture track, so the decoder can share the presenter's graphics
    /// device). On failure everything opened so far (and the source) is released and the reason thrown.
    /// </summary>
    private MediaItem OpenItem(IByteSource source, Func<IVideoPresenter?> presenter)
    {
        IDemuxer? demuxer = null;
        try
        {
            demuxer = _options.Demuxers.Open(source, CancellationToken.None);
            var info = demuxer.Info;
            _log.Info(LogSource, $"Opened {source.Name} as {info.FormatName} with {info.Tracks.Count} track(s).");
            if (ChooseAudioTrack(info, PreferredAudioTrack, AudioLanguages) is not { } audioTrack)
            {
                return OpenSilent(source, demuxer, info, presenter);
            }

            var decoded = _options.Decoders.CreateAudio(audioTrack);
            if (decoded.Decoder is null)
            {
                _log.Warning(LogSource, $"Audio track {audioTrack.Id} ({audioTrack.Codec.DisplayName()}) cannot be decoded: {decoded.Reason}");
                _events.Post(new TrackFailedEvent(audioTrack.Id, decoded.Reason!));

                // The pictures can still be watched: they play in silence, and the window says why.
                if (info.Tracks.Any(track => track.Kind == MediaKind.Video))
                {
                    return OpenSilent(source, demuxer, info, presenter);
                }

                throw new NotSupportedException(decoded.Reason);
            }

            var (videoTrack, videoDecoder) = OpenVideo(info, presenter);
            return new MediaItem { Source = source, Demuxer = demuxer, Info = info, AudioTrack = audioTrack, Decoder = decoded.Decoder, VideoTrack = videoTrack, VideoDecoder = videoDecoder, Subtitles = SubtitleReaders(info) };
        }
        catch (Exception ex) when (ex is MediaFormatException or NotSupportedException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            demuxer?.Dispose();
            source.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Media with pictures but no sound plays against silence (<see cref="SilentAudio"/>); media with
    /// neither, or whose pictures cannot be shown here, has nothing to play.
    /// </summary>
    private MediaItem OpenSilent(IByteSource source, IDemuxer demuxer, MediaInfo info, Func<IVideoPresenter?> presenter)
    {
        if (info.Tracks.All(track => track.Kind != MediaKind.Video))
        {
            throw new NotSupportedException($"{source.Name} has no audio rexplayer can play yet.");
        }

        var (videoTrack, videoDecoder) = OpenVideo(info, presenter);
        if (videoDecoder is null)
        {
            throw new NotSupportedException($"{source.Name} has no sound, and its pictures cannot be shown here.");
        }

        var duration = videoTrack!.Duration.IsKnown ? videoTrack.Duration : info.Duration;
        return new MediaItem { Source = source, Demuxer = demuxer, Info = info, AudioTrack = SilentAudio.Track(duration), Decoder = new SilenceDecoder(), VideoTrack = videoTrack, VideoDecoder = videoDecoder, Subtitles = SubtitleReaders(info) };
    }

    /// <summary>
    /// The picture track and a decoder for it, when pictures are wanted. A picture track that cannot
    /// be decoded is reported and left out: the sound still plays.
    /// </summary>
    private (TrackInfo? Track, IVideoDecoder? Decoder) OpenVideo(MediaInfo info, Func<IVideoPresenter?> presenter)
    {
        var video = info.Tracks.Where(track => track.Kind == MediaKind.Video && track.Video is not null).ToList();
        if (_options.VideoPresenterFactory is null || (video.FirstOrDefault(track => track.IsDefault) ?? video.FirstOrDefault()) is not { } track || presenter() is not { } shown)
        {
            return (null, null);
        }

        var decoded = _options.Decoders.CreateVideo(track, shown.Gpu);
        if (decoded.Decoder is null)
        {
            _log.Warning(LogSource, $"Video track {track.Id} ({track.Codec.DisplayName()}) cannot be shown: {decoded.Reason}");
            _events.Post(new TrackFailedEvent(track.Id, decoded.Reason!));
            return (null, null);
        }

        return (track, decoded.Decoder);
    }

    /// <summary>
    /// The preferred audio track when the media has it; else the best in a preferred language; else
    /// the default; else the first that is not a commentary; else the first.
    /// </summary>
    private static TrackInfo? ChooseAudioTrack(MediaInfo info, int? preferred, IReadOnlyList<string> languages)
    {
        var audio = info.Tracks.Where(track => track.Kind == MediaKind.Audio && track.Audio is not null).ToList();
        var main = audio.FirstOrDefault(track => !Languages.IsCommentary(track.Title));
        return audio.FirstOrDefault(track => track.Id == preferred)
            ?? Languages.Choose(audio, languages, track => track.Language, track => track.Title, track => track.IsDefault, main?.Language)
            ?? audio.FirstOrDefault(track => track.IsDefault)
            ?? main
            ?? audio.FirstOrDefault();
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
    private void OnItemStarted(Playback playback, MediaInfo info, int audioTrack, MediaTime position)
    {
        Enqueue(new Command(
            () =>
            {
                if (ReferenceEquals(playback, _playback))
                {
                    _events.PostTogether(
                        new MediaOpenedEvent(info),
                        new TracksChangedEvent(audioTrack, null, null),
                        new PositionEvent(info.Duration.IsKnown ? MediaTime.Min(position, info.Duration) : position, info.Duration));
                    if (_state == SessionState.Ended)
                    {
                        // Queued after the last item had already finished: playing again, with a new finish.
                        Volatile.Write(ref _finished, NewCompletion());
                        MoveTo(SessionState.Playing);
                    }
                }
            },
            null));
    }

    private static Dictionary<int, Rex.Media.Subtitles.SubtitlePackets> SubtitleReaders(MediaInfo info) =>
        info.Tracks.Where(track => track.Kind == MediaKind.Subtitle && Rex.Media.Subtitles.SubtitlePackets.CanRead(track.Codec))
            .ToDictionary(track => track.Id, track => new Rex.Media.Subtitles.SubtitlePackets(track));

    private void OnSubtitle(MediaInfo item, int trackId, Rex.Media.Subtitles.SubtitleCue cue) => _events.Post(new SubtitleCueEvent(item, trackId, cue));

    private void OnItemSkipped(string name, string reason)
    {
        _log.Warning(LogSource, $"Skipped {name}: {reason}");
        _events.Post(new ItemSkippedEvent(name, reason));
    }
}
