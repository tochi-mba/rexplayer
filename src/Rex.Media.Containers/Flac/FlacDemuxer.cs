// Spec: IETF RFC 9639 (FLAC), section 8 "File-level metadata" (STREAMINFO, PADDING, APPLICATION, SEEKTABLE, VORBIS_COMMENT, CUESHEET, PICTURE) and section 9 "Frame structure" (frames found by sync code, header CRC-8 and footer CRC-16).
using System.Buffers;
using System.Buffers.Binary;
using System.Globalization;
using Rex.Media.Codecs.Flac;
using Rex.Media.Containers.Tags;
using Rex.Media.IO;
using Rex.Media.Primitives;

namespace Rex.Media.Containers.Flac;

/// <summary>Recognises native FLAC streams by their "fLaC" marker.</summary>
public sealed class FlacDemuxerFactory : IDemuxerFactory
{
    public string Name => "flac";

    public int Probe(ReadOnlySpan<byte> head, string? extension) => head.StartsWith("fLaC"u8) ? 100 : 0;

    public IDemuxer Open(IByteSource source, CancellationToken cancellationToken) => new FlacDemuxer(source, cancellationToken);
}

/// <summary>
/// Native FLAC files. A FLAC stream records no frame lengths, so frames are found the way RFC 9639
/// intends: a frame ends where the next valid header begins (sync code, reserved bits, header
/// CRC-8, and the next frame or sample number), confirmed by the footer CRC-16. Seeking uses the
/// SEEKTABLE when there is one and narrows the rest by bisection over frame headers.
/// </summary>
public sealed class FlacDemuxer : IDemuxer
{
    private const int TrackId = 0;
    private const int MaxMetadataBlock = 16 << 20;
    private const int LeadOutTrack = 170;
    private const int LeadOutTrackNonCd = 255;

    /// <summary>How far past damaged metadata the first frame is looked for.</summary>
    private const int FirstFrameSearch = MaxMetadataBlock;

    private readonly ByteCursor _stream;
    private readonly ScanWindow _window;
    private readonly FlacStreamInfo _streamInfo;
    private readonly long _firstFrame;
    private readonly List<(long Sample, long Offset)> _seekPoints = [];
    private readonly int _frameBound;
    private bool? _variable;

