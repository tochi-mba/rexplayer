// Spec: IETF RFC 9559 (Matroska) sections 4.5 and 6 (Segment structure, unknown-size elements, element positions relative to the segment), 5.1.3.1 (Cluster, Timestamp, SimpleBlock, BlockGroup, Block, BlockDuration, ReferenceBlock, DiscardPadding), 10 (Block structure and lacing: Xiph, EBML and fixed-size), 11 (TimestampScale, CodecDelay, SeekPreRoll), 12 (Cues and seeking); WebM's subset of the same.
using Rex.Media.IO;
using Rex.Media.Primitives;

namespace Rex.Media.Containers.Matroska;

/// <summary>Recognises Matroska and WebM files by their EBML header.</summary>
public sealed class MatroskaDemuxerFactory : IDemuxerFactory
{
    public string Name => "matroska";

    public int Probe(ReadOnlySpan<byte> head, string? extension) => head.StartsWith((ReadOnlySpan<byte>)[0x1A, 0x45, 0xDF, 0xA3]) ? 100 : 0;

    public IDemuxer Open(IByteSource source, CancellationToken cancellationToken) => new MatroskaDemuxer(source, cancellationToken);
}

/// <summary>
/// Matroska and WebM files. Headers are read when the file opens, including the ones a seek head
/// says lie after the clusters; clusters are then read one element at a time, which also works
/// for live streams whose clusters have no size. Laced blocks become one packet per frame.
/// </summary>
public sealed class MatroskaDemuxer : IDemuxer
{
    private const long MaxHeaderBytes = 64L << 20;
    private const long MaxBlockBytes = 64L << 20;

    private readonly ByteCursor _stream;
    private readonly long _segmentStart;
    private readonly long _segmentEnd;
    private readonly long _firstCluster;
    private long _timestampScale = 1_000_000;
    private readonly Dictionary<int, MatroskaTrack> _tracks = [];
    private readonly List<(long Time, int Track, long Position, long Relative)> _cues = [];
    private readonly Queue<Packet> _pending = new();
    private List<(long Position, long Time)>? _clusterIndex;
    private long _position;
    private bool _inCluster;
    private long _clusterEnd;
    private long _clusterTime;
    private int? _awaitKeyframe;
    private long _resumeAt;

