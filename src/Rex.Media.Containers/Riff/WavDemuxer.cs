// Spec: Microsoft Multimedia Programming Interface and Data Specifications 1.0 (RIFF, WAVE, LIST/INFO); EBU Tech 3306 (RF64) and ITU-R BS.2088 (BW64) for the 64-bit size chunk.
using System.Buffers.Binary;
using System.Text;
using Rex.Media.IO;
using Rex.Media.Primitives;

namespace Rex.Media.Containers.Riff;

/// <summary>Recognises RIFF, RIFX (big-endian), RF64 and BW64 WAVE files.</summary>
public sealed class WavDemuxerFactory : IDemuxerFactory
{
    public string Name => "wav";

    public int Probe(ReadOnlySpan<byte> head, string? extension)
    {
        if (head.Length < 12 || !FourCC.Matches(head[8..], "WAVE"))
        {
            return 0;
        }

        return FourCC.Matches(head, "RIFF") || FourCC.Matches(head, "RIFX") || FourCC.Matches(head, "RF64") || FourCC.Matches(head, "BW64") ? 100 : 0;
    }

    public IDemuxer Open(IByteSource source, CancellationToken cancellationToken) => new WavDemuxer(source, cancellationToken);
}

/// <summary>
/// WAV files: one audio track in a "data" chunk described by a "fmt " chunk. Chunks may come in any
/// order and unknown ones are skipped. A data size of zero or 0xFFFFFFFF (a recording that was never
/// finalised) plays to the end of the file.
/// </summary>
public sealed class WavDemuxer : IDemuxer
{
    private const int TrackId = 0;
    private readonly ByteCursor _stream;
    private readonly BlockPacketizer _packets;

    public WavDemuxer(IByteSource source, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        _stream = new ByteCursor(source, cancellationToken);
        var riff = _stream.ReadFourCC();
        var bigEndian = riff == "RIFX";
        var is64 = riff is "RF64" or "BW64";
        _stream.Skip(4);
        if (_stream.ReadFourCC() != "WAVE")
        {
            throw new MediaFormatException("The RIFF file is not a WAVE file.");
        }

        WaveFormat? format = null;
        long? dataStart = null;
        long? dataLength = null;
        long? ds64DataLength = null;
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        while (!_stream.EndOfStream && (dataStart is null || format is null))
        {
            var id = _stream.ReadFourCC();
            long size = bigEndian ? _stream.ReadUInt32BigEndian() : _stream.ReadUInt32LittleEndian();
            var start = _stream.Position;
            switch (id)
            {
                case "ds64" when is64:
                    _stream.Skip(8);
                    ds64DataLength = (long)Math.Min(_stream.ReadUInt64LittleEndian(), long.MaxValue);
                    break;
                case "fmt ":
                    var block = new byte[Math.Min(size, 4096)];
                    _stream.ReadExactly(block);
                    format = WaveFormat.Parse(block, bigEndian);
                    break;
                case "LIST":
                    ReadList(size, metadata);
                    break;
                case "data":
                    dataStart = start;
                    dataLength = size is 0 or 0xFFFFFFFF ? null : size;
                    if (is64 && size == 0xFFFFFFFF)
                    {
                        dataLength = ds64DataLength;
                    }

                    break;
            }

            if (dataStart is not null && format is not null)
            {
                break;
            }

            if (id == "data" && dataLength is null)
            {
                // An unbounded data chunk runs to the end of the file; nothing can follow it.
                break;
            }

            _stream.Seek(start + size + (size & 1));
        }

        if (format is null)
        {
            throw new MediaFormatException("The WAVE file has no format chunk.");
        }

        if (dataStart is null)
        {
            throw new MediaFormatException("The WAVE file has no audio data.");
        }

        _packets = new BlockPacketizer(_stream, TrackId, dataStart.Value, dataLength, format.BlockAlign, format.SamplesPerBlock, format.SampleRate);
        var track = format.ToTrack(TrackId, _packets.Duration);
        track = track with { Audio = track.Audio! with { BigEndian = bigEndian } };
        Info = new MediaInfo
        {
            FormatName = is64 ? "RF64" : "WAVE",
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

    private void ReadList(long size, Dictionary<string, string> metadata)
    {
        if (size < 4 || size > 1 << 20)
        {
            return;
        }

        var list = new byte[size];
        _stream.ReadExactly(list);
        if (!FourCC.Matches(list, "INFO"))
        {
            return;
        }

        var offset = 4;
        while (offset + 8 <= list.Length)
        {
            var id = FourCC.ToString(list.AsSpan(offset, 4));
            var length = (int)Math.Min(BinaryPrimitives.ReadUInt32LittleEndian(list.AsSpan(offset + 4)), (uint)(list.Length - offset - 8));
            var text = Encoding.UTF8.GetString(list, offset + 8, length).TrimEnd('\0', ' ');
            if (InfoKey(id) is { } key && text.Length > 0)
            {
                metadata[key] = text;
            }

            offset += 8 + length + (length & 1);
        }
    }

    private static string? InfoKey(string id) => id switch
    {
        "INAM" => MetadataKeys.Title,
        "IART" => MetadataKeys.Artist,
        "IPRD" => MetadataKeys.Album,
        "ICMT" => MetadataKeys.Comment,
        "ICRD" => MetadataKeys.Date,
        "IGNR" => MetadataKeys.Genre,
        "ITRK" or "IPRT" => MetadataKeys.Track,
        "ICOP" => MetadataKeys.Copyright,
        "ISFT" => MetadataKeys.Encoder,
        _ => null,
    };
}
