using Rex.Media.Audio;
using Rex.Media.Codecs;
using Rex.Media.Codecs.Software.Pcm;
using Rex.Media.Containers;
using Rex.Media.Containers.Riff;
using Rex.Media.IO;
using Rex.Media.Primitives;

namespace Rex.Media.Tests.Engine;

/// <summary>A PCM decoder that waits at a gate before each packet, so a test can hold the pipeline mid-seek.</summary>
internal sealed class GatedDecoderFactory : IDecoderFactory, IDisposable
{
    public ManualResetEventSlim Gate { get; } = new(true);

    public string Name => "gated";

    public DecoderSource Source => DecoderSource.Own;

    public int Rank => 1000;

    public bool CanDecode(TrackInfo track) => true;

    public IAudioDecoder CreateAudio(TrackInfo track) => new Decoder(new PcmDecoder(track), Gate);

    public void Dispose() => Gate.Dispose();

    private sealed class Decoder(PcmDecoder inner, ManualResetEventSlim gate) : IAudioDecoder
    {
        public string Name => "gated";

        public DecoderSource Source => DecoderSource.Own;

        public void Decode(Packet packet, ICollection<AudioFrame> output)
        {
            gate.Wait(TimeSpan.FromSeconds(10));
            inner.Decode(packet, output);
        }

        public void Drain(ICollection<AudioFrame> output) => inner.Drain(output);

        public void Flush() => inner.Flush();

        public void Dispose() => inner.Dispose();
    }
}

/// <summary>
/// A WAV demuxer that behaves like a compressed container: seeks land on the start of the 400-sample
/// packet containing the target (the "keyframe"), and an extra track interleaves packets the engine
/// must ignore. Its duration can be hidden to look like a stream that did not declare one.
/// </summary>
internal sealed class CoarseDemuxerFactory(bool hideDuration = false) : IDemuxerFactory
{
    public string Name => "coarse";

    public int Probe(ReadOnlySpan<byte> head, string? extension) => extension == ".coarse" ? 200 : 0;

    public IDemuxer Open(IByteSource source, CancellationToken cancellationToken) => new Demuxer(new WavDemuxer(source, cancellationToken), hideDuration);

    private sealed class Demuxer : IDemuxer
    {
        private readonly WavDemuxer _inner;
        private bool _extraNext = true;

        public Demuxer(WavDemuxer inner, bool hideDuration)
        {
            _inner = inner;
            var audio = inner.Info.Tracks[0];
            Info = inner.Info with
            {
                Duration = hideDuration ? MediaTime.Unknown : inner.Info.Duration,
                Tracks = [new TrackInfo { Id = 9, Codec = CodecId.SubRip }, audio],
            };
        }

        public MediaInfo Info { get; }

        public Packet? ReadPacket(CancellationToken cancellationToken)
        {
            _extraNext = !_extraNext;
            if (_extraNext)
            {
                return Packet.Create(9, MediaBuffer.CopyOf("text"u8), MediaTime.Zero, MediaTime.Zero, MediaTime.Zero, true);
            }

            return _inner.ReadPacket(cancellationToken);
        }

        public void Seek(MediaTime target, CancellationToken cancellationToken)
        {
            var sample = target.ToSamples(8000) / 400 * 400;
            _inner.Seek(MediaTime.FromSamples(sample, 8000), cancellationToken);
        }

        public void Dispose() => _inner.Dispose();
    }
}

/// <summary>A sink whose drain waits until it is cancelled, to test a seek arriving during the final drain.</summary>
internal sealed class StuckDrainSink : IAudioSink
{
    public ManualResetEventSlim Draining { get; } = new(false);

    public string Name => "stuck";

    public long PlayedSamples => 0;

    public long QueuedSamples => 0;

    public bool IsRealTime => false;

    public AudioFormat Open(AudioFormat preferred) => new(preferred.SampleRate, 1, SampleFormat.F32);

    public void Write(AudioFrame frame, CancellationToken cancellationToken)
    {
    }

