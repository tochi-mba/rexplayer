using System.Collections.Concurrent;
using Rex.Media.Audio;
using Rex.Media.Diagnostics;
using Rex.Media.IO;
using Rex.Media.Primitives;
using Rex.Media.Video;

namespace Rex.Media.Engine;

public sealed partial class MediaSession
{
    /// <summary>
    /// The pipeline for a run of media: the items (source, demuxer, decoder each), the audio
    /// pipeline and sink they share, and the two threads that move data through them. When an item
    /// runs out, the demux thread opens the next queued one and carries on; the audio thread notices
    /// the hand-over from the packets' owner and plays on into the same sink, so the join is gapless.
    /// <para>
    /// A seek starts a new generation (ADR-006). The demux thread repositions and tags new packets
    /// with it; the queue disposes anything older; the audio thread flushes its decoder and sink when
    /// the first packet of the new generation arrives. Every blocking wait on the audio side (the pause
    /// gate, a full sink, draining at the end) uses the generation's cancellation token, so a seek
    /// interrupts it immediately instead of waiting for the old audio to finish.
    /// </para>
    /// </summary>
    private sealed partial class Playback : IDisposable
    {
        /// <summary>How far a frame may start from where the last ended and still be taken to follow it.</summary>
        private static readonly MediaTime TimestampJitter = MediaTime.FromMilliseconds(2);

        private readonly MediaSession _session;
        private readonly IAudioSink _sink;

        /// <summary>Samples the device holds but has not played yet.</summary>
        public long QueuedSamples => _sink.QueuedSamples;
        private readonly AudioFormat _sinkFormat;
        private readonly AudioPipeline _pipeline;
        private readonly AudioClock _clock;
        private readonly BoundedQueue<Packet> _audioQueue;
        private readonly CancellationTokenSource _lifetime = new();
        private readonly ManualResetEventSlim _playGate = new(false);
        private readonly AutoResetEvent _demuxWake = new(false);
        private readonly object _seekGate = new();
        private readonly Thread _demuxThread;
        private readonly Thread _audioThread;
        private readonly IVideoPresenter? _presenter;
        private readonly BoundedQueue<Packet>? _videoQueue;
        private readonly Thread? _videoThread;
        private readonly ConcurrentQueue<IByteSource> _upcoming = new();
        private readonly List<MediaItem> _items = [];
        private MediaItem _demuxItem;

        /// <summary>How far silence has been queued for media without sound; the demux thread's alone.</summary>
        private MediaTime _silenceUntil = MediaTime.Zero;
        private MediaItem _audioItem;
        private MediaItem _videoItem;
        private MediaInfo _info;
        private long _generation;
        private SeekRequest? _pendingSeek;
        private SeekRequest _latestSeek = new(0, MediaTime.Zero, SeekMode.Precise);
        private CancellationTokenSource _generationCancel = new();
        private SeekRequest _currentSeek = new(0, MediaTime.Zero, SeekMode.Precise);
        private long _packetsRead;
        private long _bytesRead;
        private long _framesDecoded;
        private long _samplesWritten;
        private long _corruptPackets;
        private long _lastPositionPost;
        private int _itemsStarted = 1;
        private long _earlierTicks;

        public Playback(MediaSession session, MediaItem first, IAudioSink sink, AudioFormat sinkFormat, IVideoPresenter? presenter)
        {
            _session = session;
            _items.Add(first);
            _demuxItem = first;
            _audioItem = first;
            _videoItem = first;
            _info = first.Info;
            _sink = sink;
            _sinkFormat = sinkFormat;
            _pipeline = new AudioPipeline(sinkFormat, session._options.ResamplerQuality);
            _clock = new AudioClock(sink, first);
            _clock.Rebase(MediaTime.Zero, sinkFormat.SampleRate, _speed);
            _audioQueue = new BoundedQueue<Packet>(session._options.AudioQueueCapacity);
            _demuxThread = new Thread(RunDemux) { IsBackground = true, Name = "rexplayer demux" };
            _audioThread = new Thread(RunAudio) { IsBackground = true, Name = "rexplayer audio" };
            if (presenter is not null)
            {
                _presenter = presenter;
                _videoQueue = new BoundedQueue<Packet>(session._options.VideoQueueCapacity);
                _videoThread = new Thread(RunVideo) { IsBackground = true, Name = "rexplayer video" };
            }
        }

        /// <summary>What the item being heard contains.</summary>
        public MediaInfo Info => Volatile.Read(ref _info);

