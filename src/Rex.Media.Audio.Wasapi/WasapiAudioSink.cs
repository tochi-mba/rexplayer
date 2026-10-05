using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Rex.Media.Interop.Audio;
using Rex.Media.Primitives;

namespace Rex.Media.Audio.Wasapi;

/// <summary>
/// Plays to the Windows default audio device in shared mode. Frames arrive already in the device's
/// rate and layout (the pipeline converted them), so this adapter only interleaves, waits for space
/// in the endpoint buffer and copies. The played-sample count comes from the endpoint's own clock,
/// which is what makes it a trustworthy master clock.
/// </summary>
[SupportedOSPlatform("windows6.0.6000")]
public sealed class WasapiAudioSink : IAudioSink
{
    private static readonly TimeSpan BufferDuration = TimeSpan.FromMilliseconds(200);

    private WasapiRenderClient? _client;
    private SampleFormat _storage;
    private byte[] _scratch = [];
    private bool _paused;

    public string Name => "Windows default audio device";

    public bool IsRealTime => true;

    public long PlayedSamples => _client?.PlayedFrames ?? 0;

    public long QueuedSamples => _client?.Padding ?? 0;

    public AudioFormat Open(AudioFormat preferred)
    {
        ArgumentNullException.ThrowIfNull(preferred);
        _client?.Dispose();
        _client = WasapiRenderClient.OpenDefault(BufferDuration) ?? throw new IOException("There is no audio output device.");
        var mix = _client.Format;
        var (frames, storage) = DeviceFormatPolicy.FromMixFormat(mix.SampleRate, mix.Channels, mix.BitsPerSample, mix.ChannelMask, mix.IsFloat);
        _storage = storage;
        if (!_paused)
        {
            _client.Start();
        }

        return frames;
    }

    public void Write(AudioFrame frame, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(frame);
        var client = _client ?? throw new InvalidOperationException("The sink is not open.");
        var blockAlign = client.Format.BlockAlign;
        var bytes = frame.SampleCount * blockAlign;
        if (_scratch.Length < bytes)
        {
            _scratch = new byte[bytes];
        }

        SampleConverter.Interleave(frame, _storage, _scratch.AsSpan(0, bytes));
        var written = 0;
        while (written < frame.SampleCount)
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
    }

    public void Pause()
    {
        _paused = true;
        TryDevice(client => client.Stop());
    }

    public void Resume()
    {
        _paused = false;
        TryDevice(client => client.Start());
    }

    public void Flush() => TryDevice(client =>
    {
        client.Stop();
        client.Reset();
        if (!_paused)
        {
            client.Start();
        }
    });

    public void Drain(CancellationToken cancellationToken)
    {
        var client = _client;
        while (client is not null && client.Padding > 0)
        {
            WaitHandle.WaitAny([client.BufferEvent, cancellationToken.WaitHandle], 100);
            cancellationToken.ThrowIfCancellationRequested();
        }
    }

    public void Dispose()
    {
        _client?.Dispose();
        _client = null;
    }

    /// <summary>A device that vanished mid-call is reported by the next write; state changes must not throw.</summary>
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
            // The endpoint was unplugged or disabled; the engine's device-loss handling takes over.
        }
    }
}
