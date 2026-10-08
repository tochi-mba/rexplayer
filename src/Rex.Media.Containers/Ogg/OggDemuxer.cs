// Spec: IETF RFC 3533 (the Ogg page: capture pattern, header type, granule position, serial, sequence, CRC-32 with polynomial 0x04C11DB7, lacing values), the Vorbis I specification appendix A (Vorbis in Ogg: three header packets, granule position as the last sample of the last packet ending on a page), RFC 7845 sections 3-5 (Opus in Ogg: OpusHead, OpusTags, 48 kHz granule positions, pre-skip, end trimming) and the Ogg FLAC mapping 1.0 (a 0x7F "FLAC" packet carrying STREAMINFO, then metadata blocks).
using System.Buffers.Binary;
using Rex.Media.Codecs.Flac;
using Rex.Media.Codecs.Opus;
using Rex.Media.Codecs.Vorbis;
using Rex.Media.Containers.Tags;
using Rex.Media.IO;
using Rex.Media.Primitives;

namespace Rex.Media.Containers.Ogg;

/// <summary>Recognises Ogg streams (.ogg, .oga, .opus) by their "OggS" capture pattern.</summary>
public sealed class OggDemuxerFactory : IDemuxerFactory
{
    public string Name => "ogg";

    public int Probe(ReadOnlySpan<byte> head, string? extension) => head.StartsWith("OggS"u8) ? 100 : 0;

    public IDemuxer Open(IByteSource source, CancellationToken cancellationToken) => new OggDemuxer(source, cancellationToken);
}

/// <summary>
/// Ogg files of Vorbis, Opus or FLAC sound (FMT-C09). Ogg records a position only for the last packet
/// ending on each page, so each packet's length is worked out from the packet itself (its Vorbis
/// block size, its Opus TOC byte, its FLAC frame header) and its time counted back from the page's
/// position. Seeking narrows by bisection over the pages' positions.
/// </summary>
public sealed class OggDemuxer : IDemuxer
{
    private const int MaxPage = 27 + 255 + (255 * 255);
    private const int EndScan = 128 * 1024;

    private readonly ByteCursor _stream;
    private readonly List<OggStream> _streams = [];
    private readonly Queue<Packet> _ready = new();
    private readonly long _firstAudioPage;

    public OggDemuxer(IByteSource source, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        _stream = new ByteCursor(source, cancellationToken);
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var pictures = new List<PictureBlock>();

        // The headers: every stream's first page comes first (RFC 3533 section 4), then the rest of
        // their header packets, which end a page of their own; the sound starts on the page after.
        while (ReadPage() is { } page)
        {
            if (page.BeginsStream)
            {
                _streams.Add(new OggStream(page.Serial));
            }

            if (_streams.FirstOrDefault(s => s.Serial == page.Serial) is { HeadersDone: false } stream)
            {
                foreach (var packet in stream.Assemble(page))
                {
                    stream.Header(packet, metadata, pictures);
                }
            }

            if (!page.BeginsStream && _streams.All(s => s.HeadersDone))
            {
                break;
            }
        }

        var dataStart = _stream.Position;

        var tracks = _streams.Where(s => s.Track is not null).ToList();
        if (tracks.Count == 0)
        {
            throw new MediaFormatException("The Ogg file carries no Vorbis, Opus or FLAC stream rexplayer can play.");
        }

        _streams.RemoveAll(s => s.Track is null);
        var length = _stream.Length;
        if (length is { } total && source.CanSeek)
        {
            ReadLastPositions(total);
        }

        for (var i = 0; i < _streams.Count; i++)
        {
            _streams[i].Finish(i);
        }

        _firstAudioPage = dataStart;
        _stream.Seek(_firstAudioPage);
        foreach (var stream in _streams)
        {
            stream.Reset();
        }

        Info = new MediaInfo
        {
            FormatName = "Ogg",
            Tracks = [.. _streams.Select(s => s.Track!)],
            Duration = _streams.Select(s => s.Track!.Duration).Where(d => d.IsKnown).DefaultIfEmpty(MediaTime.Unknown).Max(),
            IsSeekable = source.CanSeek && length is not null,
            Metadata = metadata,
            CoverArt = PictureBlock.Cover(pictures)?.Data,
        };
    }

    public MediaInfo Info { get; }