        public long Generation => Interlocked.Read(ref _generation);

        public bool IsPaused => !_playGate.IsSet;

        public MediaTime Position
        {
            get
            {
                AnnounceHeard();
                var now = _clock.Now;
                return Info.Duration.IsKnown ? MediaTime.Min(now, Info.Duration) : now;
            }
        }

        public void Start(bool play)
        {
            if (play)
            {
                _playGate.Set();
            }
            else
            {
                _sink.Pause();
            }

            _demuxThread.Start();
            _audioThread.Start();
            _videoThread?.Start();
        }

        public void Pause()
        {
            _playGate.Reset();
            _sink.Pause();
        }

        public void Resume()
        {
            _sink.Resume();
            _playGate.Set();
        }

        /// <summary>
        /// Takes up new sound settings now; the loudness gain is worked out from the item playing,
        /// and again from each item that follows it.
        /// </summary>
        public void ApplySound(SoundSettings sound)
        {
            Volatile.Write(ref _sound, sound);
            _pipeline.Effects = sound.EffectsFor(Volatile.Read(ref _audioItem).Info);
        }

        private SoundSettings _sound = SoundSettings.Plain;

        public void ApplyVolume(double volume, bool muted)
        {
            _pipeline.Volume.Volume = volume;
            _pipeline.Volume.Muted = muted;
        }

        /// <summary>Adds media to follow the last item; the demux thread opens it when its turn comes.</summary>
        public void QueueNext(IByteSource source)
        {
            _upcoming.Enqueue(source);
            _demuxWake.Set();
        }

        /// <summary>
        /// Plays at <paramref name="speed"/> from now: the pipeline restarts at the current position
        /// (as for a seek), so the clock and the sound change speed together.
        /// </summary>
        public void SetSpeed(double speed)
        {
            Volatile.Write(ref _speed, speed);
            RequestSeek(Position, SeekMode.Precise);
        }

        private double _speed = 1;

        /// <summary>The speed the media plays at; the wall clock after the sound ends follows it too.</summary>
        public double Speed => Volatile.Read(ref _speed);

        /// <summary>Starts a new generation positioned at <paramref name="target"/>.</summary>
        public void RequestSeek(MediaTime target, SeekMode mode)
        {
            CancellationTokenSource previous;
            lock (_seekGate)
            {
                var request = new SeekRequest(Interlocked.Increment(ref _generation), target, mode);
                _pendingSeek = request;
                _latestSeek = request;
                previous = _generationCancel;
                _generationCancel = new CancellationTokenSource();
            }

            previous.Cancel();
            _clock.Rebase(target, _sinkFormat.SampleRate, Volatile.Read(ref _speed));
            _audioQueue.Flush(Generation);
            _videoQueue?.Flush(Generation);
            _demuxWake.Set();
        }

        public SessionStats Stats() => new()
        {
            PacketsRead = Interlocked.Read(ref _packetsRead),
            BytesRead = Interlocked.Read(ref _bytesRead),
            AudioFramesDecoded = Interlocked.Read(ref _framesDecoded),
            AudioSamplesPlayed = Interlocked.Read(ref _samplesWritten),
            CorruptPackets = Interlocked.Read(ref _corruptPackets),
            AudioDecoder = Volatile.Read(ref _audioItem).Decoder.Name,
            ItemsStarted = Volatile.Read(ref _itemsStarted),
            EarlierItemsDuration = new MediaTime(Interlocked.Read(ref _earlierTicks)),
            VideoFramesDecoded = Interlocked.Read(ref _videoFramesDecoded),
            VideoFramesPresented = Interlocked.Read(ref _videoFramesPresented),
            VideoFramesDropped = Interlocked.Read(ref _videoFramesDropped),
            VideoDecoder = Volatile.Read(ref _videoItem).VideoDecoder?.Name,
            VideoDecoderSource = Volatile.Read(ref _videoItem).VideoDecoder?.Source,
        };

