// Spec: ISO/IEC 14496-12 clauses 4.3 (ftyp), 8.1.1 (mdat), 8.2 (moov, mvhd), 8.8 (mvex, trex, moof, mfhd, traf, tfhd, tfdt, trun and sample flags), 8.10 (udta, meta); Apple QuickTime File Format (files without ftyp, chapter text tracks).
using System.Text;
using Rex.Media.IO;
using Rex.Media.Primitives;

namespace Rex.Media.Containers.Mp4;

/// <summary>Recognises MP4, M4A, MOV, 3GP and other ISO base media files.</summary>
public sealed class Mp4DemuxerFactory : IDemuxerFactory
{
    public string Name => "mp4";

    public int Probe(ReadOnlySpan<byte> head, string? extension)
    {
        if (head.Length < 8)
        {
            return 0;
        }

        var type = FourCC.ToString(head.Slice(4, 4));
        return type switch
        {
            "ftyp" => 100,
            "moov" or "mdat" or "free" or "skip" or "wide" or "pnot" => 60,
            _ => 0,
        };
    }

    public IDemuxer Open(IByteSource source, CancellationToken cancellationToken) => new Mp4Demuxer(source, cancellationToken);
}

/// <summary>
/// ISO base media files (MP4, M4A, MOV, 3GP), progressive or fragmented. Every track's samples
/// are indexed when the file opens; packets then come out in file order across tracks, which is
/// how a well-interleaved file is laid out, and a seek finds the sync sample at or before the
/// target in the main track and lines the others up with it.
/// </summary>
public sealed class Mp4Demuxer : IDemuxer
{
    private const long MaxMoovBytes = 256L << 20;
    private const int MaxSampleBytes = 64 << 20;
    private const int PcmFramesPerPacket = 4096;

    private readonly ByteCursor _stream;
    private readonly List<Mp4Track> _tracks = [];
    private readonly int[] _next;

