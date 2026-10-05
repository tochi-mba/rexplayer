using Rex.Media.Audio;
using Rex.Media.Primitives;

namespace Rex.Media.TestKit;

/// <summary>
/// An audio sink that keeps everything written to it in memory, as fast as the engine produces it,
/// so engine tests can assert on exact samples, timestamps and the order of sink calls.
/// </summary>
public sealed class RecordingAudioSink : IAudioSink
{
    private readonly object _gate = new();
    private readonly List<float>[] _channels;
    private readonly int? _forcedRate;
    private AudioFormat? _format;
    private long _played;

    public RecordingAudioSink(int channels = 2, int? sampleRate = null)
    {
        _channels = Enumerable.Range(0, channels).Select(_ => new List<float>()).ToArray();
        _forcedRate = sampleRate;
    }

    public string Name => "recording";

    public bool IsRealTime => false;

    public long PlayedSamples
    {
        get
        {
            lock (_gate)
            {
                return _played;
            }
        }
    }

    public long QueuedSamples => 0;

    /// <summary>Calls in the order the engine made them: "open", "write", "pause", "resume", "flush", "drain".</summary>
    public List<string> Calls { get; } = [];

    /// <summary>The first timestamp of each frame written.</summary>
    public List<MediaTime> FrameTimestamps { get; } = [];

    public bool Disposed { get; private set; }

    public AudioFormat? Format => _format;

    /// <summary>Raised before each write, so a test can act at an exact point in the stream.</summary>
    public event Action<AudioFrame>? Writing;

    public AudioFormat Open(AudioFormat preferred)
    {
        ArgumentNullException.ThrowIfNull(preferred);
        lock (_gate)
        {
            Calls.Add("open");
            _format = new AudioFormat(_forcedRate ?? preferred.SampleRate, _channels.Length, SampleFormat.F32);
            return _format;
        }
    }

    public void Write(AudioFrame frame, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(frame);
        Writing?.Invoke(frame);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            Calls.Add("write");
            FrameTimestamps.Add(frame.Pts);
            for (var c = 0; c < _channels.Length; c++)
            {
                _channels[c].AddRange(frame.Channel(c).ToArray());
            }

            _played += frame.SampleCount;
        }
    }

    public float[] Channel(int index)
    {
        lock (_gate)
        {
            return [.. _channels[index]];
        }
    }

    public int SampleCount
    {
        get
        {
            lock (_gate)
            {
                return _channels[0].Count;
            }
        }
    }

    public void Pause() => Record("pause");

    public void Resume() => Record("resume");

    public void Flush()
    {
        lock (_gate)
        {
            Calls.Add("flush");
            _played = 0;
        }
    }

    public void Drain(CancellationToken cancellationToken) => Record("drain");

    public void Dispose()
    {
        Record("dispose");
        Disposed = true;
    }

    private void Record(string call)
    {
        lock (_gate)
        {
            Calls.Add(call);
        }
    }
}
