using System.Collections.Concurrent;
using Rex.Media.Audio;
using Rex.Media.Diagnostics;
using Rex.Media.IO;
using Rex.Media.Primitives;

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
    private sealed class Playback : IDisposable
    {
        private readonly MediaSession _session;
        private readonly IAudioSink _sink;
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
        private readonly ConcurrentQueue<IByteSource> _upcoming = new();
        private readonly List<MediaItem> _items = [];
        private MediaItem _demuxItem;
        private MediaItem _audioItem;
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

        public Playback(MediaSession session, MediaItem first, IAudioSink sink, AudioFormat sinkFormat)
        {
            _session = session;
            _items.Add(first);
            _demuxItem = first;
            _audioItem = first;
            _info = first.Info;
            _sink = sink;
            _sinkFormat = sinkFormat;
            _pipeline = new AudioPipeline(sinkFormat, session._options.ResamplerQuality);
            _clock = new AudioClock(sink);
            _clock.Rebase(MediaTime.Zero, sinkFormat.SampleRate);
            _audioQueue = new BoundedQueue<Packet>(session._options.AudioQueueCapacity);
            _demuxThread = new Thread(RunDemux) { IsBackground = true, Name = "rexplayer demux" };
            _audioThread = new Thread(RunAudio) { IsBackground = true, Name = "rexplayer audio" };
        }

        /// <summary>What the item being heard contains.</summary>
        public MediaInfo Info => Volatile.Read(ref _info);

        public long Generation => Interlocked.Read(ref _generation);

        public bool IsPaused => !_playGate.IsSet;

        public MediaTime Position
        {
            get
            {
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
            _clock.Rebase(target, _sinkFormat.SampleRate);
            _audioQueue.Flush(Generation);
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
            _lifetime.Cancel();
            _demuxWake.Set();
            JoinIfStarted(_demuxThread);
            JoinIfStarted(_audioThread);
            _audioQueue.Dispose();
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
                    if (OpenNext())
                    {
                        continue;
                    }

                    ended = true;
                    _audioQueue.Add(new QueueItem<Packet>(null, EndOfStream: true, generation, _demuxItem), token);
                    continue;
                }

                Interlocked.Increment(ref _packetsRead);
                Interlocked.Add(ref _bytesRead, packet.Data.Length);
                if (packet.TrackId != _demuxItem.AudioTrack.Id)
                {
                    packet.Dispose();
                    continue;
                }

                packet.Generation = generation;
                _audioQueue.Add(new QueueItem<Packet>(packet, EndOfStream: false, generation, _demuxItem), token);
            }
        }

        /// <summary>Moves the demux side to the next queued item that opens; false when none is left.</summary>
        private bool OpenNext()
        {
            while (_upcoming.TryDequeue(out var source))
            {
                try
                {
                    var item = _session.OpenItem(source);
                    lock (_items)
                    {
                        _items.Add(item);
                    }

                    _demuxItem = item;
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
            _clock.Rebase(_currentSeek.Target, _sinkFormat.SampleRate);
            state.AwaitingFirstFrame = true;
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
            }

            var finishedDuration = _audioItem.Info.Duration;
            Interlocked.Add(ref _earlierTicks, finishedDuration.IsKnown ? finishedDuration.Ticks : 0);
            Interlocked.Increment(ref _itemsStarted);
            Volatile.Write(ref _audioItem, next);
            Volatile.Write(ref _info, next.Info);
            lock (_items)
            {
                var index = _items.IndexOf(next);
                foreach (var finished in _items.Take(index))
                {
                    finished.Dispose();
                }

                _items.RemoveRange(0, index);
            }

            _session.OnItemStarted(this, next.Info, next.AudioTrack.Id);
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
            _session.OnEnded(this, state.Generation);
        }

        /// <summary>Decodes and plays one packet. False when the track has failed for good.</summary>
        private bool DecodePacket(AudioState state, Packet packet)
        {
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
                    if (processed.Pts.IsKnown)
                    {
                        _clock.Rebase(processed.Pts, _sinkFormat.SampleRate);
                    }

                    _session.OnSeekCompleted(this, state.Generation, processed.Pts.IsKnown ? processed.Pts : _currentSeek.Target);
                }

                var generationToken = GenerationToken(state.Generation);
                try
                {
                    _playGate.Wait(generationToken);
                    _sink.Write(processed, generationToken);
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

        private static void DisposeAll(List<AudioFrame> frames)
        {
            foreach (var frame in frames)
            {
                frame.Dispose();
            }

            frames.Clear();
        }

        private sealed record SeekRequest(long Generation, MediaTime Target, SeekMode Mode);

        /// <summary>What the audio thread carries from one packet to the next.</summary>
        private sealed class AudioState
        {
            public long Generation { get; set; }

            public bool AwaitingFirstFrame { get; set; }

            public int ConsecutiveFailures { get; set; }

            public List<AudioFrame> Decoded { get; } = [];
        }
    }
}