        public void Dispose()
        {
            // In this order: release every wait the audio side may be in, empty and close its queue so
            // its loop ends, then stop the demux thread.
            lock (_seekGate)
            {
                _generationCancel.Cancel();
            }

            _audioQueue.Flush(long.MaxValue);
            _audioQueue.Close();
            _videoQueue?.Flush(long.MaxValue);
            _videoQueue?.Close();
            _lifetime.Cancel();
            _demuxWake.Set();
            JoinIfStarted(_demuxThread);
            JoinIfStarted(_audioThread);
            if (_videoThread is not null)
            {
                JoinIfStarted(_videoThread);
            }

            _audioQueue.Dispose();
            _videoQueue?.Dispose();
            _presenter?.Dispose();
            _sink.Dispose();
            foreach (var item in _items)
            {
                item.Dispose();
            }

            while (_upcoming.TryDequeue(out var source))
            {
                source.Dispose();
            }

            _lifetime.Dispose();
            _generationCancel.Dispose();
            _playGate.Dispose();
            _demuxWake.Dispose();
        }

        private static void JoinIfStarted(Thread thread)
        {
            if (thread.ThreadState != ThreadState.Unstarted)
            {
                thread.Join();
            }
        }

        private CancellationToken GenerationToken(long generation)
        {
            lock (_seekGate)
            {
                return _latestSeek.Generation == generation ? _generationCancel.Token : new CancellationToken(canceled: true);
            }
        }

        private SeekRequest SeekFor(long generation)
        {
            lock (_seekGate)
            {
                return _latestSeek.Generation == generation ? _latestSeek : new SeekRequest(generation, MediaTime.Zero, SeekMode.Precise);
            }
        }

        private void RunDemux()
        {
            var token = _lifetime.Token;
            ThreadLoop.Run(() => DemuxLoop(token), ex => _session.OnFailed(this, "Reading the media failed: " + ExceptionDiagnostics.Summary(ex)), token);
        }

        /// <summary>Runs until the session closes, which always ends it with a cancellation.</summary>
        private void DemuxLoop(CancellationToken token)
        {
            long generation = 0;
            var ended = false;
            while (true)
            {
                token.ThrowIfCancellationRequested();
                SeekRequest? seek;
                lock (_seekGate)
                {
                    seek = _pendingSeek;
                    _pendingSeek = null;
                }

                if (seek is not null)
                {
                    _demuxItem.Demuxer.Seek(seek.Target, token);
                    generation = seek.Generation;
                    _silenceUntil = seek.Target;
                    ended = false;
                }

                if (ended)
                {
                    // Media queued after the last item ended carries on from there.
                    if (OpenNext())
                    {
                        ended = false;
                        continue;
                    }

                    WaitHandle.WaitAny([_demuxWake, token.WaitHandle]);
                    continue;
                }

                var packet = ReadPacketOrEnd(token);
                if (packet is null)
                {
                    if (SilentAudio.Is(_demuxItem.AudioTrack))
                    {
                        // The silence runs to the end of the pictures, so the last one is shown for its full time.
                        var duration = _demuxItem.AudioTrack.Duration;
                        FeedSilence(duration.IsKnown && duration > _silenceUntil ? duration : _silenceUntil, generation, token);
                    }

                    if (OpenNext())
                    {
                        continue;
                    }

                    ended = true;
                    _videoQueue?.Add(new QueueItem<Packet>(null, EndOfStream: true, generation, _demuxItem), AudioStarving, VideoHardCapacity, token);
                    _audioQueue.Add(new QueueItem<Packet>(null, EndOfStream: true, generation, _demuxItem), token);
                    continue;
                }

                Interlocked.Increment(ref _packetsRead);
                Interlocked.Add(ref _bytesRead, packet.Data.Length);
                packet.Generation = generation;
                if (packet.TrackId == _demuxItem.AudioTrack.Id)
                {
                    _audioQueue.Add(new QueueItem<Packet>(packet, EndOfStream: false, generation, _demuxItem), token);
                }
                else if (_videoQueue is not null && packet.TrackId == _demuxItem.VideoTrack?.Id)
                {
                    var end = packet.Timestamp.IsKnown ? packet.Timestamp + (packet.Duration > MediaTime.Zero ? packet.Duration : SilentAudio.Chunk) : MediaTime.Unknown;
                    _videoQueue.Add(new QueueItem<Packet>(packet, EndOfStream: false, generation, _demuxItem), AudioStarving, VideoHardCapacity, token);
                    if (SilentAudio.Is(_demuxItem.AudioTrack) && end.IsKnown)
                    {
                        FeedSilence(end, generation, token);
                    }
                }
                else
                {
                    using (packet)
                    {
                        if (_demuxItem.Subtitles.TryGetValue(packet.TrackId, out var reader) && reader.Read(packet) is { } cue)
                        {
                            _session.OnSubtitle(_demuxItem.Info, packet.TrackId, cue);
                        }
                    }
                }
            }
        }

