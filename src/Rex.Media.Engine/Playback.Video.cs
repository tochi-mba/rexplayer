using Rex.Media.Codecs;
using Rex.Media.Diagnostics;
using Rex.Media.Primitives;

namespace Rex.Media.Engine;

public sealed partial class MediaSession
{
    /// <summary>
    /// The picture side of a playback: one thread takes video packets, decodes them, and presents each
    /// picture when the clock reaches it (ADR-005: video follows the clock). A picture that is due
    /// later than its own length has passed is dropped and counted (VID-25). Into an output that is
    /// not real time (a capture file), every picture is presented in order at once.
    /// </summary>
    private sealed partial class Playback
    {
        /// <summary>The audio queue length at or below which audio is starving and video may overfill.</summary>
        private const int StarvingAudio = 2;

        /// <summary>How long the video thread sleeps at most before looking at the clock again.</summary>
        private static readonly TimeSpan ClockPoll = TimeSpan.FromMilliseconds(10);

        /// <summary>How late a picture with no duration of its own may be and still be shown.</summary>
        private static readonly MediaTime DefaultLateness = MediaTime.FromMilliseconds(40);

        private readonly object _endGate = new();
        private long _audioEndedGeneration = -1;
        private long _videoEndedGeneration = -1;
        private MediaTime _audioEndedAt;
        private long _audioEndedTimestamp;
        private long _videoFramesDecoded;
        private long _videoFramesPresented;
        private long _videoFramesDropped;

        private int VideoHardCapacity => _session._options.VideoQueueCapacity * 4;

        /// <summary>
        /// The time pictures are shown against: the audio clock while audio plays, then, once the
        /// audio has ended, real time from where it stopped, so pictures that outlast the sound play on.
        /// The audio delay moves the pictures the other way: sound heard later means pictures shown sooner.
        /// </summary>
        private MediaTime VideoNow
        {
            get
            {
                lock (_endGate)
                {
                    // Each item's pictures follow that item's own time: the next one's wait for its sound.
                    var now = _audioEndedGeneration < 0 || _audioEndedGeneration != Generation
                        ? _clock.NowFor(Volatile.Read(ref _videoItem))
                        : _audioEndedAt + new MediaTime((long)(_session._options.Time.GetElapsedTime(_audioEndedTimestamp).Ticks * Speed));
                    return now + new MediaTime(Interlocked.Read(ref _audioDelayTicks));
                }
            }
        }

        private long _audioDelayTicks;

        /// <summary>How much later the sound is heard than the pictures are shown (PB-19); negative brings it earlier.</summary>
        public void SetAudioDelay(TimeSpan delay) => Interlocked.Exchange(ref _audioDelayTicks, delay.Ticks);

        /// <summary>
        /// One side reached the end of the stream. The playback has ended once the audio has, and the
        /// pictures too when there are any, so the last pictures are shown before the end is reported.
        /// </summary>
        private void StreamEnded(bool video, long generation)
        {
            lock (_endGate)
            {
                if (video)
                {
                    _videoEndedGeneration = generation;
                }
                else
                {
                    _audioEndedGeneration = generation;
                    _audioEndedAt = _clock.Now;
                    _audioEndedTimestamp = _session._options.Time.GetTimestamp();
                }

                if (_audioEndedGeneration != generation || (_videoQueue is not null && _videoEndedGeneration != generation))
                {
                    return;
                }
            }

            _session.OnEnded(this, generation);
        }

        /// <summary>
        /// Audio about to run dry: the video queue may then hold more than its share, because a demuxer
        /// waiting for room in the video queue would otherwise keep audio from ever arriving.
        /// </summary>
        private bool AudioStarving() => _audioQueue.Count <= StarvingAudio;

        /// <summary>Releases the items of a gapless run that both the audio and the video side have finished with.</summary>
        private void ReleaseFinishedItems()
        {
            lock (_items)
            {
                var index = Math.Min(_items.IndexOf(Volatile.Read(ref _audioItem)), _items.IndexOf(Volatile.Read(ref _videoItem)));
                foreach (var finished in _items.Take(index))
                {
                    finished.Dispose();
                }

                _items.RemoveRange(0, Math.Max(0, index));
            }
        }

        private void RunVideo()
        {
            var state = new VideoState();
            ThreadLoop.Run(() => VideoLoop(state), ex => _session.OnFailed(this, "The video output failed: " + ExceptionDiagnostics.Summary(ex)), _lifetime.Token);
            DisposeAll(state.Decoded);
        }

