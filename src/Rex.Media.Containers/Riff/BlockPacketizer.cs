// Spec: none. Shared packet slicing for formats whose audio is a run of fixed-size blocks (PCM, ADPCM, G.711).
using Rex.Media.IO;
using Rex.Media.Primitives;

namespace Rex.Media.Containers.Riff;

/// <summary>
/// Cuts a contiguous run of fixed-size audio blocks into packets of about 50 milliseconds and
/// seeks by arithmetic, which is all WAV, AIFF and raw PCM need. A data size of zero or "unknown"
/// means "until the end of the file", the way streamed WAV files are written.
/// </summary>
internal sealed class BlockPacketizer
{
    private readonly ByteCursor _stream;
    private readonly int _trackId;
    private readonly long _dataStart;
    private readonly long? _dataLength;
    private readonly int _blockAlign;
    private readonly int _samplesPerBlock;
    private readonly int _sampleRate;
    private readonly int _blocksPerPacket;
    private long _nextBlock;

    public BlockPacketizer(ByteCursor stream, int trackId, long dataStart, long? dataLength, int blockAlign, int samplesPerBlock, int sampleRate)
    {
        if (blockAlign <= 0 || samplesPerBlock <= 0 || sampleRate <= 0)
        {
            throw new MediaFormatException("The audio format declares an empty block or a zero sample rate.");
        }

        _stream = stream;
        _trackId = trackId;
        _dataStart = dataStart;
        _dataLength = dataLength is > 0 ? dataLength : null;
        _blockAlign = blockAlign;
        _samplesPerBlock = samplesPerBlock;
        _sampleRate = sampleRate;
        var blocksFor50Ms = Math.Max(1, sampleRate / 20 / samplesPerBlock);
        _blocksPerPacket = Math.Max(1, Math.Min(blocksFor50Ms, (1 << 20) / blockAlign));
        _stream.Seek(dataStart);
    }

    /// <summary>The number of whole blocks, when the data size is known.</summary>
    public long? BlockCount
    {
        get
        {
            var length = DataLength;
            return length is null ? null : length / _blockAlign;
        }
    }

    public MediaTime Duration => BlockCount is { } blocks ? MediaTime.FromSamples(blocks * _samplesPerBlock, _sampleRate) : MediaTime.Unknown;

    private long? DataLength
    {
        get
        {
            if (_dataLength is { } declared)
            {
                return _stream.Length is { } fileLength ? Math.Min(declared, Math.Max(0, fileLength - _dataStart)) : declared;
            }

            return _stream.Length is { } length ? Math.Max(0, length - _dataStart) : null;
        }
    }

    public Packet? ReadPacket(CancellationToken cancellationToken)
    {
        _stream.CancellationToken = cancellationToken;
        var blocks = (long)_blocksPerPacket;
        if (BlockCount is { } total)
        {
            blocks = Math.Min(blocks, total - _nextBlock);
        }

        if (blocks <= 0)
        {
            return null;
        }

        _stream.Seek(_dataStart + (_nextBlock * _blockAlign));
        var buffer = MediaBuffer.Rent((int)blocks * _blockAlign);
        var read = _stream.Read(buffer.Span);
        var whole = read / _blockAlign;
        if (whole == 0)
        {
            buffer.Dispose();
            return null;
        }

        buffer.Truncate(whole * _blockAlign);
        var pts = MediaTime.FromSamples(_nextBlock * _samplesPerBlock, _sampleRate);
        var duration = MediaTime.FromSamples((long)whole * _samplesPerBlock, _sampleRate);
        _nextBlock += whole;
        return Packet.Create(_trackId, buffer, pts, pts, duration, isKeyframe: true);
    }

    public void Seek(MediaTime target)
    {
        var sample = Math.Max(0, target.ToSamples(_sampleRate));
        var block = sample / _samplesPerBlock;
        if (BlockCount is { } total)
        {
            block = Math.Min(block, total);
        }

        _nextBlock = block;
    }
}
