// Spec: Apple "Audio Interchange File Format: AIFF" version 1.3 (1989) and the AIFF-C draft (1991): FORM, COMM, SSND, NAME, AUTH, ANNO and "(c) " chunks; IEEE 754 80-bit extended sample rate.
using System.Text;
using Rex.Media.IO;
using Rex.Media.Primitives;

namespace Rex.Media.Containers.Riff;

/// <summary>Recognises AIFF and AIFF-C files.</summary>
public sealed class AiffDemuxerFactory : IDemuxerFactory
{
    public string Name => "aiff";

    public int Probe(ReadOnlySpan<byte> head, string? extension) =>
        head.Length >= 12 && FourCC.Matches(head, "FORM") && (FourCC.Matches(head[8..], "AIFF") || FourCC.Matches(head[8..], "AIFC")) ? 100 : 0;

    public IDemuxer Open(IByteSource source, CancellationToken cancellationToken) => new AiffDemuxer(source, cancellationToken);
}

/// <summary>
/// AIFF and AIFF-C: big-endian PCM in an SSND chunk described by a COMM chunk. AIFF-C adds a
/// compression type; the uncompressed variants (big- and little-endian integers, floats) and G.711
/// are supported. Eight-bit AIFF samples are signed, unlike eight-bit WAV.
/// </summary>
public sealed class AiffDemuxer : IDemuxer
{
    private const int TrackId = 0;
    private readonly BlockPacketizer _packets;

