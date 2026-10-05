// Spec: ISO/IEC 11172-3 and 13818-3 clause 2.4 (MPEG audio as a sequence of self-delimiting frames, free format); Xing/Info, LAME and VBRI tags; ID3v2 in front, ID3v1 and APEv2 (APE tag specification 2.0 footer) behind.
using System.Buffers.Binary;
using Rex.Media.Codecs.Mpeg;
using Rex.Media.Containers.Tags;
using Rex.Media.IO;
using Rex.Media.Primitives;

namespace Rex.Media.Containers.Mpeg;

/// <summary>Recognises MPEG audio elementary streams (MP3, MP2, MP1) by a chain of valid frame headers.</summary>
public sealed class MpegAudioDemuxerFactory : IDemuxerFactory
{
    /// <summary>Consecutive headers that make a chain convincing: random bytes rarely manage three.</summary>
    private const int ChainLength = 3;

    public string Name => "mpeg-audio";

    public int Probe(ReadOnlySpan<byte> head, string? extension)
    {
        var limit = Math.Min(head.Length - MpegAudioHeader.Size, 64 * 1024);
        for (var offset = 0; offset <= limit; offset++)
        {
            if (head[offset] == 0xFF && ChainAt(head, offset, ChainLength))
            {
                return offset == 0 ? 100 : 70;
            }
        }

        return 0;
    }

    public IDemuxer Open(IByteSource source, CancellationToken cancellationToken) => new MpegAudioDemuxer(source, cancellationToken);

    /// <summary>Whether <paramref name="count"/> headers of one stream follow each other from <paramref name="offset"/>, or a shorter chain ends with <paramref name="data"/>.</summary>
    internal static bool ChainAt(ReadOnlySpan<byte> data, int offset, int count)
    {
        if (!MpegAudioHeader.TryParse(data[offset..], out var first) || first.FrameLength == 0)
        {
            return false;
        }

        var position = offset;
        var header = first;
        for (var i = 1; i < count; i++)
        {
            position += header.FrameLength;
            if (position + MpegAudioHeader.Size > data.Length)
            {
                // The data ran out: two headers in a row are enough, and so is a lone frame ending
                // exactly at the end. One frame running off the end proves nothing.
                return i > 1 || position == data.Length;
            }

            if (!MpegAudioHeader.TryParse(data[position..], out header) || !header.SameStreamAs(first) || header.FrameLength == 0)
            {
                return false;
            }
        }

        return true;
    }
}

/// <summary>
/// An MP3 (or MP2, MP1) file: frames one after another, each with its own header, between optional
/// tags. A first frame holding a Xing/Info or VBRI tag gives the frame count and, from LAME, the
/// encoder delay and padding, which become negative timestamps at the start and a shorter declared
/// duration at the end, so playback is gapless. Seeking is sample-accurate: frame positions are
/// indexed by walking headers, and a seek starts a few frames early so the decoder's bit reservoir
/// and filterbank are full by the target.
/// </summary>
public sealed class MpegAudioDemuxer : IDemuxer
{
    /// <summary>Frames decoded before a seek target: enough for a full bit reservoir at low bitrates and the filterbank overlap.</summary>
    public const int PrerollFrames = 10;

    private const int TrackId = 0;
    private const int MaxTagBytes = 16 << 20;
    private const int SearchLimit = 1 << 20;

    private readonly ByteCursor _stream;
    private readonly ScanWindow _window;
    private readonly MpegAudioHeader _first;
    private readonly long _dataStart;
    private readonly long _dataEnd;
    private readonly int _leading;
    private readonly int _freeFormatLength;
    private readonly List<long> _frames = [];
    private long _nextFrame;

