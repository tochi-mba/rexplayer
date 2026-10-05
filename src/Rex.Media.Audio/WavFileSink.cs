using System.Buffers.Binary;
using Rex.Media.Primitives;

namespace Rex.Media.Audio;

/// <summary>
/// Writes what would have been heard to a WAV file, exactly, as fast as the engine produces it. This
/// is the audio output of headless runs and the oracle of every audio test: the captured samples are
/// compared against what the fixture encodes. A sidecar timeline can record which sample count each
/// frame started at, so tests can check timing as well as content.
/// </summary>
public sealed class WavFileSink : IAudioSink
{
    private readonly string _path;
    private readonly SampleFormat _storage;
    private FileStream? _file;
    private AudioFormat? _format;
    private long _written;
    private byte[] _scratch = [];

    public WavFileSink(string path, SampleFormat storage = SampleFormat.F32)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (storage is not (SampleFormat.S16 or SampleFormat.S24 or SampleFormat.F32))
        {
            throw new ArgumentOutOfRangeException(nameof(storage), storage, "A capture file stores 16-bit, 24-bit or float samples.");
        }

        _path = path;
        _storage = storage;
    }

    public string Name => "WAV file " + Path.GetFileName(_path);

    public long PlayedSamples => _written;

    public long QueuedSamples => 0;

    public bool IsRealTime => false;

    /// <summary>Total samples per channel written to the file.</summary>
    public long TotalSamples { get; private set; }

    public AudioFormat Open(AudioFormat preferred)
    {
        ArgumentNullException.ThrowIfNull(preferred);
        _file?.Dispose();
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
        _format = new AudioFormat(preferred.SampleRate, preferred.Channels, _storage, preferred.Layout);
        _file = new FileStream(_path, FileMode.Create, FileAccess.ReadWrite, FileShare.Read);
        _file.Write(Header(_format, 0));
        _written = 0;
        TotalSamples = 0;
        return new AudioFormat(preferred.SampleRate, preferred.Channels, SampleFormat.F32, preferred.Layout);
    }

    public void Write(AudioFrame frame, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(frame);
        var file = _file ?? throw new InvalidOperationException("The sink is not open.");
        var format = _format!;
        if (frame.Channels != format.Channels || frame.SampleRate != format.SampleRate)
        {
            throw new InvalidOperationException("The frame does not match the format the sink was opened with.");
        }

        var size = frame.SampleCount * format.BlockAlign;
        if (_scratch.Length < size)
        {
            _scratch = new byte[size];
        }

        SampleConverter.Interleave(frame, _storage, _scratch.AsSpan(0, size));
        file.Write(_scratch, 0, size);
        _written += frame.SampleCount;
        TotalSamples += frame.SampleCount;
    }

    public void Pause()
    {
    }

    public void Resume()
    {
    }

    /// <summary>A seek in a capture keeps the file: the played count restarts, the samples stay.</summary>
    public void Flush() => _written = 0;

    public void Drain(CancellationToken cancellationToken) => _file?.Flush();

    public void Dispose()
    {
        if (_file is null)
        {
            return;
        }

        var dataBytes = TotalSamples * _format!.BlockAlign;
        _file.Position = 0;
        _file.Write(Header(_format, dataBytes));
        _file.Dispose();
        _file = null;
    }

    /// <summary>A canonical 44-byte (PCM) or 58-byte-free extensible-less header.</summary>
    internal static byte[] Header(AudioFormat format, long dataBytes)
    {
        var header = new byte[44];
        var span = header.AsSpan();
        var dataSize = (uint)Math.Min(dataBytes, uint.MaxValue - 36);
        "RIFF"u8.CopyTo(span);
        BinaryPrimitives.WriteUInt32LittleEndian(span[4..], dataSize + 36);
        "WAVE"u8.CopyTo(span[8..]);
        "fmt "u8.CopyTo(span[12..]);
        BinaryPrimitives.WriteUInt32LittleEndian(span[16..], 16);
        BinaryPrimitives.WriteUInt16LittleEndian(span[20..], format.SampleFormat == SampleFormat.F32 ? (ushort)3 : (ushort)1);
        BinaryPrimitives.WriteUInt16LittleEndian(span[22..], (ushort)format.Channels);
        BinaryPrimitives.WriteUInt32LittleEndian(span[24..], (uint)format.SampleRate);
        BinaryPrimitives.WriteUInt32LittleEndian(span[28..], (uint)(format.SampleRate * format.BlockAlign));
        BinaryPrimitives.WriteUInt16LittleEndian(span[32..], (ushort)format.BlockAlign);
        BinaryPrimitives.WriteUInt16LittleEndian(span[34..], (ushort)(format.BytesPerSample * 8));
        "data"u8.CopyTo(span[36..]);
        BinaryPrimitives.WriteUInt32LittleEndian(span[40..], dataSize);
        return header;
    }
}