        /// <summary>Queues silent packets up to <paramref name="until"/> for media without sound (<see cref="SilentAudio"/>).</summary>
        private void FeedSilence(MediaTime until, long generation, CancellationToken token)
        {
            while (_silenceUntil < until)
            {
                var end = _silenceUntil + SilentAudio.Chunk < until ? _silenceUntil + SilentAudio.Chunk : until;
                var silence = SilentAudio.Between(_silenceUntil, end);
                silence.Generation = generation;
                _audioQueue.Add(new QueueItem<Packet>(silence, EndOfStream: false, generation, _demuxItem), token);
                _silenceUntil = end;
            }
        }

        /// <summary>Moves the demux side to the next queued item that opens; false when none is left.</summary>
        private bool OpenNext()
        {
            while (_upcoming.TryDequeue(out var source))
            {
                try
                {
                    var item = _session.OpenItem(source, () => _presenter);
                    lock (_items)
                    {
                        _items.Add(item);
                    }

                    _demuxItem = item;
                    _silenceUntil = MediaTime.Zero;
                    return true;
                }
                catch (Exception ex) when (ex is MediaFormatException or NotSupportedException or IOException or UnauthorizedAccessException or InvalidOperationException)
                {
                    _session.OnItemSkipped(source.Name, ex.Message);
                }
            }

            return false;
        }

        /// <summary>A truncated or damaged tail ends the stream rather than the session.</summary>
        private Packet? ReadPacketOrEnd(CancellationToken token)
        {
            try
            {
                return _demuxItem.Demuxer.ReadPacket(token);
            }
            catch (MediaFormatException ex)
            {
                _session._log.Warning(LogSource, "The file is damaged after this point; playback stops here. " + ex.Message);
                return null;
            }
        }

        private void RunAudio()
        {
            var state = new AudioState();
            ThreadLoop.Run(() => AudioLoop(state), ex => _session.OnFailed(this, "The audio output failed: " + ExceptionDiagnostics.Summary(ex)), _lifetime.Token);
            DisposeAll(state.Decoded);
        }

        /// <summary>Runs until the queue is closed (the session is closing) or the track fails for good.</summary>
        private void AudioLoop(AudioState state)
        {
            while (_audioQueue.TryTake(out var item, CancellationToken.None))
            {
                var newGeneration = item.Generation != state.Generation;
                if (newGeneration)
                {
                    StartGeneration(state, item.Generation);
                }

                if (item.Owner is MediaItem owner && !ReferenceEquals(owner, _audioItem))
                {
                    SwitchItem(state, owner, joined: !newGeneration);
                }

                if (item.EndOfStream)
                {
                    FinishStream(state);
                    continue;
                }

                using var packet = item.Payload!;
                if (!DecodePacket(state, packet))
                {
                    return;
                }
            }
        }

        private void StartGeneration(AudioState state, long generation)
        {
            state.Generation = generation;
            _currentSeek = SeekFor(generation);
            DisposeAll(state.Decoded);
            _audioItem.Decoder.Flush();
            _pipeline.Reset(_currentSeek.Target);
            _sink.Flush();
            _session.Scope.Clear();
            _pipeline.Speed = Volatile.Read(ref _speed);
            _clock.Rebase(_currentSeek.Target, _sinkFormat.SampleRate, _pipeline.Speed);
            state.AwaitingFirstFrame = true;
            state.NextPts = MediaTime.Unknown;
            state.PaddingSamples = 0;
        }

        /// <summary>
        /// The demux side has moved on to <paramref name="next"/>. When the two join with no seek
        /// between, the last item's tail plays first and the new one starts at its own time zero;
        /// after a seek, the new generation has already reset everything. Earlier items are released.
        /// </summary>
        private void SwitchItem(AudioState state, MediaItem next, bool joined)
        {
            if (joined)
            {
                _audioItem.Decoder.Drain(state.Decoded);
                WriteFrames(state);
                _currentSeek = new SeekRequest(state.Generation, MediaTime.Zero, SeekMode.Precise);
                state.AwaitingFirstFrame = true;
                state.Continuing = true;

                // A run that had ended (the next item queued after it) is playing again.
                lock (_endGate)
                {
                    _audioEndedGeneration = -1;
                }
            }

            state.NextPts = MediaTime.Unknown;
            state.PaddingSamples = 0;
            var finishedDuration = _audioItem.Info.Duration;
            Interlocked.Add(ref _earlierTicks, finishedDuration.IsKnown ? finishedDuration.Ticks : 0);
            Interlocked.Increment(ref _itemsStarted);
            Volatile.Write(ref _audioItem, next);
            _pipeline.Effects = Volatile.Read(ref _sound).EffectsFor(next.Info);
            ReleaseFinishedItems();
        }