    public MpegAudioDemuxer(IByteSource source, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        _stream = new ByteCursor(source, cancellationToken);
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var pictures = new List<PictureBlock>();
        var chapters = new List<Chapter>();
        var start = Id3v2.LeadingTagsLength(source, cancellationToken);
        if (start is > 0 and <= MaxTagBytes)
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
                chapters.AddRange(id3.Chapters);
            }
        }

        _dataEnd = TrailingTagsStart(metadata);
        _window = new ScanWindow(_stream, start);
        var (firstAt, first) = FindFirstFrame(start) ?? throw new MediaFormatException("The file has no MPEG audio frames.");
        _first = first;
        _window.Reset(firstAt);
        _freeFormatLength = _first.BitrateKbps == 0 ? FreeFormatLength(firstAt, _first) : 0;

        // A first frame carrying an encoder tag is silence written to hold the tag, not audio.
        var firstLength = FrameLength(_first);
        _window.Ensure(firstLength);
        var tagFrame = MpegInfoTag.Parse(_window.Span[..Math.Min(firstLength, _window.Available)], _first);
        _dataStart = tagFrame is null ? firstAt : firstAt + firstLength;
        _window.Reset(_dataStart);
        _frames.Add(_dataStart);

        var spf = _first.SamplesPerFrame;
        _leading = tagFrame?.LeadingSamples ?? 0;
        var trailing = tagFrame?.TrailingSamples ?? 0;
        var duration = MediaTime.Unknown;
        long? bitRate = _first.BitrateKbps > 0 ? _first.BitrateKbps * 1000L : null;
        if (tagFrame?.Frames is { } frames)
        {
            var samples = Math.Max(0, (frames * spf) - _leading - trailing);
            duration = MediaTime.FromSamples(samples, _first.SampleRate);
            // "Info" marks a constant-bitrate stream, whose header already states the bitrate.
            if (frames > 0 && tagFrame.Kind != "Info" && (tagFrame.Bytes ?? (_dataEnd - _dataStart)) is var bytes and > 0)
            {
                bitRate = bytes * 8 * _first.SampleRate / (frames * spf);
            }
        }
        else if (bitRate is { } constant && _dataEnd != long.MaxValue && _dataEnd > _dataStart)
        {
            // No tag: assume a constant bitrate, the norm for untagged files.
            duration = MediaTime.FromSeconds((_dataEnd - _dataStart) * 8.0 / constant);
        }

        var track = new TrackInfo
        {
            Id = TrackId,
            Codec = _first.Codec,
            Duration = duration,
            BitRate = bitRate,
            IsDefault = true,
            Audio = new AudioTrackInfo
            {
                SampleRate = _first.SampleRate,
                Channels = _first.Channels,
                Layout = ChannelLayouts.Default(_first.Channels),
                LeadingPadding = _leading,
                TrailingPadding = trailing,
            },
        };
        Info = new MediaInfo
        {
            FormatName = _first.Codec switch
            {
                CodecId.Mp3 => "MP3",
                CodecId.Mp2 => "MPEG audio Layer II",
                _ => "MPEG audio Layer I",
            },
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
        while (_window.Position + MpegAudioHeader.Size <= _dataEnd)
        {
            if (!_window.Ensure(MpegAudioHeader.Size))
            {
                return null;
            }

            if (!MpegAudioHeader.TryParse(_window.Span, out var header) || !header.SameStreamAs(_first))
            {
                Resync();
                continue;
            }

            var length = FrameLength(header);
            if (_window.Position + length > _dataEnd || !_window.Ensure(length))
            {
                // A frame cut off by the end of the file: there is nothing whole left to play.
                return null;
            }

            // The index always holds this frame's start: reading a frame records where the next one begins.
            var index = _nextFrame++;
            var buffer = MediaBuffer.CopyOf(_window.Span[..length]);
            _window.Consume(length);
            if (_nextFrame == _frames.Count)
            {
                _frames.Add(_window.Position);
            }

            var spf = header.SamplesPerFrame;
            var pts = MediaTime.FromSamples((index * spf) - _leading, header.SampleRate);
            return Packet.Create(TrackId, buffer, pts, pts, MediaTime.FromSamples(spf, header.SampleRate), isKeyframe: true);
        }

        return null;
    }

    public void Seek(MediaTime target, CancellationToken cancellationToken)
    {
        _stream.CancellationToken = cancellationToken;
        var spf = _first.SamplesPerFrame;
        var frame = Math.Max(0, (target.ToSamples(_first.SampleRate) + _leading) / spf);
        // Every index entry but the last was read as a header; the last may be the end of the data.
        IndexUpTo(frame + 1);
        frame = Math.Min(frame, Math.Max(0, _frames.Count - 2));
        var startFrame = Math.Max(0, frame - PrerollFrames);
        _nextFrame = startFrame;
        _window.Reset(_frames[(int)startFrame]);
    }

    public void Dispose()
    {
    }

    /// <summary>Extends the frame index by walking headers, up to <paramref name="frame"/> or the end.</summary>
    private void IndexUpTo(long frame)
    {
        Span<byte> bytes = stackalloc byte[MpegAudioHeader.Size];
        while (_frames.Count - 1 < frame)
        {
            var position = _frames[^1];
            _stream.Seek(position);
            if (position + MpegAudioHeader.Size > _dataEnd || _stream.Read(bytes) < bytes.Length
                || !MpegAudioHeader.TryParse(bytes, out var header) || !header.SameStreamAs(_first))
            {
                return;
            }

            _frames.Add(position + FrameLength(header));
        }
    }

    private int FrameLength(MpegAudioHeader header) =>
        header.BitrateKbps > 0 ? header.FrameLength : _freeFormatLength + (header.Padding ? (header.Layer == 1 ? 4 : 1) : 0);

    /// <summary>Skips to the next place a frame of this stream starts, or to the end.</summary>
    private void Resync()
    {
        while (_window.Position < _dataEnd)
        {
            _window.Ensure(4096);
            var span = _window.Span;
            var next = span.Length > 1 ? span[1..].IndexOf((byte)0xFF) : -1;
            if (next < 0)
            {
                _window.Consume(span.Length);
                if (_window.AtEnd)
                {
                    return;
                }

                continue;
            }

            _window.Consume(next + 1);
            if (MpegAudioHeader.TryParse(_window.Span, out var header) && header.SameStreamAs(_first))
            {
                return;
            }
        }
    }

    private (long Offset, MpegAudioHeader Header)? FindFirstFrame(long start)
    {
        var buffer = new byte[(int)Math.Min(SearchLimit, Math.Max(0, _dataEnd - start))];
        _stream.Seek(start);
        var span = buffer.AsSpan(0, _stream.Read(buffer));
        for (var offset = 0; offset + MpegAudioHeader.Size <= span.Length; offset++)
        {
            if (span[offset] == 0xFF && MpegAudioHeader.TryParse(span[offset..], out var header)
                && (header.FrameLength == 0 ? FreeFormatAt(span, offset, header) > 0 : MpegAudioDemuxerFactory.ChainAt(span, offset, 3)))
            {
                return (start + offset, header);
            }
        }

        return null;
    }

    /// <summary>A free-format stream's frame length without padding: the distance to the next matching header.</summary>
    private int FreeFormatLength(long firstAt, MpegAudioHeader header)
    {
        var buffer = new byte[(int)Math.Min(16 * 1024, _dataEnd - firstAt)];
        _stream.Seek(firstAt);
        var span = buffer.AsSpan(0, _stream.Read(buffer));
        return FreeFormatAt(span, 0, header);
    }

    private static int FreeFormatAt(ReadOnlySpan<byte> span, int offset, MpegAudioHeader first)
    {
        for (var next = offset + MpegAudioHeader.Size; next + MpegAudioHeader.Size <= span.Length; next++)
        {
            if (span[next] == 0xFF && MpegAudioHeader.TryParse(span[next..], out var header) && header.SameStreamAs(first) && header.BitrateKbps == 0)
            {
                return next - offset - (first.Padding ? (first.Layer == 1 ? 4 : 1) : 0);
            }
        }

        return 0;
    }

    /// <summary>Where the audio ends: before an ID3v1 tag (whose fields are kept) and an APEv2 tag, when present.</summary>
    private long TrailingTagsStart(Dictionary<string, string> metadata)
    {
        if (_stream.Length is not { } end)
        {
            return long.MaxValue;
        }

        Span<byte> tail = stackalloc byte[Id3v1.Size];
        if (end >= Id3v1.Size)
        {
            _stream.Seek(end - Id3v1.Size);
            _stream.ReadExactly(tail);
            if (Id3v1.IsTag(tail))
            {
                Id3v1.Read(tail, metadata);
                end -= Id3v1.Size;
            }
        }

        Span<byte> footer = stackalloc byte[32];
        if (end >= 32)
        {
            _stream.Seek(end - 32);
            _stream.ReadExactly(footer);
            if (footer.StartsWith("APETAGEX"u8))
            {
                var size = BinaryPrimitives.ReadUInt32LittleEndian(footer[12..]);
                var hasHeader = (BinaryPrimitives.ReadUInt32LittleEndian(footer[20..]) & 0x80000000) != 0;
                end = Math.Max(0, end - size - (hasHeader ? 32 : 0));
            }
        }

        return end;
    }
}