    public MatroskaDemuxer(IByteSource source, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        _stream = new ByteCursor(source, cancellationToken);
        var (headerId, _) = Ebml.Read(_stream, keepMarker: true);
        if (headerId != MatroskaId.Ebml)
        {
            throw new MediaFormatException("The file is not EBML, so it is not Matroska or WebM.");
        }

        var header = ReadBody();
        var docType = Ebml.Find(header, MatroskaId.DocType) is { } doc ? Ebml.Text(header.AsSpan(doc.Start, doc.End - doc.Start)) : "matroska";
        var (segmentId, _) = Ebml.Read(_stream, keepMarker: true);
        if (segmentId != MatroskaId.Segment)
        {
            throw new MediaFormatException("The Matroska file has no segment.");
        }

        var (segmentSize, _) = Ebml.Read(_stream, keepMarker: false);
        _segmentStart = _stream.Position;
        _segmentEnd = segmentSize == Ebml.UnknownSize ? _stream.Length ?? long.MaxValue : _segmentStart + segmentSize;

        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var chapters = new List<Chapter>();
        byte[]? cover = null;
        double durationUnits = 0;
        string? title = null;
        var seen = new HashSet<uint>();
        var later = new List<(uint Id, long Position)>();
        _firstCluster = _segmentEnd;
        var position = _segmentStart;
        while (position < _segmentEnd && TryReadHeader(position, out var id, out var size, out var dataStart))
        {
            if (id == MatroskaId.Cluster)
            {
                _firstCluster = position;
                break;
            }

            if (size == Ebml.UnknownSize)
            {
                break;
            }

            Read(id, dataStart, size);
            position = dataStart + size;
        }

        foreach (var (id, offset) in later)
        {
            var at = _segmentStart + offset;
            if (!seen.Contains(id) && at < _segmentEnd && source.CanSeek && TryReadHeader(at, out var foundId, out var size, out var dataStart) && foundId == id && size != Ebml.UnknownSize)
            {
                Read(id, dataStart, size);
            }
        }

        if (_tracks.Count == 0)
        {
            throw new MediaFormatException("The Matroska file has no tracks.");
        }

        var duration = durationUnits > 0 ? new MediaTime((long)(durationUnits * _timestampScale / 100)) : MediaTime.Unknown;
        if (title is not null)
        {
            metadata.TryAdd(MetadataKeys.Title, title);
        }

        Info = new MediaInfo
        {
            FormatName = docType == "webm" ? "WebM" : "Matroska",
            Tracks = [.. _tracks.Values.Select(t => t.Describe(duration))],
            Duration = duration,
            IsSeekable = source.CanSeek,
            Metadata = metadata,
            Chapters = chapters,
            CoverArt = cover,
        };
        _position = _firstCluster;

        void Read(uint id, long dataStart, long size)
        {
            if (id is not (MatroskaId.Info or MatroskaId.Tracks or MatroskaId.Cues or MatroskaId.Chapters or MatroskaId.Tags or MatroskaId.Attachments or MatroskaId.SeekHead))
            {
                return;
            }

            if (size > MaxHeaderBytes)
            {
                throw new MediaFormatException("A Matroska header element is larger than rexplayer accepts.");
            }

            seen.Add(id);
            _stream.Seek(dataStart);
            var body = new byte[size];
            _stream.ReadExactly(body);
            switch (id)
            {
                case MatroskaId.Info:
                    _timestampScale = Math.Max(1, MatroskaMetadata.ReadUIntOr(body, MatroskaId.TimestampScale, 1_000_000));
                    durationUnits = Ebml.Find(body, MatroskaId.Duration) is { } d ? Ebml.Float(body.AsSpan(d.Start, d.End - d.Start)) : 0;
                    title = Ebml.Find(body, MatroskaId.Title) is { } t ? Ebml.Text(body.AsSpan(t.Start, t.End - t.Start)) : null;
                    break;
                case MatroskaId.Tracks:
                    foreach (var entry in Ebml.Children(body))
                    {
                        if (entry.Id == MatroskaId.TrackEntry)
                        {
                            var track = MatroskaTrack.Parse(body.AsSpan(entry.Start, entry.End - entry.Start));
                            _tracks.TryAdd(track.Number, track);
                        }
                    }

                    break;
                case MatroskaId.Cues:
                    _cues.AddRange(MatroskaMetadata.ReadCues(body));
                    break;
                case MatroskaId.Chapters:
                    chapters.AddRange(MatroskaMetadata.ReadChapters(body));
                    break;
                case MatroskaId.Tags:
                    MatroskaMetadata.ReadTags(body, metadata);
                    break;
                case MatroskaId.Attachments:
                    cover ??= MatroskaMetadata.ReadCover(body);
                    break;
                default:
                    later.AddRange(MatroskaMetadata.ReadSeekHead(body));
                    break;
            }
        }
    }

    public MediaInfo Info { get; }

    public Packet? ReadPacket(CancellationToken cancellationToken)
    {
        _stream.CancellationToken = cancellationToken;
        while (true)
        {
            if (_pending.TryDequeue(out var queued))
            {
                return queued;
            }

            if (!_inCluster)
            {
                if (_position >= _segmentEnd || !TryReadHeader(_position, out var id, out var size, out var dataStart))
                {
                    return null;
                }

                if (id == MatroskaId.Cluster)
                {
                    _inCluster = true;
                    _clusterEnd = size == Ebml.UnknownSize ? -1 : dataStart + size;
                    _clusterTime = 0;
                    _position = dataStart;
                    continue;
                }

                if (size == Ebml.UnknownSize)
                {
                    return null;
                }

                _position = dataStart + size;
                continue;
            }

            if ((_clusterEnd >= 0 && _position >= _clusterEnd) || !TryReadHeader(_position, out var childId, out var childSize, out var childStart))
            {
                _inCluster = false;
                if (_clusterEnd < 0)
                {
                    return null;
                }

                _position = _clusterEnd;
                continue;
            }

            if (_clusterEnd < 0 && MatroskaId.IsTopLevel(childId))
            {
                // An unknown-size cluster ends where the next top-level element begins.
                _inCluster = false;
                continue;
            }

            if (childSize == Ebml.UnknownSize || childSize > MaxBlockBytes)
            {
                throw new MediaFormatException("A Matroska block has an unusable size.");
            }

            var elementStart = _position;
            _position = childStart + childSize;
            if (childId != MatroskaId.Timestamp && elementStart < _resumeAt)
            {
                // A cue pointed into this cluster: blocks before the one it named are skipped.
                continue;
            }

            switch (childId)
            {
                case MatroskaId.Timestamp:
                    _clusterTime = (long)Math.Min(Ebml.UInt(ReadAt(childStart, childSize)), long.MaxValue);
                    break;
                case MatroskaId.SimpleBlock:
                    var simple = ReadAt(childStart, childSize);
                    QueueBlock(simple, keyframe: null, durationUnits: null, discardNs: 0);
                    break;
                case MatroskaId.BlockGroup:
                    var group = ReadAt(childStart, childSize);
                    if (Ebml.Find(group, MatroskaId.Block) is { } block)
                    {
                        var referenced = Ebml.Find(group, MatroskaId.ReferenceBlock) is not null;
                        long? blockDuration = Ebml.Find(group, MatroskaId.BlockDuration) is { } d ? (long)Ebml.UInt(group.AsSpan(d.Start, d.End - d.Start)) : null;
                        var discardNs = Ebml.Find(group, MatroskaId.DiscardPadding) is { } p ? Ebml.Int(group.AsSpan(p.Start, p.End - p.Start)) : 0;
                        QueueBlock(group[block.Start..block.End], !referenced, blockDuration, discardNs);
                    }

                    break;
            }
        }
    }