    public AiffDemuxer(IByteSource source, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        var stream = new ByteCursor(source, cancellationToken);
        if (stream.ReadFourCC() != "FORM")
        {
            throw new MediaFormatException("The file is not an IFF FORM.");
        }

        stream.Skip(4);
        var formType = stream.ReadFourCC();
        if (formType is not ("AIFF" or "AIFC"))
        {
            throw new MediaFormatException("The FORM is not AIFF or AIFF-C.");
        }

        var isCompressedForm = formType == "AIFC";
        Common? common = null;
        long? dataStart = null;
        long? dataLength = null;
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        while (!stream.EndOfStream)
        {
            var id = stream.ReadFourCC();
            long size = stream.ReadUInt32BigEndian();
            var start = stream.Position;
            switch (id)
            {
                case "COMM":
                    common = ReadCommon(stream, size, isCompressedForm);
                    break;
                case "SSND":
                    var offset = stream.ReadUInt32BigEndian();
                    stream.Skip(4);
                    dataStart = start + 8 + offset;
                    dataLength = Math.Max(0, size - 8 - offset);
                    break;
                case "NAME":
                    metadata[MetadataKeys.Title] = ReadText(stream, size);
                    break;
                case "AUTH":
                    metadata[MetadataKeys.Artist] = ReadText(stream, size);
                    break;
                case "ANNO":
                    metadata[MetadataKeys.Comment] = ReadText(stream, size);
                    break;
                case "(c) ":
                    metadata[MetadataKeys.Copyright] = ReadText(stream, size);
                    break;
            }

            stream.Seek(start + size + (size & 1));
        }

        if (common is null)
        {
            throw new MediaFormatException("The AIFF file has no COMM chunk.");
        }

        if (dataStart is null)
        {
            throw new MediaFormatException("The AIFF file has no sound data.");
        }

        var blockAlign = common.BytesPerSample * common.Channels;
        var declared = Math.Min(dataLength!.Value, common.SampleFrames * blockAlign);
        _packets = new BlockPacketizer(stream, TrackId, dataStart.Value, declared, blockAlign, 1, common.SampleRate);
        var track = new TrackInfo
        {
            Id = TrackId,
            Codec = common.Codec,
            Duration = _packets.Duration,
            BitRate = (long)common.SampleRate * blockAlign * 8,
            Audio = new AudioTrackInfo
            {
                SampleRate = common.SampleRate,
                Channels = common.Channels,
                Layout = ChannelLayouts.Default(common.Channels),
                BitsPerSample = common.BitsPerSample,
                PcmFormat = common.Format,
                BigEndian = common.BigEndian,
                BlockAlign = blockAlign,
                SamplesPerBlock = 1,
            },
        };
        Info = new MediaInfo
        {
            FormatName = isCompressedForm ? "AIFF-C" : "AIFF",
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

    /// <summary>The value of an IEEE 754 80-bit extended float, as AIFF stores its sample rate.</summary>
    internal static double ReadExtended(ReadOnlySpan<byte> bytes)
    {
        var sign = (bytes[0] & 0x80) != 0 ? -1.0 : 1.0;
        var exponent = ((bytes[0] & 0x7F) << 8) | bytes[1];
        ulong mantissa = 0;
        for (var i = 2; i < 10; i++)
        {
            mantissa = (mantissa << 8) | bytes[i];
        }

        if (exponent == 0 && mantissa == 0)
        {
            return 0;
        }

        if (exponent == 0x7FFF)
        {
            return double.NaN;
        }

        return sign * mantissa * Math.Pow(2, exponent - 16383 - 63);
    }

    private static Common ReadCommon(ByteCursor stream, long size, bool compressedForm)
    {
        if (size < 18)
        {
            throw new MediaFormatException("The AIFF COMM chunk is too short.");
        }

        int channels = stream.ReadUInt16BigEndian();
        long frames = stream.ReadUInt32BigEndian();
        int bits = stream.ReadUInt16BigEndian();
        Span<byte> rateBytes = stackalloc byte[10];
        stream.ReadExactly(rateBytes);
        var rate = ReadExtended(rateBytes);
        if (channels is 0 or > AudioFormat.MaxChannels || double.IsNaN(rate) || rate < 1 || rate > 768_000 || bits is 0 or > 64)
        {
            throw new MediaFormatException("The AIFF COMM chunk declares an impossible audio format.");
        }

        var compression = compressedForm && size >= 22 ? stream.ReadFourCC() : "NONE";
        var bytesPerSample = (bits + 7) / 8;
        var (codec, format, bigEndian) = compression switch
        {
            "NONE" or "twos" => (CodecId.Pcm, IntegerFormat(bytesPerSample), true),
            "sowt" => (CodecId.Pcm, IntegerFormat(bytesPerSample), false),
            "raw " => (CodecId.Pcm, SampleFormat.U8, true),
            "in24" => (CodecId.Pcm, SampleFormat.S24, true),
            "in32" => (CodecId.Pcm, SampleFormat.S32, true),
            "fl32" or "FL32" => (CodecId.Pcm, SampleFormat.F32, true),
            "fl64" or "FL64" => (CodecId.Pcm, SampleFormat.F64, true),
            "alaw" or "ALAW" => (CodecId.Alaw, SampleFormat.Unknown, true),
            "ulaw" or "ULAW" => (CodecId.Mulaw, SampleFormat.Unknown, true),
            _ => (CodecId.Unknown, SampleFormat.Unknown, true),
        };
        if (compression is "fl32" or "FL32" or "in32")
        {
            bytesPerSample = 4;
        }
        else if (compression is "fl64" or "FL64")
        {
            bytesPerSample = 8;
        }
        else if (compression is "in24")
        {
            bytesPerSample = 3;
        }
        else if (codec is CodecId.Alaw or CodecId.Mulaw || compression == "raw ")
        {
            bytesPerSample = 1;
        }

        return new Common(channels, frames, bits, (int)Math.Round(rate), codec, format, bigEndian, bytesPerSample);
    }

    private static SampleFormat IntegerFormat(int bytesPerSample) => bytesPerSample switch
    {
        1 => SampleFormat.S8,
        2 => SampleFormat.S16,
        3 => SampleFormat.S24,
        4 => SampleFormat.S32,
        _ => SampleFormat.Unknown,
    };

    private static string ReadText(ByteCursor stream, long size)
    {
        var bytes = new byte[Math.Min(size, 64 * 1024)];
        stream.ReadExactly(bytes);
        return Encoding.Latin1.GetString(bytes).TrimEnd('\0', ' ');
    }

    private sealed record Common(int Channels, long SampleFrames, int BitsPerSample, int SampleRate, CodecId Codec, SampleFormat Format, bool BigEndian, int BytesPerSample);
}