        /// <summary>
        /// Reports each item whose first sample has now been heard: the session then describes it.
        /// Decoding runs ahead of the device by its buffer, so this comes later than the switch above.
        /// </summary>
        private void AnnounceHeard()
        {
            foreach (var item in _clock.TakeStarted().Cast<MediaItem>())
            {
                Volatile.Write(ref _info, item.Info);
                _session.OnItemStarted(this, item.Info, item.AudioTrack.Id, _clock.NowFor(item));
            }
        }

        private void FinishStream(AudioState state)
        {
            _audioItem.Decoder.Drain(state.Decoded);
            WriteFrames(state);
            if (_pipeline.Drain() is { } tail)
            {
                WriteProcessed(state, tail);
            }

            if (state.AwaitingFirstFrame)
            {
                state.AwaitingFirstFrame = false;
                _session.OnSeekCompleted(this, state.Generation, _currentSeek.Target);
            }

            try
            {
                _sink.Drain(GenerationToken(state.Generation));
            }
            catch (OperationCanceledException)
            {
                // A seek arrived while the last audio was playing out; the new generation takes over.
                return;
            }

            PostPosition(force: true);
            StreamEnded(video: false, state.Generation);
        }

        /// <summary>Decodes and plays one packet. False when the track has failed for good.</summary>
        private bool DecodePacket(AudioState state, Packet packet)
        {
            if (packet.DiscardSamples > 0 && packet.Pts.IsKnown)
            {
                (state.PaddingAt, state.PaddingSamples) = (packet.Pts, packet.DiscardSamples);
            }

            try
            {
                _audioItem.Decoder.Decode(packet, state.Decoded);
                state.ConsecutiveFailures = 0;
            }
            catch (MediaFormatException ex)
            {
                Interlocked.Increment(ref _corruptPackets);
                DisposeAll(state.Decoded);
                state.ConsecutiveFailures++;
                if (state.ConsecutiveFailures >= _session._options.MaxConsecutiveCorruptPackets)
                {
                    _session.OnTrackFailed(_audioItem.AudioTrack.Id, "The audio stream is too damaged to decode: " + ex.Message);
                    _session.OnFailed(this, "No playable streams remain.");
                    return false;
                }

                return true;
            }

            WriteFrames(state);
            return true;
        }

