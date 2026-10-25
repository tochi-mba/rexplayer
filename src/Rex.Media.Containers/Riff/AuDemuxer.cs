// Spec: Sun Microsystems "Audio File Formats" (1992), AU file header: magic, data offset/size, encoding, sample rate, channels and annotation.
using System.Text;
using Rex.Media.IO;
using Rex.Media.Primitives;

namespace Rex.Media.Containers.Riff;

/// <summary>Recognises Sun/NeXT AU audio by its big-endian .snd magic.</summary>
public sealed class AuDemuxerFactory : IDemuxerFactory
{
    public string Name => "au";

    public int Probe(ReadOnlySpan<byte> head, string? extension) =>
        head.Length >= 24 && head.StartsWith(".snd"u8) ? 100 : 0;

    public IDemuxer Open(IByteSource source, CancellationToken cancellationToken) => new AuDemuxer(source, cancellationToken);
}

/// <summary>One audio stream stored after the fixed AU header and its optional annotation.</summary>
public sealed class AuDemuxer : IDemuxer
{
    private const int TrackId = 0;
    private readonly BlockPacketizer _packets;

    public AuDemuxer(IByteSource source, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        var stream = new ByteCursor(source, cancellationToken);
        if (stream.ReadFourCC() != ".snd")
        {
            throw new MediaFormatException("The file is not Sun/NeXT audio.");
        }

        var dataOffset = stream.ReadUInt32BigEndian();
        var declaredSize = stream.ReadUInt32BigEndian();
        var encoding = stream.ReadUInt32BigEndian();
        var sampleRate = stream.ReadUInt32BigEndian();
        var channels = stream.ReadUInt32BigEndian();
        if (dataOffset < 24 || sampleRate is 0 or > 768_000 || channels is 0 or > AudioFormat.MaxChannels)
        {
            throw new MediaFormatException("The AU header declares an impossible audio format.");
        }

        var (codec, format, bytesPerSample, bitsPerSample) = encoding switch
        {
            1 => (CodecId.Mulaw, SampleFormat.Unknown, 1, 8),
            2 => (CodecId.Pcm, SampleFormat.S8, 1, 8),
            3 => (CodecId.Pcm, SampleFormat.S16, 2, 16),
            4 => (CodecId.Pcm, SampleFormat.S24, 3, 24),
            5 => (CodecId.Pcm, SampleFormat.S32, 4, 32),
            6 => (CodecId.Pcm, SampleFormat.F32, 4, 32),
            7 => (CodecId.Pcm, SampleFormat.F64, 8, 64),
            27 => (CodecId.Alaw, SampleFormat.Unknown, 1, 8),
            _ => (CodecId.Unknown, SampleFormat.Unknown, 1, 0),
        };
        var blockAlign = checked(bytesPerSample * (int)channels);
        var dataLength = declaredSize == uint.MaxValue ? null : (long?)declaredSize;
        _packets = new BlockPacketizer(stream, TrackId, dataOffset, dataLength, blockAlign, 1, (int)sampleRate);

        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (dataOffset > 24)
        {
            var annotation = new byte[dataOffset - 24];
            stream.Seek(24);
            stream.ReadExactly(annotation);
            var comment = Encoding.UTF8.GetString(annotation).TrimEnd('\0', ' ');
            if (comment.Length > 0)
            {
                metadata[MetadataKeys.Comment] = comment;
            }
        }

        var track = new TrackInfo
        {
            Id = TrackId,
            Codec = codec,
            Duration = _packets.Duration,
            BitRate = (long)sampleRate * blockAlign * 8,
            Audio = new AudioTrackInfo
            {
                SampleRate = (int)sampleRate,
                Channels = (int)channels,
                Layout = ChannelLayouts.Default((int)channels),
                BitsPerSample = bitsPerSample,
                PcmFormat = format,
                BigEndian = true,
                BlockAlign = blockAlign,
                SamplesPerBlock = 1,
            },
        };
        Info = new MediaInfo
        {
            FormatName = "AU",
            Tracks = [track],
            Duration = _packets.Duration,
            IsSeekable = source.CanSeek,
            Metadata = metadata,
        };
    }

    public MediaInfo Info { get; }

    public Packet? ReadPacket(CancellationToken cancellationToken) => _packets.ReadPacket(cancellationToken);

    public void Seek(MediaTime target, CancellationToken cancellationToken) => _packets.Seek(target);

    public void Dispose()
    {
    }
}