    public void Seek(MediaTime target, CancellationToken cancellationToken)
    {
        _stream.CancellationToken = cancellationToken;
        var video = _tracks.Values.FirstOrDefault(t => t.Type == 1);
        var main = video ?? _tracks.Values.First();

        // Audio whose decoder needs earlier frames to warm up starts a little before the target.
        var preroll = main.SeekPreRollNs > 0 ? main.SeekPreRollNs : main.CodecName switch
        {
            "A_MPEG/L3" => 300_000_000L,
            "A_AAC" or "A_VORBIS" or "A_AC3" or "A_EAC3" => 100_000_000L,
            _ => 0L,
        };
        var units = Math.Max(0, ((target.Ticks * 100) - preroll + main.CodecDelayNs) / _timestampScale);
        var position = _firstCluster;
        var relative = 0L;
        if (_cues.Count > 0)
        {
            var relevant = _cues.Where(c => c.Track == main.Number).ToList();
            foreach (var (time, _, offset, within) in relevant.Count > 0 ? relevant : _cues)
            {
                if (time > units)
                {
                    break;
                }

                (position, relative) = (_segmentStart + offset, within);
            }
        }
        else
        {
            foreach (var (offset, time) in ClusterIndex())
            {
                if (time > units)
                {
                    break;
                }

                position = offset;
            }
        }

        while (_pending.TryDequeue(out var stale))
        {
            stale.Dispose();
        }

        _position = position;
        _inCluster = false;
        _awaitKeyframe = video?.Number;
        _resumeAt = relative > 0 && TryReadHeader(position, out var id, out _, out var dataStart) && id == MatroskaId.Cluster ? dataStart + relative : 0;
    }

    public void Dispose()
    {
        while (_pending.TryDequeue(out var packet))
        {
            packet.Dispose();
        }
    }

    /// <summary>Every cluster's position and timestamp, found by walking cluster headers once (for files without cues).</summary>
    private List<(long Position, long Time)> ClusterIndex()
    {
        if (_clusterIndex is not null)
        {
            return _clusterIndex;
        }

        _clusterIndex = [];
        var position = _firstCluster;
        while (position < _segmentEnd && TryReadHeader(position, out var id, out var size, out var dataStart) && size != Ebml.UnknownSize)
        {
            if (id == MatroskaId.Cluster)
            {
                var time = 0L;
                if (TryReadHeader(dataStart, out var childId, out var childSize, out var childStart) && childId == MatroskaId.Timestamp && childSize <= 8)
                {
                    time = (long)Math.Min(Ebml.UInt(ReadAt(childStart, childSize)), long.MaxValue);
                }

                _clusterIndex.Add((position, time));
            }

            position = dataStart + size;
        }

        return _clusterIndex;
    }