    public FlacDemuxer(IByteSource source, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        _stream = new ByteCursor(source, cancellationToken);
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var pictures = new List<PictureBlock>();
        var chapters = new List<Chapter>();
        var start = Id3v2.LeadingTagsLength(source, cancellationToken);
        if (start is > 0 and <= MaxMetadataBlock)
        {
            var tag = new byte[start];
            _stream.ReadExactly(tag);
            if (Id3v2.Read(tag) is { } id3)
            {
                foreach (var (key, value) in id3.Metadata)
                {
                    metadata[key] = value;
                }

                pictures.AddRange(id3.Pictures);
            }
        }

        _stream.Seek(start);
        if (_stream.ReadFourCC() != "fLaC")
        {
            throw new MediaFormatException("The file is not a FLAC stream.");
        }

        var header = _stream.ReadByte();
        var length = (int)_stream.ReadUInt24BigEndian();
        if ((header & 0x7F) != 0)
        {
            throw new MediaFormatException("The FLAC stream does not start with a STREAMINFO block.");
        }

        var streamInfoBytes = ReadBlock(length);
        var streamInfo = FlacStreamInfo.Parse(streamInfoBytes);
        streamInfoBytes = streamInfoBytes[..FlacStreamInfo.Size];
        var afterStreamInfo = _stream.Position;
        var damaged = false;
        var last = (header & 0x80) != 0;
        while (!last && !damaged)
        {
            var blockStart = _stream.Position;
            try
            {
                header = _stream.ReadByte();
                last = (header & 0x80) != 0;
                length = (int)_stream.ReadUInt24BigEndian();
                damaged = !ReadMetadataBlock(header & 0x7F, length, streamInfo.SampleRate, metadata, pictures, chapters);
            }
            catch (MediaFormatException)
            {
                // A block that runs past the end of the file: the metadata is damaged from here.
                damaged = true;
            }

            if (damaged)
            {
                _stream.Seek(blockStart);
            }
        }

        // Damaged metadata (a block with a wrong length, say) costs the tags, not the sound: the
        // frames are found by their sync code after the stream information.
        _firstFrame = damaged ? FindFirstFrame(streamInfo, afterStreamInfo) : _stream.Position;
        (_streamInfo, streamInfoBytes) = Repaired(streamInfo, streamInfoBytes, _firstFrame);
        _window = new ScanWindow(_stream, _firstFrame);

        // The largest frame the stream can hold: verbatim samples, one extra bit for a side channel.
        _frameBound = _streamInfo.MaxFrameSize > 0
            ? _streamInfo.MaxFrameSize + 1024
            : (_streamInfo.MaxBlockSize * _streamInfo.Channels * ((_streamInfo.BitsPerSample + 8) / 8)) + 1024;
        var duration = _streamInfo.TotalSamples > 0 ? MediaTime.FromSamples(_streamInfo.TotalSamples, _streamInfo.SampleRate) : MediaTime.Unknown;
        var track = new TrackInfo
        {
            Id = TrackId,
            Codec = CodecId.Flac,
            CodecPrivate = streamInfoBytes,
            Duration = duration,
            IsDefault = true,
            Audio = new AudioTrackInfo
            {
                SampleRate = _streamInfo.SampleRate,
                Channels = _streamInfo.Channels,
                Layout = FlacStreamInfo.Layout(_streamInfo.Channels),
                BitsPerSample = _streamInfo.BitsPerSample,
            },
        };
        Info = new MediaInfo
        {
            FormatName = "FLAC",
            Tracks = [track],
            Duration = duration,
            IsSeekable = source.CanSeek,
            Metadata = metadata,
            Chapters = chapters,
            CoverArt = PictureBlock.Cover(pictures)?.Data,
        };
    }

    public MediaInfo Info { get; }

    public Packet? ReadPacket(CancellationToken cancellationToken)
    {
        _stream.CancellationToken = cancellationToken;
        while (true)
        {
            _window.Ensure(FlacFrameHeader.MaxLength);
            var span = _window.Span;
            if (span.Length == 0)
            {
                return null;
            }

            if (!FlacFrameHeader.TryParse(span, out var header) || !Fits(header))
            {
                // Lost sync (damage, or junk after the audio): skip to the next byte that could start a frame.
                var next = span.Length > 1 ? span[1..].IndexOf((byte)0xFF) : -1;
                _window.Consume(next < 0 ? span.Length : next + 1);
                continue;
            }

            _variable ??= header.VariableBlockSize;
            var length = FrameLength(header);
            var buffer = MediaBuffer.CopyOf(_window.Span[..length]);
            _window.Consume(length);
            var pts = MediaTime.FromSamples(header.FirstSample(_streamInfo.MaxBlockSize), _streamInfo.SampleRate);
            return Packet.Create(TrackId, buffer, pts, pts, MediaTime.FromSamples(header.BlockSize, _streamInfo.SampleRate), isKeyframe: true);
        }
    }

    public void Seek(MediaTime target, CancellationToken cancellationToken)
    {
        _stream.CancellationToken = cancellationToken;
        var sample = Math.Max(0, target.ToSamples(_streamInfo.SampleRate));
        var low = _firstFrame;
        var high = _stream.Length ?? long.MaxValue;
        foreach (var (pointSample, offset) in _seekPoints)
        {
            if (pointSample <= sample)
            {
                low = _firstFrame + offset;
            }
            else
            {
                high = Math.Min(high, _firstFrame + offset);
                break;
            }
        }

        while (high != long.MaxValue && high - low > 2)
        {
            var middle = low + ((high - low) / 2);
            if (FindFrame(middle, high) is not { } found || found.Sample > sample)
            {
                high = middle;
                continue;
            }

            low = found.Offset;
            if (sample < found.Sample + found.BlockSize)
            {
                break;
            }
        }

        _window.Reset(low);
    }

