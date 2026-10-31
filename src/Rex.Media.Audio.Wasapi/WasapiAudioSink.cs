using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Rex.Media.Interop.Audio;
using Rex.Media.Primitives;

namespace Rex.Media.Audio.Wasapi;

/// <summary>
/// Plays to Windows' default audio device for media in shared mode. Frames arrive already in the
/// device's rate and layout (the pipeline converted them), so this adapter only interleaves, waits for
/// space in the endpoint buffer and copies. The played-sample count comes from the endpoint's own
/// clock, which is what makes it a trustworthy master clock.
/// <para>
/// The sound follows the default (<see cref="DeviceFollowPolicy"/>): when Windows' media default
/// changes to another device, or the device in use is lost, the next write moves the stream to the
/// new default in the same format (Windows converts), carrying the clock so pictures stay in step.
/// With no device at all it keeps time and tries again every <see cref="DeviceFollowPolicy.Retry"/>.
/// </para>
/// </summary>
[SupportedOSPlatform("windows6.0.6000")]
public sealed class WasapiAudioSink : IAudioSink
{
    private static readonly TimeSpan BufferDuration = TimeSpan.FromMilliseconds(200);

    private readonly Lock _gate = new();
    private WasapiRenderClient? _client;
    private WasapiMixFormat? _format;
    private AudioEndpointWatch? _watch;
    private SampleFormat _storage;
    private byte[] _scratch = [];
    private bool _paused;
    private volatile bool _move;
    private string? _device;
    private long _carried;
    private long _lastAttempt;

    public string Name => "Windows default audio device";

    public bool IsRealTime => true;

    /// <summary>Raised on the audio thread after the sound moved to another device.</summary>
    public event Action? Moved;

    public long PlayedSamples
    {
        get
        {
            lock (_gate)
            {
                return DeviceFollowPolicy.Heard(_carried, Read(client => client.PlayedFrames));
            }
        }
    }

    public long QueuedSamples
    {
        get
        {
            lock (_gate)
            {
                return Read(client => client.Padding);
            }
        }
    }

    public AudioFormat Open(AudioFormat preferred)
    {
        ArgumentNullException.ThrowIfNull(preferred);
        lock (_gate)
        {
            _client?.Dispose();
            _client = WasapiRenderClient.OpenDefault(BufferDuration) ?? throw new IOException("There is no audio output device.");
            (_format, _device, _carried, _move) = (_client.Format, _client.DeviceId, 0, false);
            _watch ??= Watch();
            var mix = _client.Format;
            var (frames, storage) = DeviceFormatPolicy.FromMixFormat(mix.SampleRate, mix.Channels, mix.BitsPerSample, mix.ChannelMask, mix.IsFloat);
            _storage = storage;
            if (!_paused)
            {
                _client.Start();
            }

            return frames;
        }
    }

    public void Write(AudioFrame frame, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(frame);
        var format = _format ?? throw new InvalidOperationException("The sink is not open.");
        var blockAlign = format.BlockAlign;
        var bytes = frame.SampleCount * blockAlign;
        if (_scratch.Length < bytes)
        {
            _scratch = new byte[bytes];
        }

        SampleConverter.Interleave(frame, _storage, _scratch.AsSpan(0, bytes));
        var written = 0;
        while (written < frame.SampleCount)
        {
            if (_move || (_client is null && DeviceFollowPolicy.ShouldRetry(Stopwatch.GetElapsedTime(_lastAttempt))))
            {
                Move();
            }

            var client = _client;
            if (client is null)
            {
                // No device: the rest of this frame is not heard, but time goes on at its pace.
                var left = frame.SampleCount - written;
                cancellationToken.WaitHandle.WaitOne(TimeSpan.FromSeconds((double)left / format.SampleRate));
                cancellationToken.ThrowIfCancellationRequested();
                lock (_gate)
                {
                    _carried += left;
                }

                return;
            }

            try
            {
                var space = (int)(client.BufferFrames - client.Padding);
                if (space <= 0)
                {
                    WaitHandle.WaitAny([client.BufferEvent, cancellationToken.WaitHandle], 100);
                    cancellationToken.ThrowIfCancellationRequested();
                    continue;
                }

                var count = Math.Min(space, frame.SampleCount - written);
                client.Write(_scratch.AsSpan(written * blockAlign, count * blockAlign), count);
                written += count;
            }
            catch (COMException ex) when (DeviceFollowPolicy.IsDeviceLost(ex.HResult))
            {
                _move = true;
            }
        }
    }