    public Packet? ReadPacket(CancellationToken cancellationToken)
    {
        _stream.CancellationToken = cancellationToken;
        while (_ready.Count == 0)
        {
            if (ReadPage() is not { } page)
            {
                return null;
            }

            if (_streams.FirstOrDefault(s => s.Serial == page.Serial) is { } stream)
            {
                foreach (var packet in stream.Packets(page))
                {
                    _ready.Enqueue(packet);
                }
            }
        }

        return _ready.Dequeue();
    }

    public void Seek(MediaTime target, CancellationToken cancellationToken)
    {
        _stream.CancellationToken = cancellationToken;
        var main = _streams[0];

        // Start early enough for the decoder to settle: a long Vorbis block, or Opus's 80 ms.
        var granule = Math.Max(0, main.GranuleFor(target) - main.PreRoll);
        var low = _firstAudioPage;
        var high = _stream.Length ?? _firstAudioPage;
        while (high - low > MaxPage)
        {
            var middle = low + ((high - low) / 2);
            var found = FindPageFrom(middle, main.Serial, high);
            if (found is { } page && page.Granule >= 0 && page.Granule <= granule)
            {
                low = page.Offset;
            }
            else
            {
                high = middle;
            }
        }

        // Then page by page: the sound starts after the last page of the stream that ends before the target.
        var start = _firstAudioPage;
        _stream.Seek(low);
        while (ReadPage() is { } page)
        {
            if (page.Serial != main.Serial || page.Granule < 0)
            {
                continue;
            }

            if (page.Granule > granule)
            {
                break;
            }

            start = _stream.Position;
        }

        _stream.Seek(start);
        _ready.Clear();
        foreach (var stream in _streams)
        {
            stream.Reset();
            stream.SkipPartial = start > _firstAudioPage;
        }
    }

    public void Dispose()
    {
    }

    /// <summary>The first page of <paramref name="serial"/> at or after <paramref name="offset"/>, before <paramref name="limit"/>.</summary>
    private (long Offset, long Granule)? FindPageFrom(long offset, int serial, long limit)
    {
        _stream.Seek(offset);
        while (_stream.Position < limit && ReadPage() is { } page)
        {
            if (page.Serial == serial && page.Granule >= 0)
            {
                return (page.Offset, page.Granule);
            }
        }

        return null;
    }

    /// <summary>The last granule position of each stream, from the pages at the end of the file: the length.</summary>
    private void ReadLastPositions(long length)
    {
        _stream.Seek(Math.Max(0, length - EndScan));
        while (ReadPage() is { } page)
        {
            if (_streams.FirstOrDefault(s => s.Serial == page.Serial) is { } stream && page.Granule >= 0)
            {
                stream.LastGranule = page.Granule;
            }
        }
    }

    /// <summary>
    /// The next page whose CRC checks out, skipping anything damaged or not Ogg; null at the end.
    /// </summary>
    private OggPage? ReadPage()
    {
        var header = new byte[27];
        while (true)
        {
            var offset = _stream.Position;
            if (_stream.Peek(header) < 27)
            {
                return null;
            }

            if (!header.AsSpan().StartsWith("OggS"u8) || header[4] != 0)
            {
                _stream.Skip(1);
                continue;
            }

            var segments = header[26];
            var table = new byte[segments];
            _stream.Skip(27);
            var whole = _stream.Read(table) == segments;
            var body = new byte[table.Sum(lace => lace)];
            whole = whole && _stream.Read(body) == body.Length;
            var page = new OggPage(offset, header, table, body);
            if (!whole || !page.ChecksOut)
            {
                // Damaged (or cut short by a damaged length): look for the next page from just past this one's capture pattern.
                _stream.Seek(offset + 1);
                continue;
            }

            return page;
        }
    }
}

/// <summary>One page: its header fields, lacing values and body.</summary>
internal sealed class OggPage(long offset, byte[] header, byte[] lacing, byte[] body)
{
    public long Offset { get; } = offset;

    public bool Continues => (header[5] & 1) != 0;

    public bool BeginsStream => (header[5] & 2) != 0;

    public bool EndsStream => (header[5] & 4) != 0;

    public long Granule { get; } = BinaryPrimitives.ReadInt64LittleEndian(header.AsSpan(6));

