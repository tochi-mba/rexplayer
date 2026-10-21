using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Rex.Media.Interop.Mac.Audio;
using Rex.Media.Primitives;

namespace Rex.Media.Audio.CoreAudio;

/// <summary>
/// Plays to the Mac's default audio output. The engine writes into a <see cref="SampleRing"/>
/// that the device drains on its own thread; the ring's count of frames taken is the master
/// clock, so only what the device has pulled counts as played. Stereo at most: the output unit
/// mixes to the device, and the pipeline folds anything wider to two channels first.
/// </summary>
[SupportedOSPlatform("macos")]
public sealed class CoreAudioSink : IAudioSink
{
    private static readonly TimeSpan BufferDuration = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan Poll = TimeSpan.FromMilliseconds(5);

    private AudioUnitOutput? _output;
    private SampleRing? _ring;
    private byte[] _scratch = [];
    private bool _paused;

    public string Name => "Mac default audio output";

    public bool IsRealTime => true;

    public long PlayedSamples => _ring?.FramesRead ?? 0;

    public long QueuedSamples => _ring?.QueuedFrames ?? 0;

    public AudioFormat Open(AudioFormat preferred)
    {
        ArgumentNullException.ThrowIfNull(preferred);
        Dispose();
        var channels = Math.Min(preferred.Channels, 2);
        var ring = new SampleRing((int)(preferred.SampleRate * BufferDuration.TotalSeconds), channels);
        _output = AudioUnitOutput.OpenDefault(preferred.SampleRate, channels, buffer => ring.Read(buffer)) ?? throw new IOException("There is no audio output device.");
        _ring = ring;
        if (!_paused)
        {
            _output.Start();
        }

        return new AudioFormat(preferred.SampleRate, channels, SampleFormat.F32, channels == 2 ? ChannelLayout.Stereo : ChannelLayout.Mono);
    }

    public void Write(AudioFrame frame, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(frame);
        var ring = _ring ?? throw new InvalidOperationException("The sink is not open.");
        var bytes = frame.SampleCount * ring.Channels * sizeof(float);
        if (_scratch.Length < bytes)
        {
            _scratch = new byte[bytes];
        }

        SampleConverter.Interleave(frame, SampleFormat.F32, _scratch.AsSpan(0, bytes));
        var samples = MemoryMarshal.Cast<byte, float>(_scratch.AsSpan(0, bytes));
        var written = 0;
        while (written < samples.Length)
        {
            written += ring.Write(samples[written..]);
            if (written < samples.Length)
            {
                cancellationToken.WaitHandle.WaitOne(Poll);
                cancellationToken.ThrowIfCancellationRequested();
            }
        }
    }

    public void Pause()
    {
        _paused = true;
        _output?.Stop();
    }

    public void Resume()
    {
        _paused = false;
        _output?.Start();
    }

    public void Flush()
    {
        // The device must not pull while the ring is emptied under it.
        _output?.Stop();
        _ring?.Clear();
        if (!_paused)
        {
            _output?.Start();
        }
    }

    public void Drain(CancellationToken cancellationToken)
    {
        while (_ring is { QueuedFrames: > 0 } && !_paused)
        {
            cancellationToken.WaitHandle.WaitOne(Poll);
            cancellationToken.ThrowIfCancellationRequested();
        }
    }

    public void Dispose()
    {
        _output?.Dispose();
        _output = null;
        _ring = null;
    }
}