    public void Dispose()
    {
    }

    private byte[] ReadBlock(int length)
    {
        if (_stream.Remaining is { } remaining && length > remaining)
        {
            throw new MediaFormatException("A FLAC metadata block runs past the end of the file.");
        }

        var block = new byte[length];
        _stream.ReadExactly(block);
        return block;
    }

    /// <summary>
    /// Reads one metadata block after STREAMINFO (the readers pass over damaged contents); false
    /// when the block itself cannot be right, so the blocks after it cannot be found.
    /// </summary>
    private bool ReadMetadataBlock(int type, int length, int sampleRate, Dictionary<string, string> metadata, List<PictureBlock> pictures, List<Chapter> chapters)
    {
        if (type is 0 or 127 || (_stream.Remaining is { } remaining && length > remaining))
        {
            // A second STREAMINFO, the forbidden type or a length past the end: not a block at all.
            return false;
        }

        if (type is not (3 or 4 or 5 or 6))
        {
            _stream.Skip(length);
            return true;
        }

        var block = ReadBlock(length);
        switch (type)
        {
            case 3:
                ReadSeekTable(block);
                break;
            case 4:
                VorbisComments.Read(block, metadata);
                break;
            case 5:
                chapters.AddRange(ReadCueSheet(block, sampleRate));
                break;
            default:
                if (PictureBlock.Parse(block) is { } picture)
                {
                    pictures.Add(picture);
                }

                break;
        }

        return true;
    }