    public int Serial { get; } = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(14));

    public byte[] Lacing => lacing;

    public byte[] Body => body;

    /// <summary>Whether the page's CRC (taken with its own CRC field as zero) matches.</summary>
    public bool ChecksOut
    {
        get
        {
            var expected = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(22));
            var copy = header.ToArray();
            copy.AsSpan(22, 4).Clear();
            var crc = Crc.Crc32Ogg;
            var register = crc.Append(crc.Start(), copy);
            register = crc.Append(register, lacing);
            register = crc.Append(register, body);
            return crc.Finish(register) == expected;
        }
    }
}

/// <summary>One logical stream of an Ogg file: its codec, its headers, and where its packets are in time.</summary>
internal sealed class OggStream(int serial)
{
    private readonly List<byte> _partial = [];
    private readonly List<byte[]> _headers = [];
    private CodecId _codec;
    private int _headerCount;
    private int _sampleRate;
    private int _channels;
    private byte[] _codecPrivate = [];
    private VorbisSetup? _vorbis;
    private OpusHead? _opus;
    private FlacStreamInfo? _flac;
    private int _previousBlock;
    private long? _position;
    private int _trackId;

    public int Serial { get; } = serial;

    public bool HeadersDone { get; private set; }

    public TrackInfo? Track { get; private set; }

    public long LastGranule { get; set; } = -1;

    /// <summary>After a seek into the middle of the file: a packet carried over from an earlier page is dropped.</summary>
    public bool SkipPartial { get; set; }

    /// <summary>The granule positions to start before a target, so the decoder settles before it.</summary>
    public long PreRoll => _codec switch
    {
        CodecId.Vorbis => _vorbis?.LongBlock ?? 0,
        CodecId.Opus => 3840,
        _ => 0,
    };

    public long GranuleFor(MediaTime time) => time.ToSamples(_sampleRate) + (_opus?.PreSkip ?? 0);

    /// <summary>The packets ending on <paramref name="page"/>, joined across pages.</summary>
    public List<byte[]> Assemble(OggPage page)
    {
        var packets = new List<byte[]>();
        if (!page.Continues || SkipPartial)
        {
            // A page that does not continue a packet ends whatever was left unfinished.
            _partial.Clear();
        }

        var skipping = page.Continues && SkipPartial;
        SkipPartial = false;
        var at = 0;
        foreach (var lace in page.Lacing)
        {
            if (!skipping)
            {
                _partial.AddRange(page.Body.AsSpan(at, lace));
            }

            at += lace;
            if (lace < 255)
            {
                if (!skipping)
                {
                    packets.Add([.. _partial]);
                }

                _partial.Clear();
                skipping = false;
            }
        }

        return packets;
    }

    /// <summary>Reads one header packet: the first names the codec; the rest carry tags and setup.</summary>
    public void Header(byte[] packet, Dictionary<string, string> metadata, List<PictureBlock> pictures)
    {
        _headers.Add(packet);
        if (_headers.Count == 1)
        {
            Identify(packet);
            return;
        }

        switch (_codec)
        {
            case CodecId.Vorbis when packet.AsSpan().StartsWith("\x03vorbis"u8):
                VorbisComments.Read(packet.AsSpan(7), metadata);
                break;
            case CodecId.Opus when packet.AsSpan().StartsWith("OpusTags"u8):
                VorbisComments.Read(packet.AsSpan(8), metadata);
                break;
            case CodecId.Flac when packet.Length >= 4:
                var type = packet[0] & 0x7F;
                if (type == 4)
                {
                    VorbisComments.Read(packet.AsSpan(4), metadata);
                }
                else if (type == 6 && PictureBlock.Parse(packet.AsSpan(4)) is { } picture)
                {
                    pictures.Add(picture);
                }

                break;
        }

        HeadersDone = _headers.Count >= _headerCount;
        if (HeadersDone && _codec == CodecId.Vorbis)
        {
            _vorbis = VorbisSetup.FromHeaders(_headers[0], _headers[2]);
            _codecPrivate = Lace(_headers);
        }
    }

    /// <summary>Makes the track, numbered <paramref name="id"/>, once the length is known.</summary>
    public void Finish(int id)
    {
        _trackId = id;
        var end = LastGranule - (_opus?.PreSkip ?? 0);
        Track = Track! with
        {
            Id = id,
            CodecPrivate = _codecPrivate,
            Duration = LastGranule >= 0 && end >= 0 ? MediaTime.FromSamples(end, _sampleRate) : MediaTime.Unknown,
        };
    }

    public void Reset()
    {
        _partial.Clear();
        _previousBlock = 0;
        _position = null;
    }

