using Rex.Media.Audio;
using Rex.Media.Codecs;
using Rex.Media.Codecs.Software.Pcm;
using Rex.Media.Containers;
using Rex.Media.Containers.Riff;
using Rex.Media.Engine;
using Rex.Media.IO;
using Rex.Media.Primitives;
using Rex.Media.TestKit;
using Rex.Media.Video;

namespace Rex.Media.Tests.Engine;

/// <summary>A media session wired to in-memory parts, with its events collected for assertions.</summary>
internal sealed class SessionHarness : IDisposable
{
    private readonly List<SessionEvent> _events = [];
    private readonly object _gate = new();

    public SessionHarness(bool autoPlay = true, IAudioSink? sink = null, IDecoderFactory? decoder = null, IDemuxerFactory? demuxer = null, int maxCorrupt = 30, IDecoderFactory? videoDecoder = null, IVideoPresenter? presenter = null, int videoQueueCapacity = 32)
    {
        Sink = sink ?? new RecordingAudioSink(channels: 1);
        var demuxers = new DemuxerRegistry().Add(new WavDemuxerFactory());
        if (demuxer is not null)
        {
            demuxers.Add(demuxer);
        }

        var decoders = new DecoderRegistry().Add(decoder ?? new PcmDecoderFactory());
        if (videoDecoder is not null)
        {
            decoders.Add(videoDecoder);
        }

        Session = new MediaSession(
            new EngineOptions
            {
                Demuxers = demuxers,
                Decoders = decoders,
                AudioSinkFactory = () => Sink,
                AutoPlay = autoPlay,
                MaxConsecutiveCorruptPackets = maxCorrupt,
                PositionInterval = TimeSpan.Zero,
                VideoPresenterFactory = presenter is null ? null : () => presenter,
                VideoQueueCapacity = videoQueueCapacity,
            },
            Record);
    }

    public MediaSession Session { get; }

    public IAudioSink Sink { get; }

    public RecordingAudioSink Recording => (RecordingAudioSink)Sink;

    public IReadOnlyList<SessionEvent> Events
    {
        get
        {
            lock (_gate)
            {
                return [.. _events];
            }
        }
    }

    public IEnumerable<SessionState> States => Events.OfType<StateChangedEvent>().Select(e => e.To);

    public static IByteSource Source(byte[] file, string name = "clip.wav") => new MemoryByteSource(file, name);

    /// <summary>Waits until an event matching <paramref name="match"/> has been delivered.</summary>
    public T WaitFor<T>(Func<T, bool>? match = null)
        where T : SessionEvent
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        lock (_gate)
        {
            while (true)
            {
                var found = _events.OfType<T>().FirstOrDefault(e => match?.Invoke(e) ?? true);
                if (found is not null)
                {
                    return found;
                }

                var remaining = deadline - DateTime.UtcNow;
                if (remaining <= TimeSpan.Zero)
                {
                    throw new TimeoutException($"No {typeof(T).Name} arrived. Saw: {string.Join(", ", _events.Select(e => e.GetType().Name))}");
                }

                Monitor.Wait(_gate, remaining);
            }
        }
    }

    public void WaitForState(SessionState state) => WaitFor<StateChangedEvent>(e => e.To == state);

    public Task FinishAsync() => Session.WaitForFinishAsync().WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

    public void Dispose() => Session.Dispose();

    private void Record(SessionEvent sessionEvent)
    {
        lock (_gate)
        {
            _events.Add(sessionEvent);
            Monitor.PulseAll(_gate);
        }
    }
}

/// <summary>A decoder that fails on every packet, for the error-policy tests.</summary>
internal sealed class BrokenDecoderFactory : IDecoderFactory
{
    public string Name => "broken";

    public DecoderSource Source => DecoderSource.Own;

    public int Rank => 1000;

    public bool CanDecode(TrackInfo track) => true;

    public IAudioDecoder CreateAudio(TrackInfo track) => new Decoder();

    private sealed class Decoder : IAudioDecoder
    {
        public string Name => "broken";

        public DecoderSource Source => DecoderSource.Own;

        public void Decode(Packet packet, ICollection<AudioFrame> output)
        {
            output.Add(AudioFrame.Rent(8000, 1, 1));
            throw new MediaFormatException("corrupt");
        }

        public void Drain(ICollection<AudioFrame> output)
        {
        }

        public void Flush()
        {
        }

        public void Dispose()
        {
        }
    }
}

/// <summary>
/// A demuxer over a WAV file that misbehaves after a number of packets: either the file turns out to
/// be damaged (a format error) or the disk fails (an I/O error).
/// </summary>
internal sealed class FailingDemuxerFactory(int goodPackets, Exception failure) : IDemuxerFactory
{
    public string Name => "failing";

    public int Probe(ReadOnlySpan<byte> head, string? extension) => extension == ".fail" ? 200 : 0;

    public IDemuxer Open(IByteSource source, CancellationToken cancellationToken) => new Demuxer(new WavDemuxer(source, cancellationToken), goodPackets, failure);

    private sealed class Demuxer(WavDemuxer inner, int goodPackets, Exception failure) : IDemuxer
    {
        private int _read;

        public MediaInfo Info => inner.Info;

        public Packet? ReadPacket(CancellationToken cancellationToken) =>
            _read++ < goodPackets ? inner.ReadPacket(cancellationToken) : throw failure;

        public void Seek(MediaTime target, CancellationToken cancellationToken) => inner.Seek(target, cancellationToken);

        public void Dispose() => inner.Dispose();
    }
}