    public void Pause()
    {
    }

    public void Resume()
    {
    }

    public void Flush()
    {
    }

    public void Drain(CancellationToken cancellationToken)
    {
        Draining.Set();
        cancellationToken.WaitHandle.WaitOne(TimeSpan.FromSeconds(10));
        cancellationToken.ThrowIfCancellationRequested();
    }

    public void Dispose() => Draining.Dispose();
}

/// <summary>A sink whose first drain waits for the test, ignoring cancellation like a slow device would.</summary>
internal sealed class GatedDrainSink : IAudioSink
{
    public ManualResetEventSlim Draining { get; } = new(false);

    public ManualResetEventSlim Release { get; } = new(false);

    public string Name => "gated drain";

    public long PlayedSamples => 0;

    public long QueuedSamples => 0;

    public bool IsRealTime => false;

    public AudioFormat Open(AudioFormat preferred) => new(preferred.SampleRate, 1, SampleFormat.F32);

    public void Write(AudioFrame frame, CancellationToken cancellationToken)
    {
    }

    public void Pause()
    {
    }

    public void Resume()
    {
    }

    public void Flush()
    {
    }

    public void Drain(CancellationToken cancellationToken)
    {
        Draining.Set();
        Release.Wait(TimeSpan.FromSeconds(10), CancellationToken.None);
    }

    public void Dispose()
    {
        Draining.Dispose();
        Release.Dispose();
    }
}

/// <summary>A sink that cannot open, like a device that was unplugged a moment ago.</summary>
internal sealed class UnpluggedSink : IAudioSink
{
    public bool Disposed { get; private set; }

    public string Name => "unplugged";

    public long PlayedSamples => 0;

    public long QueuedSamples => 0;

    public bool IsRealTime => true;

    public AudioFormat Open(AudioFormat preferred) => throw new IOException("There is no audio output device.");

    public void Write(AudioFrame frame, CancellationToken cancellationToken)
    {
    }

    public void Pause()
    {
    }

    public void Resume()
    {
    }

    public void Flush()
    {
    }

    public void Drain(CancellationToken cancellationToken)
    {
    }

    public void Dispose() => Disposed = true;
}

/// <summary>
/// A PCM decoder that hands each packet back as two frames, made outside the pool so a test can see
/// that every one was disposed when the output failed between them.
/// </summary>
internal sealed class SplittingDecoderFactory : IDecoderFactory
{
    public List<AudioFrame> Frames { get; } = [];

    public string Name => "splitting";

    public DecoderSource Source => DecoderSource.Own;

    public int Rank => 1000;

    public bool CanDecode(TrackInfo track) => true;

    public IAudioDecoder CreateAudio(TrackInfo track) => new Decoder(new PcmDecoder(track), Frames);

    private sealed class Decoder(PcmDecoder inner, List<AudioFrame> frames) : IAudioDecoder
    {
        public string Name => "splitting";

        public DecoderSource Source => DecoderSource.Own;

        public void Decode(Packet packet, ICollection<AudioFrame> output)
        {
            var decoded = new List<AudioFrame>();
            inner.Decode(packet, decoded);
            foreach (var whole in decoded)
            {
                var half = whole.SampleCount / 2;
                foreach (var (start, count) in new[] { (0, half), (half, whole.SampleCount - half) })
                {
                    var part = AudioFrame.Rent(pool: null, whole.SampleRate, whole.Channels, count, whole.Layout);
                    part.Pts = whole.Pts;
                    part.Generation = whole.Generation;
                    for (var c = 0; c < whole.Channels; c++)
                    {
                        whole.Channel(c).Slice(start, count).CopyTo(part.Channel(c));
                    }

                    lock (frames)
                    {
                        frames.Add(part);
                    }

                    output.Add(part);
                }

                whole.Dispose();
            }
        }

        public void Drain(ICollection<AudioFrame> output) => inner.Drain(output);

        public void Flush() => inner.Flush();

        public void Dispose() => inner.Dispose();
    }
}