        /// <summary>Runs until the queue is closed (the session is closing).</summary>
        private void VideoLoop(VideoState state)
        {
            while (_videoQueue!.TryTake(out var item, CancellationToken.None))
            {
                if (item.Generation != state.Generation)
                {
                    state.Generation = item.Generation;
                    var seek = SeekFor(item.Generation);
                    state.Target = seek.Mode == SeekMode.Precise ? seek.Target : MediaTime.Zero;
                    _videoItem.VideoDecoder?.Flush();
                }

                if (item.Owner is MediaItem owner && !ReferenceEquals(owner, _videoItem))
                {
                    // The run has moved to the next item: what the last one's decoder holds is shown first.
                    DrainAndShow(_videoItem.VideoDecoder, state);
                    Volatile.Write(ref _videoItem, owner);
                    state.Target = MediaTime.Zero;
                    lock (_endGate)
                    {
                        _videoEndedGeneration = -1;
                    }

                    ReleaseFinishedItems();
                }

                var decoder = _videoItem.VideoDecoder;
                if (item.EndOfStream)
                {
                    DrainAndShow(decoder, state);
                    StreamEnded(video: true, state.Generation);
                    continue;
                }

                using var packet = item.Payload!;
                try
                {
                    decoder!.Decode(packet, state.Decoded);
                }
                catch (MediaFormatException ex)
                {
                    // A damaged picture is skipped; the decoder picks up again at the next keyframe.
                    Interlocked.Increment(ref _corruptPackets);
                    _session._log.Warning(LogSource, "A video packet could not be decoded: " + ex.Message);
                    DisposeAll(state.Decoded);
                    continue;
                }

                Show(state);
            }
        }

        /// <summary>Shows everything a decoder still holds, batch by batch, releasing each batch before asking for the next.</summary>
        private void DrainAndShow(IVideoDecoder? decoder, VideoState state)
        {
            if (decoder is null)
            {
                return;
            }

            while (!decoder.Drain(state.Decoded))
            {
                Show(state);
            }

            Show(state);
        }

        /// <summary>Presents the decoded pictures in turn, each when it is due, and empties the list.</summary>
        private void Show(VideoState state)
        {
            var frames = state.Decoded;
            var next = 0;
            try
            {
                while (next < frames.Count)
                {
                    using var frame = frames[next++];
                    Interlocked.Increment(ref _videoFramesDecoded);
                    var length = frame.Duration > MediaTime.Zero ? frame.Duration : DefaultLateness;
                    if (frame.Pts.IsKnown && frame.Pts + length <= state.Target)
                    {
                        // Before the target of a precise seek: decoded only to reach it.
                        continue;
                    }

                    if (_sink.IsRealTime && frame.Pts.IsKnown)
                    {
                        if (!WaitUntilDue(frame.Pts, state.Generation))
                        {
                            continue;
                        }

                        if (VideoNow > frame.Pts + length)
                        {
                            Interlocked.Increment(ref _videoFramesDropped);
                            continue;
                        }
                    }

                    // Decoders may not copy packet metadata onto delayed output frames.
                    // The video worker owns the authoritative seek generation.
                    frame.Generation = state.Generation;
                    _presenter!.Present(frame);
                    Interlocked.Increment(ref _videoFramesPresented);
                }
            }
            finally
            {
                for (; next < frames.Count; next++)
                {
                    frames[next].Dispose();
                }

                frames.Clear();
            }
        }

        /// <summary>Waits until the clock reaches <paramref name="due"/>; false when a seek or the end of the session cut the wait short.</summary>
        private bool WaitUntilDue(MediaTime due, long generation)
        {
            var token = GenerationToken(generation);
            while (VideoNow < due)
            {
                var wait = due - VideoNow;
                if (token.WaitHandle.WaitOne(wait.Ticks < ClockPoll.Ticks ? new TimeSpan(Math.Max(wait.Ticks, 0)) : ClockPoll))
                {
                    return false;
                }
            }

            return !token.IsCancellationRequested;
        }

        /// <summary>What the video thread carries from one packet to the next.</summary>
        private sealed class VideoState
        {
            public long Generation { get; set; }

            /// <summary>Pictures that end before this are skipped: the target of the current precise seek.</summary>
            public MediaTime Target { get; set; } = MediaTime.Zero;

            public List<VideoFrame> Decoded { get; } = [];
        }
    }
}