    /// <summary>The packets ending on <paramref name="page"/>, timed from the page's granule position.</summary>
    public List<Packet> Packets(OggPage page)
    {
        var data = Assemble(page);
        var lengths = data.Select(Samples).ToList();

        // Each page's position times its own packets, so a page lost to damage leaves no drift; the
        // last page's position marks where the stream ends instead, so it carries on from the one before.
        if (page.Granule >= 0 && (!page.EndsStream || _position is null))
        {
            _position = page.Granule - lengths.Sum();
        }

        var packets = new List<Packet>();
        var position = _position ?? 0;
        for (var i = 0; i < data.Count; i++)
        {
            var start = position - (_opus?.PreSkip ?? 0);
            var pts = MediaTime.FromSamples(start, _sampleRate);
            var packet = Packet.Create(_trackId, MediaBuffer.CopyOf(data[i]), pts, pts, MediaTime.FromSamples(lengths[i], _sampleRate), isKeyframe: true);
            position += lengths[i];

            // The last page may end the stream before its last packet does (RFC 7845 section 4.4).
            if (i == data.Count - 1 && page.EndsStream && page.Granule >= 0 && position > page.Granule)
            {
                packet.DiscardSamples = (int)Math.Min(lengths[i], position - page.Granule);
            }

            packets.Add(packet);
        }

        if (data.Count > 0)
        {
            _position = page.Granule >= 0 ? page.Granule : position;
        }

        return packets;
    }

    private static byte[] Lace(List<byte[]> packets)
    {
        var laced = new List<byte> { (byte)(packets.Count - 1) };
        foreach (var packet in packets[..^1])
        {
            var size = packet.Length;
            while (size >= 255)
            {
                laced.Add(255);
                size -= 255;
            }

            laced.Add((byte)size);
        }

        foreach (var packet in packets)
        {
            laced.AddRange(packet);
        }

        return [.. laced];
    }

    private void Identify(byte[] packet)
    {
        var span = packet.AsSpan();
        if (span.StartsWith("\x01vorbis"u8) && packet.Length >= 30)
        {
            (_codec, _headerCount) = (CodecId.Vorbis, 3);
            _channels = packet[11];
            _sampleRate = (int)BinaryPrimitives.ReadUInt32LittleEndian(span[12..]);
        }
        else if (span.StartsWith("OpusHead"u8))
        {
            _opus = OpusHead.Parse(span);
            (_codec, _headerCount, _channels, _sampleRate, _codecPrivate) = (CodecId.Opus, 2, _opus.Channels, 48000, packet);
        }
        else if (span.Length >= 17 + FlacStreamInfo.Size && span[0] == 0x7F && span[1..].StartsWith("FLAC"u8))
        {
            // The mapping's header count leaves out this first packet.
            _flac = FlacStreamInfo.Parse(span.Slice(17, FlacStreamInfo.Size));
            _codecPrivate = span.Slice(17, FlacStreamInfo.Size).ToArray();
            (_codec, _headerCount, _channels, _sampleRate) = (CodecId.Flac, 1 + BinaryPrimitives.ReadUInt16BigEndian(span[7..]), _flac.Channels, _flac.SampleRate);
        }
        else
        {
            // Another codec (Theora, Speex and the rest): not played, so its stream is left alone.
            HeadersDone = true;
            return;
        }

        Track = new TrackInfo
        {
            Id = 0,
            Codec = _codec,
            IsDefault = true,
            Audio = new AudioTrackInfo
            {
                SampleRate = _sampleRate,
                Channels = _channels,
                Layout = ChannelLayouts.Default(_channels),
                BitsPerSample = _flac?.BitsPerSample ?? 0,
            },
        };
        HeadersDone = _headerCount <= 1;
    }

    /// <summary>How many samples (at the stream's rate; 48 kHz for Opus) a packet decodes to.</summary>
    private int Samples(byte[] packet)
    {
        switch (_codec)
        {
            case CodecId.Vorbis:
                // A Vorbis packet gives the samples between its block's centre and the one before it.
                var block = _vorbis!.BlockSizeOf(packet);
                if (block == 0)
                {
                    return 0;
                }

                var samples = _previousBlock == 0 ? 0 : (_previousBlock / 4) + (block / 4);
                _previousBlock = block;
                return samples;
            case CodecId.Opus:
                return OpusPacket.Samples(packet);
            default:
                return FlacFrameHeader.TryParse(packet, out var header) ? header.BlockSize : 0;
        }
    }
}