    /// <summary>
    /// Where the frames start when the metadata is damaged: the first frame header after
    /// <paramref name="from"/> that numbers the stream's first frame, else the first that fits the
    /// stream, else <paramref name="from"/> itself (reading then scans on from there).
    /// </summary>
    private long FindFirstFrame(FlacStreamInfo info, long from)
    {
        var size = (int)Math.Min(FirstFrameSearch, Math.Max(0, (_stream.Length ?? (from + FirstFrameSearch)) - from));
        var buffer = ArrayPool<byte>.Shared.Rent(size);
        try
        {
            _stream.Seek(from);
            var span = buffer.AsSpan(0, _stream.Read(buffer.AsSpan(0, size)));
            long? fitting = null;
            for (var i = span.IndexOf((byte)0xFF); i >= 0 && i + 1 < span.Length; i = NextSync(span, i))
            {
                if (FlacFrameHeader.IsSync(span[i], span[i + 1]) && FlacFrameHeader.TryParse(span[i..], out var header) && Fits(info, header, header.VariableBlockSize))
                {
                    if (header.CodedNumber == 0)
                    {
                        return from + i;
                    }

                    fitting ??= from + i;
                }
            }

            return fitting ?? from;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        static int NextSync(ReadOnlySpan<byte> span, int at)
        {
            var next = span[(at + 1)..].IndexOf((byte)0xFF);
            return next < 0 ? -1 : at + 1 + next;
        }
    }

    /// <summary>
    /// The stream information, put right from the first frame when damage left it describing a
    /// stream no frame fits (a wrong channel count, rate, sample size or block size): otherwise no
    /// frame would ever be found and the whole file would be read for nothing.
    /// </summary>
    private (FlacStreamInfo Info, byte[] Bytes) Repaired(FlacStreamInfo info, byte[] bytes, long firstFrame)
    {
        Span<byte> head = stackalloc byte[FlacFrameHeader.MaxLength];
        _stream.Seek(firstFrame);
        var read = _stream.Read(head);
        if (!FlacFrameHeader.TryParse(head[..read], out var first) || Fits(info, first, first.VariableBlockSize))
        {
            return (info, bytes);
        }

        var blockSize = Math.Max(first.BlockSize, 16);
        var repaired = info with
        {
            Channels = first.Channels,
            SampleRate = first.SampleRate > 0 ? first.SampleRate : info.SampleRate,
            BitsPerSample = first.BitsPerSample > 0 ? first.BitsPerSample : info.BitsPerSample,
            MinBlockSize = first.VariableBlockSize ? Math.Min(info.MinBlockSize, blockSize) : blockSize,
            MaxBlockSize = first.VariableBlockSize ? Math.Max(info.MaxBlockSize, blockSize) : Math.Max(info.MaxBlockSize == info.MinBlockSize ? blockSize : info.MaxBlockSize, blockSize),
            MinFrameSize = 0,
            MaxFrameSize = 0,
        };
        return (repaired, repaired.ToBytes());
    }

    private void ReadSeekTable(byte[] table)
    {
        for (var offset = 0; offset + 18 <= table.Length; offset += 18)
        {
            var sample = BinaryPrimitives.ReadUInt64BigEndian(table.AsSpan(offset));
            if (sample == ulong.MaxValue)
            {
                // A placeholder the encoder left for a later tool to fill in.
                continue;
            }

            var point = ((long)Math.Min(sample, long.MaxValue), (long)Math.Min(BinaryPrimitives.ReadUInt64BigEndian(table.AsSpan(offset + 8)), long.MaxValue));
            if (_seekPoints.Count == 0 || point.Item1 > _seekPoints[^1].Sample)
            {
                _seekPoints.Add(point);
            }
        }
    }

    private static List<Chapter> ReadCueSheet(byte[] block, int sampleRate)
    {
        var chapters = new List<Chapter>();
        const int TracksAt = 128 + 8 + 1 + 258;
        if (block.Length <= TracksAt)
        {
            return chapters;
        }

        var count = block[TracksAt];
        var offset = TracksAt + 1;
        for (var i = 0; i < count && offset + 36 <= block.Length; i++)
        {
            var start = BinaryPrimitives.ReadUInt64BigEndian(block.AsSpan(offset));
            var number = block[offset + 8];
            var indexes = block[offset + 35];
            offset += 36 + (indexes * 12);
            if (number is not LeadOutTrack and not LeadOutTrackNonCd)
            {
                var title = string.Create(CultureInfo.InvariantCulture, $"Track {number:00}");
                chapters.Add(new Chapter(MediaTime.FromSamples((long)Math.Min(start, long.MaxValue), sampleRate), title));
            }
        }

        return chapters;
    }

    /// <summary>Whether a header belongs to this stream rather than being a sync pattern inside audio data.</summary>
    private bool Fits(FlacFrameHeader header) => Fits(_streamInfo, header, _variable ?? header.VariableBlockSize);

    private static bool Fits(FlacStreamInfo info, FlacFrameHeader header, bool variable) =>
        header.VariableBlockSize == variable
        && header.Channels == info.Channels
        && (header.SampleRate == 0 || header.SampleRate == info.SampleRate)
        && (header.BitsPerSample == 0 || header.BitsPerSample == info.BitsPerSample)
        && header.BlockSize <= info.MaxBlockSize;

    private static bool Follows(FlacFrameHeader current, FlacFrameHeader next) =>
        next.CodedNumber == current.CodedNumber + (current.VariableBlockSize ? current.BlockSize : 1);

    /// <summary>
    /// Where the frame at the window's start ends: at the first following header that fits the stream
    /// and makes the CRC-16 of everything before it come out as zero. A damaged frame has no such
    /// point, so past the largest possible frame size it ends at the next header that continues the
    /// numbering, or failing that the next header at all.
    /// </summary>
    private int FrameLength(FlacFrameHeader header)
    {
        var crc = Crc.Crc16Flac;
        var scanned = header.Length;
        var register = crc.Append(crc.Start(), _window.Span[..scanned]);
        var unconfirmed = -1;
        var loose = -1;
        while (true)
        {
            var span = _window.Span;
            var limit = span.Length - 1;
            var needMore = false;
            while (scanned < limit)
            {
                var next = span[scanned..limit].IndexOf((byte)0xFF);
                var stop = next < 0 ? limit : scanned + next;
                register = crc.Append(register, span[scanned..stop]);
                scanned = stop;
                if (scanned == limit)
                {
                    break;
                }

                if (FlacFrameHeader.IsSync(span[scanned], span[scanned + 1]))
                {
                    if (scanned + FlacFrameHeader.MaxLength > span.Length && !_window.AtEnd)
                    {
                        needMore = true;
                        break;
                    }

                    if (FlacFrameHeader.TryParse(span[scanned..], out var candidate) && Fits(candidate))
                    {
                        // A zero CRC residue exactly at a valid header settles it, even if frames are
                        // missing in between; otherwise remember the candidate in case nothing better comes.
                        if (register == 0)
                        {
                            return scanned;
                        }

                        unconfirmed = Follows(header, candidate) && unconfirmed < 0 ? scanned : unconfirmed;
                        loose = loose < 0 ? scanned : loose;
                    }
                }

                register = crc.Append(register, span.Slice(scanned, 1));
                scanned++;
            }

            if (scanned > _frameBound && (unconfirmed >= 0 || loose >= 0))
            {
                // Damage: no boundary checks out. Cut at the best guess and let the decoder conceal it.
                return unconfirmed >= 0 ? unconfirmed : loose;
            }

            if ((needMore || !_window.AtEnd) && _window.Grow())
            {
                continue;
            }

            return LastFrameLength(header, unconfirmed);
        }
    }

    /// <summary>
    /// The last frame runs to the end of the file, unless a tag follows it (ID3v1, APEv2, Lyrics3 or
    /// an appended ID3v2): then it ends where its CRC-16 comes out as zero right before the tag.
    /// </summary>
    private int LastFrameLength(FlacFrameHeader header, int unconfirmed)
    {
        if (unconfirmed >= 0)
        {
            return unconfirmed;
        }

        var span = _window.Span;
        var crc = Crc.Crc16Flac;
        var register = crc.Append(crc.Start(), span[..header.Length]);
        for (var i = header.Length; i < span.Length; i++)
        {
            register = crc.Append(register, span.Slice(i, 1));
            if (register == 0 && IsTrailingTag(span[(i + 1)..]))
            {
                return i + 1;
            }
        }

        return span.Length;
    }

    private static bool IsTrailingTag(ReadOnlySpan<byte> rest) =>
        rest.StartsWith("TAG"u8) || rest.StartsWith("APETAGEX"u8) || rest.StartsWith("ID3"u8) || rest.StartsWith("LYRICSBEGIN"u8);

    /// <summary>
    /// The first frame header that fits the stream and starts at or after <paramref name="position"/>
    /// and before <paramref name="limit"/>. A header starting just before the limit is still read
    /// whole, or bisection could close in on a frame it can never see.
    /// </summary>
    private (long Offset, long Sample, int BlockSize)? FindFrame(long position, long limit)
    {
        var starts = (int)Math.Min(limit - position, Math.Max(128 * 1024, 2L * _frameBound));
        var size = starts + FlacFrameHeader.MaxLength;
        var buffer = ArrayPool<byte>.Shared.Rent(size);
        try
        {
            _stream.Seek(position);
            var span = buffer.AsSpan(0, _stream.Read(buffer.AsSpan(0, size)));
            for (var i = 0; i < starts && i + 1 < span.Length; i++)
            {
                if (FlacFrameHeader.IsSync(span[i], span[i + 1]) && FlacFrameHeader.TryParse(span[i..], out var header) && Fits(header))
                {
                    _variable ??= header.VariableBlockSize;
                    return (position + i, header.FirstSample(_streamInfo.MaxBlockSize), header.BlockSize);
                }
            }

            return null;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}