    public Mp4Demuxer(IByteSource source, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        _stream = new ByteCursor(source, cancellationToken);
        byte[]? moov = null;
        var fragments = new List<(long Start, byte[] Body)>();
        var majorBrand = string.Empty;
        long position = 0;
        Span<byte> header = stackalloc byte[16];
        while (true)
        {
            _stream.Seek(position);
            if (_stream.Read(header[..8]) < 8)
            {
                break;
            }

            long size = Mp4Boxes.U32(header, 0);
            var type = FourCC.ToString(header.Slice(4, 4));
            var headerSize = 8;
            if (size == 1)
            {
                if (_stream.Read(header.Slice(8, 8)) < 8)
                {
                    break;
                }

                size = (long)Math.Min(Mp4Boxes.U64(header, 8), long.MaxValue);
                headerSize = 16;
            }
            else if (size == 0)
            {
                size = (_stream.Length ?? long.MaxValue) - position;
            }

            if (size < headerSize)
            {
                break;
            }

            switch (type)
            {
                case "ftyp" when size >= headerSize + 4:
                    majorBrand = _stream.ReadFourCC();
                    break;
                case "moov":
                    moov = ReadBody(position + headerSize, size - headerSize);
                    break;
                case "moof":
                    fragments.Add((position, ReadBody(position + headerSize, size - headerSize)));
                    break;
            }

            if (size > long.MaxValue - position)
            {
                break;
            }

            position += size;
        }

        if (moov is null)
        {
            throw new MediaFormatException("The MP4 file has no movie header (moov), so its tracks cannot be found.");
        }

        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var chapters = new List<Chapter>();
        byte[]? cover = null;
        var span = moov.AsSpan();
        uint movieTimescale = 1000;
        long movieDuration = 0;
        if (Mp4Boxes.Find(span, "mvhd") is { } mvhd)
        {
            var body = Mp4Boxes.Body(span, mvhd);
            var version = body[0];
            movieTimescale = Math.Max(1, Mp4Boxes.U32(body, version == 1 ? 20 : 12));
            movieDuration = version == 1 ? (long)Math.Min(Mp4Boxes.U64(body, 24), long.MaxValue) : Mp4Boxes.U32(body, 16);
        }

        var defaults = new Dictionary<int, (uint Duration, uint Size, uint Flags)>();
        foreach (var box in Mp4Boxes.Children(span))
        {
            switch (box.Type)
            {
                case "trak" when Mp4Track.Parse(span, box, movieTimescale) is { } track:
                    _tracks.Add(track);
                    break;
                case "mvex":
                    foreach (var trex in Mp4Boxes.Children(span[..box.End], box.Start))
                    {
                        if (trex.Type == "trex" && trex.Length >= 24)
                        {
                            var body = Mp4Boxes.Body(span, trex);
                            defaults[(int)Mp4Boxes.U32(body, 4)] = (Mp4Boxes.U32(body, 12), Mp4Boxes.U32(body, 16), Mp4Boxes.U32(body, 20));
                        }
                    }

                    break;
                case "udta" or "meta":
                    cover = ReadUserData(span, box, metadata, chapters) ?? cover;
                    break;
            }
        }

        foreach (var (start, body) in fragments)
        {
            ReadFragment(start, body, defaults);
        }

        foreach (var chapterTrack in _tracks.Where(t => _tracks.Any(other => other.ChapterTrackId == t.Id)).ToList())
        {
            if (chapters.Count == 0)
            {
                chapters.AddRange(ReadChapterTrack(chapterTrack));
            }

            _tracks.Remove(chapterTrack);
        }

        ApplyGaplessTag(metadata);
        _next = new int[_tracks.Count];
        var tracks = _tracks.Select(Describe).ToList();
        var duration = movieDuration > 0 ? MediaTime.FromSamples(movieDuration, (int)Math.Min(movieTimescale, int.MaxValue)) : MediaTime.Unknown;
        if (tracks.Count > 0 && tracks.All(t => t.Duration.IsKnown) && (!duration.IsKnown || fragments.Count > 0 || _tracks.Any(t => t.EditDuration is not null)))
        {
            duration = tracks.Max(t => t.Duration);
        }

        Info = new MediaInfo
        {
            FormatName = majorBrand.StartsWith("qt", StringComparison.Ordinal) ? "QuickTime" : "MP4",
            Tracks = tracks,
            Duration = duration,
            IsSeekable = source.CanSeek,
            Metadata = metadata,
            Chapters = chapters,
            CoverArt = cover,
        };
    }

    public MediaInfo Info { get; }

    public Packet? ReadPacket(CancellationToken cancellationToken)
    {
        _stream.CancellationToken = cancellationToken;
        var best = -1;
        for (var t = 0; t < _tracks.Count; t++)
        {
            if (_next[t] < _tracks[t].SampleCount && (best < 0 || _tracks[t].Offsets[_next[t]] < _tracks[best].Offsets[_next[best]]))
            {
                best = t;
            }
        }

        if (best < 0)
        {
            return null;
        }

        var track = _tracks[best];
        var first = _next[best];
        var last = first;
        long bytes = track.Sizes[first];
        if (track.Codec == CodecId.Pcm)
        {
            // PCM files store one sample per audio frame; contiguous ones travel together.
            while (last + 1 < track.SampleCount && last + 1 - first < PcmFramesPerPacket
                && track.Offsets[last + 1] == track.Offsets[last] + track.Sizes[last])
            {
                last++;
                bytes += track.Sizes[last];
            }
        }

        _next[best] = last + 1;
        if (bytes > MaxSampleBytes)
        {
            throw new MediaFormatException("An MP4 sample is larger than rexplayer accepts.");
        }

        _stream.Seek(track.Offsets[first]);
        var buffer = MediaBuffer.Rent((int)bytes);
        var read = _stream.Read(buffer.Span);
        if (read < bytes)
        {
            buffer.Dispose();
            throw new MediaFormatException("An MP4 sample lies beyond the end of the file.");
        }

        var end = last + 1 < track.SampleCount ? track.Dts[last + 1] : Math.Max(track.NextDts, track.Dts[last]);
        var pts = Time(track, track.Presentation(first));
        var dts = Time(track, track.Dts[first] - track.EditOffset);
        var packet = Packet.Create(track.Id, buffer, pts, dts, Time(track, end - track.Dts[first]), track.IsSync(first));
        return packet;
    }