    /// <summary>Splits a block into its frames and queues one packet per frame.</summary>
    private void QueueBlock(byte[] block, bool? keyframe, long? durationUnits, long discardNs)
    {
        var offset = 0;
        if (!Ebml.TryReadSize(block, ref offset, out var number) || offset + 3 > block.Length || !_tracks.TryGetValue((int)Math.Min(number, int.MaxValue), out var track) || track.Unreadable)
        {
            return;
        }

        var relative = MatroskaMetadata.Int16(block.AsSpan(offset));
        var flags = block[offset + 2];
        offset += 3;
        var isKey = keyframe ?? (flags & 0x80) != 0;
        if (_awaitKeyframe == track.Number)
        {
            if (!isKey)
            {
                return;
            }

            _awaitKeyframe = null;
        }

        var frames = Lace(block, offset, (flags >> 1) & 3);
        var nanoseconds = ((_clusterTime + relative) * _timestampScale) - track.CodecDelayNs;
        var frameNs = track.DefaultDurationNs > 0
            ? track.DefaultDurationNs
            : durationUnits is { } units ? units * _timestampScale / frames.Count : 0;
        // Padding the encoder added to the block's end, which the decoded audio loses.
        var discard = discardNs > 0 ? (int)Math.Min(Math.Round(discardNs * track.SampleRate / 1e9), int.MaxValue) : 0;
        for (var i = 0; i < frames.Count; i++)
        {
            var (start, length) = frames[i];
            var buffer = MediaBuffer.Rent(track.StrippedHeader.Length + length);
            track.StrippedHeader.CopyTo(buffer.Span);
            block.AsSpan(start, length).CopyTo(buffer.Span[track.StrippedHeader.Length..]);
            var pts = i == 0 || frameNs > 0 ? new MediaTime((nanoseconds + (i * frameNs)) / 100) : MediaTime.Unknown;
            var packet = Packet.Create(track.Number, buffer, pts, pts, new MediaTime(frameNs / 100), isKey);
            packet.DiscardSamples = i == frames.Count - 1 ? discard : 0;
            _pending.Enqueue(packet);
        }
    }

    /// <summary>The frames of a block as (start, length): one unlaced, or Xiph-, fixed- or EBML-laced.</summary>
    private static List<(int Start, int Length)> Lace(byte[] block, int offset, int lacing)
    {
        if (lacing == 0)
        {
            return [(offset, block.Length - offset)];
        }

        if (offset >= block.Length)
        {
            throw new MediaFormatException("A laced Matroska block has no frame count.");
        }

        var count = block[offset++] + 1;
        var sizes = new long[count];
        switch (lacing)
        {
            case 1:
                for (var i = 0; i < count - 1; i++)
                {
                    long size = 0;
                    byte b;
                    do
                    {
                        if (offset >= block.Length)
                        {
                            throw new MediaFormatException("A Xiph-laced Matroska block ends inside its sizes.");
                        }

                        b = block[offset++];
                        size += b;
                    }
                    while (b == 255);
                    sizes[i] = size;
                }

                break;
            case 3:
                if (!Ebml.TryReadSize(block, ref offset, out sizes[0]))
                {
                    throw new MediaFormatException("An EBML-laced Matroska block ends inside its sizes.");
                }

                for (var i = 1; i < count - 1; i++)
                {
                    var start = offset;
                    if (!Ebml.TryReadSize(block, ref offset, out var raw))
                    {
                        throw new MediaFormatException("An EBML-laced Matroska block ends inside its sizes.");
                    }

                    // Later sizes are differences from the one before, stored with a bias.
                    var length = offset - start;
                    sizes[i] = sizes[i - 1] + raw - ((1L << ((7 * length) - 1)) - 1);
                }

                break;
        }

        var remaining = (long)block.Length - offset;
        if (lacing == 2)
        {
            Array.Fill(sizes, remaining / count);
        }
        else
        {
            sizes[count - 1] = remaining - sizes.Take(count - 1).Sum();
        }

        var frames = new List<(int, int)>(count);
        foreach (var size in sizes)
        {
            if (size < 0 || size > block.Length - offset)
            {
                throw new MediaFormatException("A laced Matroska block's frame sizes do not fit the block.");
            }

            frames.Add((offset, (int)size));
            offset += (int)size;
        }

        return frames;
    }

    private bool TryReadHeader(long position, out uint id, out long size, out long dataStart)
    {
        id = 0;
        size = 0;
        dataStart = 0;
        _stream.Seek(position);
        Span<byte> bytes = stackalloc byte[12];
        var read = _stream.Read(bytes);
        var offset = 0;
        if (!Ebml.TryReadId(bytes[..read], ref offset, out id) || !Ebml.TryReadSize(bytes[..read], ref offset, out size))
        {
            return false;
        }

        dataStart = position + offset;
        return true;
    }

    private byte[] ReadAt(long start, long size)
    {
        _stream.Seek(start);
        var body = new byte[size];
        if (_stream.Read(body) < size)
        {
            throw new MediaFormatException("A Matroska element runs past the end of the file.");
        }

        return body;
    }

    private byte[] ReadBody()
    {
        var (size, _) = Ebml.Read(_stream, keepMarker: false);
        if (size is < 0 or > 4096)
        {
            throw new MediaFormatException("The EBML header has an impossible size.");
        }

        var body = new byte[size];
        _stream.ReadExactly(body);
        return body;
    }
}
