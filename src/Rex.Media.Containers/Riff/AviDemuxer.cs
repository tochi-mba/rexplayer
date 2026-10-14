// Spec: Microsoft Multimedia Programming Interface and Data Specifications 1.0 (AVI RIFF File Reference); OpenDML AVI File Format Extensions 1.02.
using System.Buffers.Binary;
using Rex.Media.IO;
using Rex.Media.Primitives;

namespace Rex.Media.Containers.Riff;

public sealed class AviDemuxerFactory : IDemuxerFactory
{
    public string Name => "avi";
    public int Probe(ReadOnlySpan<byte> head, string? extension) => head.Length >= 12 && FourCC.Matches(head, "RIFF") && FourCC.Matches(head[8..], "AVI ") ? 100 : 0;
    public IDemuxer Open(IByteSource source, CancellationToken cancellationToken) => new AviDemuxer(source, cancellationToken);
}

/// <summary>RIFF AVI streams, including additional OpenDML AVIX segments.</summary>
public sealed class AviDemuxer : IDemuxer
{
    private readonly IByteSource _source;
    private readonly ByteCursor _stream;
    private readonly List<StreamHeader> _headers = [];
    private readonly List<(long Start, long End)> _movie = [];
    private readonly List<Chunk> _chunks = [];
    private long[] _units = [];
    private int _next;

    public AviDemuxer(IByteSource source, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        _source = source;
        _stream = new ByteCursor(source, cancellationToken);
        Parse(cancellationToken);
        if (_headers.Count == 0 || _movie.Count == 0) throw new MediaFormatException("The AVI file has no playable streams or movie data.");
        if (_chunks.Count == 0) ScanMovie(cancellationToken);
        _chunks.Sort((left, right) => left.Offset.CompareTo(right.Offset));
        StampUnits();
        _units = new long[_headers.Count];
        var tracks = _headers.Select((header, id) => header.Track(id)).ToList();
        var known = tracks.Select(track => track.Duration).Where(value => value.IsKnown).ToList();
        Info = new MediaInfo { FormatName = "AVI", Tracks = tracks, Duration = known.Count == 0 ? MediaTime.Unknown : known.Max(), IsSeekable = _chunks.Any(chunk => chunk.Keyframe) };
    }

    public MediaInfo Info { get; }

    public Packet? ReadPacket(CancellationToken cancellationToken)
    {
        _stream.CancellationToken = cancellationToken;
        while (_next < _chunks.Count)
        {
            var chunk = _chunks[_next++];
            if ((uint)chunk.Stream >= (uint)_headers.Count || chunk.Size > int.MaxValue) continue;
            var header = _headers[chunk.Stream];
            var units = _units[chunk.Stream];
            var advance = header.SampleSize > 0 ? Math.Max(1, chunk.Size / header.SampleSize) : 1;
            var pts = header.Time(units);
            _units[chunk.Stream] = checked(units + advance);
            _stream.Seek(chunk.Offset);
            return Packet.Create(chunk.Stream, _stream.ReadBuffer((int)chunk.Size), pts, pts, header.Time(advance), chunk.Keyframe);
        }

        return null;
    }

    public void Seek(MediaTime target, CancellationToken cancellationToken)
    {
        _stream.CancellationToken = cancellationToken;
        var video = _headers.FindIndex(header => header.Kind == "vids");
        var at = 0;
        if (video >= 0)
        {
            var wanted = _headers[video].Units(target);
            for (var i = 0; i < _chunks.Count; i++)
            {
                if (_chunks[i] is { Stream: var stream, Keyframe: true, Unit: var unit } && stream == video && unit <= wanted) at = i;
            }
        }

        _next = at;
        Array.Clear(_units);
        for (var i = 0; i < at; i++)
        {
            var chunk = _chunks[i];
            if ((uint)chunk.Stream >= (uint)_headers.Count) continue;
            var header = _headers[chunk.Stream];
            _units[chunk.Stream] += header.SampleSize > 0 ? Math.Max(1, chunk.Size / header.SampleSize) : 1;
        }
    }

    public void Dispose() => _source.Dispose();