    public void Seek(MediaTime target, CancellationToken cancellationToken)
    {
        var main = _tracks.FindIndex(t => t.Handler == "vide" && t.SampleCount > 0);
        if (main < 0)
        {
            main = _tracks.FindIndex(t => t.SampleCount > 0);
        }

        if (main < 0)
        {
            return;
        }

        var key = Locate(_tracks[main], target);
        var keyTime = Time(_tracks[main], _tracks[main].Dts[key] - _tracks[main].EditOffset);
        for (var t = 0; t < _tracks.Count; t++)
        {
            _next[t] = t == main ? key : _tracks[t].SampleCount == 0 ? 0 : Locate(_tracks[t], MediaTime.Min(keyTime, target));
        }
    }

    public void Dispose()
    {
    }

    private static MediaTime Time(Mp4Track track, long units) => MediaTime.FromSamples(units, (int)Math.Min(track.Timescale, int.MaxValue));

    /// <summary>The sync sample at or before <paramref name="target"/>, moved back by the codec's preroll.</summary>
    private static int Locate(Mp4Track track, MediaTime target)
    {
        var units = target.ToSamples((int)Math.Min(track.Timescale, int.MaxValue)) + track.EditOffset;
        int low = 0, high = track.SampleCount - 1, found = 0;
        while (low <= high)
        {
            var middle = (low + high) / 2;
            if (track.Dts[middle] <= units)
            {
                found = middle;
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        // With reordered frames a sync sample decoded before the target can still be shown after it,
        // so the search steps back to the last sync sample presented at or before the target.
        var presented = target.ToSamples((int)Math.Min(track.Timescale, int.MaxValue));
        while (found > 0 && (!track.IsSync(found) || track.Presentation(found) > presented))
        {
            found--;
        }

        // Audio codecs whose frames lean on the ones before (bit reservoir, overlapping windows)
        // need a few earlier frames decoded for the target to sound right; the engine trims them.
        var preroll = track.Codec switch
        {
            CodecId.Mp3 => 10,
            CodecId.Aac or CodecId.Vorbis or CodecId.Opus or CodecId.Ac3 or CodecId.Eac3 => 2,
            _ => 0,
        };
        return Math.Max(0, found - preroll);
    }

    private byte[] ReadBody(long start, long length)
    {
        if (length > MaxMoovBytes)
        {
            throw new MediaFormatException("An MP4 header box is larger than rexplayer accepts.");
        }

        var body = new byte[length];
        _stream.Seek(start);
        var read = _stream.Read(body);
        return read == body.Length ? body : body[..read];
    }

    private static byte[]? ReadUserData(ReadOnlySpan<byte> moov, Mp4Box box, Dictionary<string, string> metadata, List<Chapter> chapters)
    {
        byte[]? cover = null;
        var parent = moov[..box.End];
        if (box.Type == "meta")
        {
            // ISO meta is a full box (four bytes of version and flags); QuickTime's is not.
            var offset = box.Length >= 8 && FourCC.Matches(moov[(box.Start + 4)..], "hdlr") ? box.Start : box.Start + 4;
            if (Mp4Boxes.Find(parent, "ilst", offset) is { } ilst)
            {
                cover = Mp4Metadata.ReadItems(Mp4Boxes.Body(moov, ilst), metadata);
            }

            return cover;
        }

        foreach (var child in Mp4Boxes.Children(parent, box.Start))
        {
            if (child.Type == "meta")
            {
                cover = ReadUserData(moov, child, metadata, chapters) ?? cover;
            }
            else if (child.Type == "chpl")
            {
                chapters.AddRange(Mp4Metadata.ReadChapterList(Mp4Boxes.Body(moov, child)));
            }
        }

        return cover;
    }

    /// <summary>Appends a movie fragment's samples to their tracks.</summary>
    private void ReadFragment(long moofStart, byte[] moof, Dictionary<int, (uint Duration, uint Size, uint Flags)> defaults)
    {
        var span = moof.AsSpan();
        var previousEnd = moofStart;
        foreach (var traf in Mp4Boxes.Children(span))
        {
            if (traf.Type != "traf" || Mp4Boxes.Find(span[..traf.End], "tfhd", traf.Start) is not { } tfhd)
            {
                continue;
            }

            var header = Mp4Boxes.Body(span, tfhd);
            var flags = Mp4Boxes.U32(header, 0) & 0xFFFFFF;
            var id = (int)Mp4Boxes.U32(header, 4);
            var track = _tracks.Find(t => t.Id == id);
            if (track is null)
            {
                continue;
            }

            var at = 8;
            var trackDefaults = defaults.GetValueOrDefault(id);
            var baseOffset = (flags & 0x20000) != 0 ? moofStart : previousEnd;
            if ((flags & 0x1) != 0)
            {
                baseOffset = (long)Mp4Boxes.U64(header, at);
                at += 8;
            }

            at += (flags & 0x2) != 0 ? 4 : 0;
            var defaultDuration = (flags & 0x8) != 0 ? Mp4Boxes.U32(header, (at += 4) - 4) : trackDefaults.Duration;
            var defaultSize = (flags & 0x10) != 0 ? Mp4Boxes.U32(header, (at += 4) - 4) : trackDefaults.Size;
            var defaultFlags = (flags & 0x20) != 0 ? Mp4Boxes.U32(header, at) : trackDefaults.Flags;
            var dts = track.NextDts;
            if (Mp4Boxes.Find(span[..traf.End], "tfdt", traf.Start) is { } tfdt)
            {
                var body = Mp4Boxes.Body(span, tfdt);
                dts = body[0] == 1 ? (long)Mp4Boxes.U64(body, 4) : Mp4Boxes.U32(body, 4);
            }

            var dataPosition = baseOffset;
            foreach (var trun in Mp4Boxes.Children(span[..traf.End], traf.Start))
            {
                if (trun.Type != "trun")
                {
                    continue;
                }

                var run = Mp4Boxes.Body(span, trun);
                var runFlags = Mp4Boxes.U32(run, 0) & 0xFFFFFF;
                var count = Mp4Boxes.U32(run, 4);
                var cursor = 8;
                if ((runFlags & 0x1) != 0)
                {
                    dataPosition = baseOffset + (int)Mp4Boxes.U32(run, cursor);
                    cursor += 4;
                }

                if (track.SampleCount + (long)count > Mp4Track.MaxSamples)
                {
                    throw new MediaFormatException("An MP4 track claims more samples than rexplayer accepts.");
                }

                uint? firstFlags = (runFlags & 0x4) != 0 ? Mp4Boxes.U32(run, (cursor += 4) - 4) : null;
                for (var i = 0u; i < count; i++)
                {
                    var duration = (runFlags & 0x100) != 0 ? Mp4Boxes.U32(run, (cursor += 4) - 4) : defaultDuration;
                    var size = (runFlags & 0x200) != 0 ? Mp4Boxes.U32(run, (cursor += 4) - 4) : defaultSize;
                    var sampleFlags = (runFlags & 0x400) != 0 ? Mp4Boxes.U32(run, (cursor += 4) - 4) : i == 0 && firstFlags is { } f ? f : defaultFlags;
                    var composition = 0;
                    if ((runFlags & 0x800) != 0)
                    {
                        var raw = Mp4Boxes.U32(run, (cursor += 4) - 4);
                        composition = run[0] == 1 ? (int)raw : (int)Math.Min(raw, int.MaxValue);
                    }

                    var sync = (sampleFlags & 0x00010000) == 0;
                    if (!sync && track.Sync is null)
                    {
                        track.Sync = [.. Enumerable.Repeat(true, track.SampleCount)];
                    }

                    track.Offsets.Add(dataPosition);
                    track.Sizes.Add((int)Math.Min(size, int.MaxValue));
                    track.Dts.Add(dts);
                    track.CompositionOffsets.Add(composition);
                    track.Sync?.Add(sync);
                    dataPosition += size;
                    dts += duration;
                }
            }

            track.NextDts = dts;
            previousEnd = dataPosition;
        }
    }

    /// <summary>A QuickTime chapter track holds one title per sample: a 16-bit length and UTF-8 text.</summary>
    private List<Chapter> ReadChapterTrack(Mp4Track track)
    {
        var chapters = new List<Chapter>();
        Span<byte> length = stackalloc byte[2];
        for (var i = 0; i < track.SampleCount; i++)
        {
            _stream.Seek(track.Offsets[i]);
            if (track.Sizes[i] < 2 || _stream.Read(length) < 2)
            {
                continue;
            }

            var count = Math.Min((length[0] << 8) | length[1], track.Sizes[i] - 2);
            var text = new byte[count];
            _stream.ReadExactly(text);
            chapters.Add(new Chapter(Time(track, track.Presentation(i)), Encoding.UTF8.GetString(text)));
        }

        return chapters;
    }

    /// <summary>Without an edit list, an iTunes gapless tag says where the audio starts and how long it is.</summary>
    private void ApplyGaplessTag(Dictionary<string, string> metadata)
    {
        if (Mp4Metadata.Gapless(metadata) is not { } gapless)
        {
            return;
        }

        foreach (var track in _tracks.Where(t => t.Handler == "soun" && t.EditDuration is null && t.EditOffset == 0))
        {
            track.EditOffset = gapless.Delay;
            track.EditDuration = gapless.Samples;
        }
    }

    private TrackInfo Describe(Mp4Track track)
    {
        var length = track.EditDuration ?? Math.Max(0, Math.Max(track.MediaDuration, track.NextDts) - track.EditOffset);
        var duration = Time(track, length);
        long totalBytes = 0;
        foreach (var size in track.Sizes)
        {
            totalBytes += size;
        }

        var info = new TrackInfo
        {
            Id = track.Id,
            Codec = track.Codec,
            CodecPrivate = track.CodecPrivate,
            Duration = duration,
            Language = track.Language,
            Title = track.Name,
            IsDefault = track.Enabled && _tracks.First(t => t.Handler == track.Handler) == track,
            BitRate = duration > MediaTime.Zero ? (long)(totalBytes * 8 / duration.TotalSeconds) : null,
        };
        if (track.Handler == "soun" && track.SampleRate > 0 && track.Channels > 0)
        {
            var trailing = Math.Max(track.MediaDuration, track.NextDts) - track.EditOffset - length;
            var bytesPerFrame = track.Codec == CodecId.Pcm ? new AudioFormat(track.SampleRate, track.Channels, track.PcmFormat).BlockAlign : 0;
            info = info with
            {
                Audio = new AudioTrackInfo
                {
                    SampleRate = track.SampleRate,
                    Channels = track.Channels,
                    Layout = ChannelLayouts.Default(track.Channels),
                    BitsPerSample = track.BitsPerSample,
                    PcmFormat = track.PcmFormat,
                    BigEndian = track.BigEndian,
                    BlockAlign = bytesPerFrame,
                    SamplesPerBlock = bytesPerFrame > 0 ? 1 : 0,
                    LeadingPadding = (int)Math.Min(track.EditOffset * track.SampleRate / track.Timescale, int.MaxValue),
                    TrailingPadding = trailing > 0 ? (int)Math.Min(trailing * track.SampleRate / track.Timescale, int.MaxValue) : 0,
                },
            };
        }
        else if (track.Handler == "vide" && track.Width > 0 && track.Height > 0)
        {
            info = info with
            {
                Video = new VideoTrackInfo
                {
                    Width = track.Width,
                    Height = track.Height,
                    PixelAspect = track.PixelAspect,
                    FrameRate = FrameRate(track),
                    Rotation = track.Rotation,
                    Color = track.Color,
                },
            };
        }

        return info;
    }

    /// <summary>The nominal frame rate: the most common sample duration.</summary>
    private static Rational? FrameRate(Mp4Track track)
    {
        if (track.SampleCount < 2)
        {
            return null;
        }

        var delta = track.Dts.Zip(track.Dts.Skip(1), (a, b) => b - a).GroupBy(d => d).MaxBy(g => g.Count())!.Key;
        return delta > 0 ? new Rational(track.Timescale, delta) : null;
    }
}
