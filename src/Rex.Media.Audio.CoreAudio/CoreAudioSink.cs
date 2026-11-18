using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Rex.Media.Interop.Mac.Audio;
using Rex.Media.Primitives;

namespace Rex.Media.Audio.CoreAudio;

/// <summary>
/// Plays to the Mac's default audio output. The engine writes into a <see cref="SampleRing"/>
/// that the device drains on its own thread; the ring's count of frames taken is the master
/// clock, so only what the device has pulled counts as played. The format asked of the output
/// unit is the policy's decision (<see cref="DeviceFormatPolicy.ForConvertingOutput"/>): the
/// stream's own channels when the default device has that many outputs, else as many as it has,
/// with the pure mixer folding the rest before frames arrive here.
/// <para>
/// The sound follows the default (<see cref="DeviceFollowPolicy"/>): when the Mac's default
/// output changes to another device, or goes away, the next write moves the stream to the new
/// default in the same format (the unit converts), keeping the ring and the clock so pictures
/// stay in step. With no device at all it keeps time and tries again every
/// <see cref="DeviceFollowPolicy.Retry"/>.
/// </para>
/// </summary>
[SupportedOSPlatform("macos")]
public sealed class CoreAudioSink : IAudioSink
{
    private static readonly TimeSpan BufferDuration = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan Poll = TimeSpan.FromMilliseconds(5);

    private readonly Lock _gate = new();
    private AudioUnitOutput? _output;
    private SampleRing? _ring;
    private AudioFormat? _format;
    private DefaultOutputWatch? _watch;
    private byte[] _scratch = [];
    private bool _paused;
    private volatile bool _move;
    private uint _device;
    private long _carried;
    private long _lastAttempt;

    public string Name => "Mac default audio output";

    public bool IsRealTime => true;

    /// <summary>Raised on the audio thread after the sound moved to another device.</summary>
    public event Action? Moved;

    public long PlayedSamples
    {
        get
        {
            lock (_gate)
            {
                return DeviceFollowPolicy.Heard(_carried, _ring?.FramesRead ?? 0);
            }
        }
    }

    public long QueuedSamples => _ring?.QueuedFrames ?? 0;

    public AudioFormat Open(AudioFormat preferred)
    {
        ArgumentNullException.ThrowIfNull(preferred);
        lock (_gate)
        {
            _output?.Dispose();
            _output = null;
            var device = DefaultOutputWatch.Device();
            var format = DeviceFormatPolicy.ForConvertingOutput(preferred, DefaultOutputWatch.OutputChannels(device));
            var ring = new SampleRing((int)(format.SampleRate * BufferDuration.TotalSeconds), format.Channels);
            _output = AudioUnitOutput.OpenDefault(format.SampleRate, format.Channels, buffer => ring.Read(buffer)) ?? throw new IOException("There is no audio output device.");
            (_ring, _format, _carried, _move) = (ring, format, 0, false);
            Volatile.Write(ref _device, device);
            _watch ??= Watch();
            if (!_paused)
            {
                _output.Start();
            }

            return format;
        }
    }

    public void Write(AudioFrame frame, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(frame);
        var ring = _ring ?? throw new InvalidOperationException("The sink is not open.");
        var format = _format!;
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
            if (_move || (_output is null && DeviceFollowPolicy.ShouldRetry(Stopwatch.GetElapsedTime(_lastAttempt))))
            {
                Move();
            }

            if (_output is null)
            {
                // No device: the rest of this frame is not heard, but time goes on at its pace.
                var left = (samples.Length - written) / ring.Channels;
                cancellationToken.WaitHandle.WaitOne(TimeSpan.FromSeconds((double)left / format.SampleRate));
                cancellationToken.ThrowIfCancellationRequested();
                lock (_gate)
                {
                    _carried += left;
                }

                return;
            }

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
        lock (_gate)
        {
            _paused = true;
            TryDevice(output => output.Stop());
        }
    }

    public void Resume()
    {
        lock (_gate)
        {
            _paused = false;
            TryDevice(output => output.Start());
        }
    }

    public void Flush()
    {
        lock (_gate)
        {
            // A seek starts the count again; the device must not pull while the ring is emptied under it.
            _carried = 0;
            TryDevice(output => output.Stop());
            _ring?.Clear();
            if (!_paused)
            {
                TryDevice(output => output.Start());
            }
        }
    }

    public void Drain(CancellationToken cancellationToken)
    {
        while (_ring is { QueuedFrames: > 0 } && !_paused)
        {
            if (_move || (_output is null && DeviceFollowPolicy.ShouldRetry(Stopwatch.GetElapsedTime(_lastAttempt))))
            {
                Move();
            }

            if (_output is null)
            {
                // What is queued cannot be heard with no device to pull it.
                return;
            }

            cancellationToken.WaitHandle.WaitOne(Poll);
            cancellationToken.ThrowIfCancellationRequested();
        }
    }

    public void Dispose()
    {
        _watch?.Dispose();
        _watch = null;
        lock (_gate)
        {
            _output?.Dispose();
            _output = null;
            _ring = null;
            _format = null;
        }
    }

    /// <summary>
    /// Moves the stream to the new default in the session's format, which the unit converts; the
    /// ring goes on unchanged, so queued audio survives the move and the clock never steps. Leaves
    /// no output when there is no device.
    /// </summary>
    private void Move()
    {
        lock (_gate)
        {
            _move = false;
            _lastAttempt = Stopwatch.GetTimestamp();
            _output?.Dispose();
            _output = null;
            if (_format is not { } format || _ring is not { } ring)
            {
                return;
            }

            var device = DefaultOutputWatch.Device();
            if (device == 0)
            {
                return;
            }

            _output = AudioUnitOutput.OpenDefault(format.SampleRate, format.Channels, buffer => ring.Read(buffer));
            if (_output is not { } opened)
            {
                return;
            }

            Volatile.Write(ref _device, device);
            if (!_paused)
            {
                opened.Start();
            }
        }

        Moved?.Invoke();
    }

    private DefaultOutputWatch? Watch()
    {
        try
        {
            return new DefaultOutputWatch(device =>
            {
                if (DeviceFollowPolicy.ShouldMove(device, Volatile.Read(ref _device)))
                {
                    _move = true;
                }
            });
        }
        catch (IOException)
        {
            // Without notifications the sound still plays; it just cannot follow the default.
            return null;
        }
    }

    /// <summary>A device that vanished mid-call moves the sound at the next write; state changes must not throw.</summary>
    private void TryDevice(Action<AudioUnitOutput> action)
    {
        if (_output is null)
        {
            return;
        }

        try
        {
            action(_output);
        }
        catch (IOException)
        {
            _move = true;
        }
    }
}
