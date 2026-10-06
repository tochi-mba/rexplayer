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

        private long _videoFramesDecoded;
        private long _videoFramesPresented;
        private long _videoFramesDropped;

        private int VideoHardCapacity => _session._options.VideoQueueCapacity * 4;

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
                    _videoItem.VideoDecoder?.Drain(state.Decoded);
                    Show(state);
                    Volatile.Write(ref _videoItem, owner);
                    state.Target = MediaTime.Zero;
                    ReleaseFinishedItems();
                }

                var decoder = _videoItem.VideoDecoder;
                if (item.EndOfStream)
                {
                    decoder?.Drain(state.Decoded);
                    Show(state);
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

                        if (_clock.Now > frame.Pts + length)
                        {
                            Interlocked.Increment(ref _videoFramesDropped);
                            continue;
                        }
                    }

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
            while (_clock.Now < due)
            {
                var wait = due - _clock.Now;
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