    public void Pause()
    {
        lock (_gate)
        {
            _paused = true;
            TryDevice(client => client.Stop());
        }
    }

    public void Resume()
    {
        lock (_gate)
        {
            _paused = false;
            TryDevice(client => client.Start());
        }
    }

    public void Flush()
    {
        lock (_gate)
        {
            // A seek starts the count again on whatever device is in use.
            _carried = 0;
            TryDevice(client =>
            {
                client.Stop();
                client.Reset();
                if (!_paused)
                {
                    client.Start();
                }
            });
        }
    }

    public void Drain(CancellationToken cancellationToken)
    {
        while (_client is { } client)
        {
            try
            {
                if (client.Padding == 0)
                {
                    return;
                }
            }
            catch (COMException ex) when (DeviceFollowPolicy.IsDeviceLost(ex.HResult))
            {
                // What was left in a lost device's buffer cannot be heard any more.
                return;
            }

            WaitHandle.WaitAny([client.BufferEvent, cancellationToken.WaitHandle], 100);
            cancellationToken.ThrowIfCancellationRequested();
        }
    }

    public void Dispose()
    {
        _watch?.Dispose();
        _watch = null;
        lock (_gate)
        {
            _client?.Dispose();
            _client = null;
        }
    }

    /// <summary>Moves the stream to the media default in the same format, carrying the clock; leaves no client when there is no device.</summary>
    private void Move()
    {
        lock (_gate)
        {
            _move = false;
            _lastAttempt = Stopwatch.GetTimestamp();
            if (_client is { } old)
            {
                _carried = DeviceFollowPolicy.Heard(_carried, Read(client => client.PlayedFrames));
                old.Dispose();
                _client = null;
            }

            try
            {
                _client = WasapiRenderClient.OpenDefault(BufferDuration, _format);
            }
            catch (COMException)
            {
                _client = null;
            }

            if (_client is not { } client)
            {
                return;
            }

            _device = client.DeviceId;
            if (!_paused)
            {
                client.Start();
            }
        }

        Moved?.Invoke();
    }

    private AudioEndpointWatch? Watch()
    {
        try
        {
            return new AudioEndpointWatch((playback, role, device) =>
            {
                if (DeviceFollowPolicy.ShouldMove(playback, (EndpointRole)role, device, Volatile.Read(ref _device)))
                {
                    _move = true;
                }
            });
        }
        catch (COMException)
        {
            // Without notifications the sound still moves when its device is lost.
            return null;
        }
    }

    /// <summary>A count from the device in use, 0 without one; a lost device asks for a move rather than throwing.</summary>
    private long Read(Func<WasapiRenderClient, long> read)
    {
        if (_client is not { } client)
        {
            return 0;
        }

        try
        {
            return read(client);
        }
        catch (COMException ex) when (DeviceFollowPolicy.IsDeviceLost(ex.HResult))
        {
            _move = true;
            return 0;
        }
    }

    /// <summary>A device that vanished mid-call moves the sound at the next write; state changes must not throw.</summary>
    private void TryDevice(Action<WasapiRenderClient> action)
    {
        if (_client is null)
        {
            return;
        }

        try
        {
            action(_client);
        }
        catch (COMException)
        {
            _move = true;
        }
    }
}