    private void Parse(CancellationToken cancellationToken)
    {
        var length = _source.Length ?? throw new MediaFormatException("AVI needs a source whose length is known.");
        long position = 0;
        while (position + 12 <= length)
        {
            _stream.Seek(position);
            if (_stream.ReadFourCC() != "RIFF") throw new MediaFormatException("The AVI file contains bytes outside a RIFF segment.");
            var size = _stream.ReadUInt32LittleEndian();
            var type = _stream.ReadFourCC();
            if (type is not ("AVI " or "AVIX")) throw new MediaFormatException("The RIFF segment is not AVI or AVIX movie data.");
            var end = Math.Min(length, checked(position + 8L + size));
            ParseRange(_stream.Position, end, type == "AVI ", cancellationToken);
            position = Align2(end);
        }
    }

    private void ParseRange(long start, long end, bool headersAllowed, CancellationToken cancellationToken)
    {
        for (var position = start; position + 8 <= end;)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _stream.Seek(position);
            var id = _stream.ReadFourCC();
            var size = _stream.ReadUInt32LittleEndian();
            var data = _stream.Position;
            var next = Math.Min(end, Align2(checked(data + size)));
            if (id == "LIST" && size >= 4)
            {
                var type = _stream.ReadFourCC();
                if (type == "hdrl" && headersAllowed) ParseHeaderList(_stream.Position, data + size, cancellationToken);
                else if (type == "movi") _movie.Add((_stream.Position, data + size));
            }
            else if (id == "idx1") ParseLegacyIndex(data, size);
            if (next <= position) throw new MediaFormatException("An AVI chunk has an invalid size.");
            position = next;
        }
    }

    private void ParseHeaderList(long start, long end, CancellationToken cancellationToken)
    {
        for (var position = start; position + 8 <= end;)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _stream.Seek(position);
            var id = _stream.ReadFourCC();
            var size = _stream.ReadUInt32LittleEndian();
            var data = _stream.Position;
            if (id == "LIST" && size >= 4 && _stream.ReadFourCC() == "strl") ParseStreamList(_stream.Position, data + size);
            position = Math.Min(end, Align2(checked(data + size)));
        }
    }

    private void ParseStreamList(long start, long end)
    {
        byte[]? streamHeader = null;
        byte[]? streamFormat = null;
        for (var position = start; position + 8 <= end;)
        {
            _stream.Seek(position);
            var id = _stream.ReadFourCC();
            var size = _stream.ReadUInt32LittleEndian();
            var data = _stream.Position;
            if (size <= 1024 * 1024 && id is "strh" or "strf")
            {
                var bytes = new byte[(int)size];
                _stream.ReadExactly(bytes);
                if (id == "strh") streamHeader = bytes; else streamFormat = bytes;
            }
            position = Math.Min(end, Align2(checked(data + size)));
        }
        if (streamHeader is not null && streamFormat is not null) _headers.Add(StreamHeader.Parse(streamHeader, streamFormat));
    }

    private void ParseLegacyIndex(long start, uint size)
    {
        if (_movie.Count == 0) return;
        var baseOffset = _movie[0].Start - 4;
        _stream.Seek(start);
        Span<byte> entry = stackalloc byte[16];
        for (long read = 0; read + 16 <= size; read += 16)
        {
            _stream.ReadExactly(entry);
            var stream = StreamNumber(entry);
            var offset = BinaryPrimitives.ReadUInt32LittleEndian(entry[8..]);
            var length = BinaryPrimitives.ReadUInt32LittleEndian(entry[12..]);
            if (stream < 0) continue;
            var header = baseOffset + offset;
            if (!ChunkHeaderMatches(header, entry[..4])) header += 4;
            if (ChunkHeaderMatches(header, entry[..4])) _chunks.Add(new Chunk(header + 8, length, stream, (BinaryPrimitives.ReadUInt32LittleEndian(entry[4..]) & 0x10) != 0, 0));
        }
    }

    private void ScanMovie(CancellationToken cancellationToken)
    {
        foreach (var range in _movie) ScanChunks(range.Start, range.End, cancellationToken);
    }

    private void ScanChunks(long start, long end, CancellationToken cancellationToken)
    {
        Span<byte> id = stackalloc byte[4];
        for (var position = start; position + 8 <= end;)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _stream.Seek(position);
            _stream.ReadExactly(id);
            var size = _stream.ReadUInt32LittleEndian();
            var data = _stream.Position;
            if (FourCC.Matches(id, "LIST") && size >= 4)
            {
                _stream.Skip(4);
                ScanChunks(_stream.Position, Math.Min(end, data + size), cancellationToken);
            }
            else if (StreamNumber(id) is var stream && stream >= 0)
            {
                _chunks.Add(new Chunk(data, size, stream, id[2] == (byte)'d' && id[3] == (byte)'b', 0));
            }
            position = Math.Min(end, Align2(checked(data + size)));
        }
    }

    private void StampUnits()
    {
        var units = new long[_headers.Count];
        for (var i = 0; i < _chunks.Count; i++)
        {
            var chunk = _chunks[i];
            if ((uint)chunk.Stream >= (uint)_headers.Count) continue;
            _chunks[i] = chunk with { Unit = units[chunk.Stream] };
            var header = _headers[chunk.Stream];
            units[chunk.Stream] += header.SampleSize > 0 ? Math.Max(1, chunk.Size / header.SampleSize) : 1;
        }
    }

    private bool ChunkHeaderMatches(long position, ReadOnlySpan<byte> id)
    {
        if (position < 0 || _source.Length is not { } length || position + 8 > length) return false;
        _stream.Seek(position);
        Span<byte> actual = stackalloc byte[4];
        return _stream.Read(actual) == 4 && actual.SequenceEqual(id);
    }

    private static int StreamNumber(ReadOnlySpan<byte> id)
    {
        if (id.Length < 4) return -1;
        static int Digit(byte value) => value is >= (byte)'0' and <= (byte)'9' ? value - '0' : value is >= (byte)'A' and <= (byte)'F' ? value - 'A' + 10 : value is >= (byte)'a' and <= (byte)'f' ? value - 'a' + 10 : -1;
        var high = Digit(id[0]);
        var low = Digit(id[1]);
        return high < 0 || low < 0 ? -1 : (high * 16) + low;
    }

    private static long Align2(long value) => checked(value + (value & 1));

    private sealed record StreamHeader(string Kind, uint Scale, uint Rate, uint Length, uint SampleSize, byte[] Format)
    {
        public static StreamHeader Parse(ReadOnlySpan<byte> header, byte[] format)
        {
            if (header.Length < 48) throw new MediaFormatException("An AVI stream header is too short.");
            var scale = BinaryPrimitives.ReadUInt32LittleEndian(header[20..]);
            var rate = BinaryPrimitives.ReadUInt32LittleEndian(header[24..]);
            if (scale == 0 || rate == 0) throw new MediaFormatException("An AVI stream has an invalid time base.");
            return new(FourCC.ToString(header), scale, rate, BinaryPrimitives.ReadUInt32LittleEndian(header[32..]), BinaryPrimitives.ReadUInt32LittleEndian(header[44..]), format);
        }

        public MediaTime Time(long units) => MediaTime.FromTimebase(units, new Rational(Scale, Rate));
        public long Units(MediaTime time) => Rational.Rescale(time.Ticks, MediaTime.Timebase, new Rational(Scale, Rate));

        public TrackInfo Track(int id)
        {
            var duration = Time(Length);
            if (Kind == "auds") return WaveFormat.Parse(Format, false).ToTrack(id, duration);
            if (Kind != "vids" || Format.Length < 40) return new TrackInfo { Id = id, Codec = CodecId.Unknown, Duration = duration };
            var width = Math.Abs(BinaryPrimitives.ReadInt32LittleEndian(Format.AsSpan(4)));
            var height = Math.Abs(BinaryPrimitives.ReadInt32LittleEndian(Format.AsSpan(8)));
            var compression = FourCC.ToString(Format.AsSpan(16)).ToUpperInvariant();
            var codec = compression switch { "XVID" or "DIVX" or "DX50" or "MP4V" or "M4S2" => CodecId.Mpeg4Part2, "MJPG" => CodecId.Mjpeg, "H264" or "X264" or "AVC1" => CodecId.H264, _ => CodecId.Unknown };
            return new TrackInfo { Id = id, Codec = codec, CodecPrivate = Format.Length > 40 ? Format[40..] : [], Duration = duration, Video = new VideoTrackInfo { Width = width, Height = height, FrameRate = new Rational(Rate, Scale) } };
        }
    }

    private readonly record struct Chunk(long Offset, uint Size, int Stream, bool Keyframe, long Unit);
}