        /// <summary>
        /// Plays the decoded frames and empties the list. Every frame is disposed exactly once, even
        /// when the sink fails part-way: a pooled frame disposed twice could be taken back from
        /// whoever the pool gave it to next.
        /// </summary>
        private void WriteFrames(AudioState state)
        {
            var frames = state.Decoded;
            var next = 0;
            try
            {
                while (next < frames.Count)
                {
                    using var frame = frames[next++];
                    Interlocked.Increment(ref _framesDecoded);
                    DropPadding(state, frame);
                    Smooth(state, frame);
                    if (Trim(frame) && _pipeline.Process(frame) is { } processed)
                    {
                        WriteProcessed(state, processed);
                    }
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

        /// <summary>
        /// Drops audio before the target of a precise seek, and always before time zero (where a
        /// gapless stream keeps its encoder delay). When the track declares trailing padding, audio
        /// past its duration goes too. False when the whole frame goes.
        /// </summary>
        private bool Trim(AudioFrame frame)
        {
            if (!frame.Pts.IsKnown)
            {
                return frame.SampleCount > 0;
            }

            var start = _currentSeek.Mode == SeekMode.Precise ? _currentSeek.Target : MediaTime.Zero;
            if (frame.Pts < start)
            {
                frame.TrimStart((int)Math.Min(int.MaxValue, (start - frame.Pts).ToSamples(frame.SampleRate)));
            }

            var track = _audioItem.AudioTrack;
            if (track.Audio is { TrailingPadding: > 0 } && track.Duration.IsKnown)
            {
                var room = (track.Duration - frame.Pts).ToSamples(frame.SampleRate);
                frame.SetSampleCount((int)Math.Clamp(room, 0, frame.SampleCount));
            }

            return frame.SampleCount > 0;
        }

        private void WriteProcessed(AudioState state, AudioFrame processed)
        {
            using (processed)
            {
                if (state.AwaitingFirstFrame)
                {
                    state.AwaitingFirstFrame = false;
                    if (state.Continuing)
                    {
                        // Joined behind the last item's tail: its time starts when this sample is heard.
                        state.Continuing = false;
                        _clock.Continue(processed.Pts.IsKnown ? processed.Pts : MediaTime.Zero, _audioItem);
                    }
                    else if (processed.Pts.IsKnown)
                    {
                        _clock.Rebase(processed.Pts, _sinkFormat.SampleRate, _pipeline.Speed, _audioItem);
                    }

                    _session.OnSeekCompleted(this, state.Generation, processed.Pts.IsKnown ? processed.Pts : _currentSeek.Target);
                }

                var generationToken = GenerationToken(state.Generation);
                try
                {
                    _playGate.Wait(generationToken);
                    _sink.Write(processed, generationToken);
                    _clock.Wrote(processed.SampleCount);
                    _session.Scope.Write(processed);
                }
                catch (OperationCanceledException)
                {
                    // Superseded by a seek, or the session is closing: this audio is dropped.
                    return;
                }

                Interlocked.Add(ref _samplesWritten, processed.SampleCount);
                PostPosition(force: false);
            }
        }

        private void PostPosition(bool force)
        {
            AnnounceHeard();
            var time = _session._options.Time;
            var now = time.GetTimestamp();
            var last = Interlocked.Read(ref _lastPositionPost);
            if (!force && last != 0 && time.GetElapsedTime(last, now) < _session._options.PositionInterval)
            {
                return;
            }

            Interlocked.Exchange(ref _lastPositionPost, now);
            _session.OnPosition(Position, Info.Duration);
        }

        private static void DisposeAll<T>(List<T> frames)
            where T : IDisposable
        {
            foreach (var frame in frames)
            {
                frame.Dispose();
            }

            frames.Clear();
        }

        /// <summary>
        /// Containers round timestamps (Matroska to the millisecond), so a frame that starts within a
        /// rounding error of where the last one ended is taken to follow it exactly, and a frame with
        /// no timestamp continues from the last. Otherwise the frame straddling time zero could lose
        /// samples to the trim, and seams could drop or repeat a few.
        /// </summary>
        private static void Smooth(AudioState state, AudioFrame frame)
        {
            if (state.NextPts.IsKnown && (!frame.Pts.IsKnown || Math.Abs((frame.Pts - state.NextPts).Ticks) <= TimestampJitter.Ticks))
            {
                frame.Pts = state.NextPts;
            }

            state.NextPts = frame.Pts.IsKnown ? frame.Pts + frame.Duration : MediaTime.Unknown;
        }

        /// <summary>
        /// Takes the padding a container flagged off the end of the frame decoded from that packet.
        /// Decoders stamp a frame with its packet's time (to within rounding), which is how the frame is found: a decoder
        /// with latency (Windows' AAC decoder returns each packet's audio one packet later) hands it
        /// over later than the packet arrived.
        /// </summary>
        private static void DropPadding(AudioState state, AudioFrame frame)
        {
            if (state.PaddingSamples > 0 && frame.Pts.IsKnown && Math.Abs((frame.Pts - state.PaddingAt).Ticks) <= TimestampJitter.Ticks)
            {
                frame.SetSampleCount(Math.Max(0, frame.SampleCount - state.PaddingSamples));
                state.PaddingSamples = 0;
            }
        }

        private sealed record SeekRequest(long Generation, MediaTime Target, SeekMode Mode);

        /// <summary>What the audio thread carries from one packet to the next.</summary>
        private sealed class AudioState
        {
            public long Generation { get; set; }

            public bool AwaitingFirstFrame { get; set; }

            /// <summary>The next frame joins the last item's tail without a seek between them.</summary>
            public bool Continuing { get; set; }

            public int ConsecutiveFailures { get; set; }

            /// <summary>Where the last frame ended, for smoothing the next one's timestamp.</summary>
            public MediaTime NextPts { get; set; } = MediaTime.Unknown;

            /// <summary>The time of a packet whose decoded end is padding, and how many samples of it.</summary>
            public MediaTime PaddingAt { get; set; }

            public int PaddingSamples { get; set; }

            public List<AudioFrame> Decoded { get; } = [];
        }
    }
}
