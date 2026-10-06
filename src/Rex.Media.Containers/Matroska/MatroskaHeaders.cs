// Spec: IETF RFC 9559 (Matroska) sections 5.1.2 (SeekHead), 5.1.3 (Info), 5.1.4 (Tracks, TrackEntry, Video, Colour, Audio, ContentEncodings with header stripping), 5.1.5 (Cues, CueRelativePosition), 5.1.6 (Attachments), 5.1.7 (Chapters), 5.1.8 (Tags); the Matroska codec mappings ("Codec Specifications": A_*, V_*, S_* identifiers and their CodecPrivate); RFC 9639 section 10.2 for FLAC's codec private data.
using System.Buffers.Binary;
using Rex.Media.Containers.Riff;
using Rex.Media.Containers.Tags;
using Rex.Media.Primitives;

namespace Rex.Media.Containers.Matroska;

/// <summary>Matroska element IDs, with their marker bits, as RFC 9559 lists them.</summary>
internal static class MatroskaId
{
    public const uint Ebml = 0x1A45DFA3;
    public const uint DocType = 0x4282;
    public const uint Segment = 0x18538067;
    public const uint SeekHead = 0x114D9B74;
    public const uint Seek = 0x4DBB;
    public const uint SeekId = 0x53AB;
    public const uint SeekPosition = 0x53AC;
    public const uint Info = 0x1549A966;
    public const uint TimestampScale = 0x2AD7B1;
    public const uint Duration = 0x4489;
    public const uint Title = 0x7BA9;
    public const uint Tracks = 0x1654AE6B;
    public const uint TrackEntry = 0xAE;
    public const uint TrackNumber = 0xD7;
    public const uint TrackType = 0x83;
    public const uint FlagEnabled = 0xB9;
    public const uint FlagDefault = 0x88;
    public const uint FlagForced = 0x55AA;
    public const uint CodecId = 0x86;
    public const uint CodecPrivate = 0x63A2;
    public const uint CodecDelay = 0x56AA;
    public const uint SeekPreRoll = 0x56BB;
    public const uint Name = 0x536E;
    public const uint Language = 0x22B59C;
    public const uint LanguageBcp47 = 0x22B59D;
    public const uint DefaultDuration = 0x23E383;
    public const uint Video = 0xE0;
    public const uint PixelWidth = 0xB0;
    public const uint PixelHeight = 0xBA;
    public const uint DisplayWidth = 0x54B0;
    public const uint DisplayHeight = 0x54BA;
    public const uint Colour = 0x55B0;
    public const uint MatrixCoefficients = 0x55B1;
    public const uint Range = 0x55B9;
    public const uint TransferCharacteristics = 0x55BA;
    public const uint Primaries = 0x55BB;
    public const uint Audio = 0xE1;
    public const uint SamplingFrequency = 0xB5;
    public const uint OutputSamplingFrequency = 0x78B5;
    public const uint Channels = 0x9F;
    public const uint BitDepth = 0x6264;
    public const uint ContentEncodings = 0x6D80;
    public const uint ContentEncoding = 0x6240;
    public const uint ContentCompression = 0x5034;
    public const uint ContentCompAlgo = 0x4254;
    public const uint ContentCompSettings = 0x4255;
    public const uint Cluster = 0x1F43B675;
    public const uint Timestamp = 0xE7;
    public const uint SimpleBlock = 0xA3;
    public const uint BlockGroup = 0xA0;
    public const uint Block = 0xA1;
    public const uint BlockDuration = 0x9B;
    public const uint ReferenceBlock = 0xFB;
    public const uint DiscardPadding = 0x75A2;
    public const uint Cues = 0x1C53BB6B;
    public const uint CuePoint = 0xBB;
    public const uint CueTime = 0xB3;
    public const uint CueTrackPositions = 0xB7;
    public const uint CueTrack = 0xF7;
    public const uint CueClusterPosition = 0xF1;
    public const uint CueRelativePosition = 0xF0;
    public const uint Chapters = 0x1043A770;
    public const uint EditionEntry = 0x45B9;
    public const uint EditionFlagDefault = 0x45DB;
    public const uint ChapterAtom = 0xB6;
    public const uint ChapterTimeStart = 0x91;
    public const uint ChapterFlagHidden = 0x98;
    public const uint ChapterDisplay = 0x80;
    public const uint ChapString = 0x85;
    public const uint Tags = 0x1254C367;
    public const uint Tag = 0x7373;
    public const uint Targets = 0x63C0;
    public const uint TargetTrackUid = 0x63C5;
    public const uint SimpleTag = 0x67C8;
    public const uint TagName = 0x45A3;
    public const uint TagString = 0x4487;
    public const uint Attachments = 0x1941A469;
    public const uint AttachedFile = 0x61A7;
    public const uint FileName = 0x466E;
    public const uint FileMediaType = 0x4660;
    public const uint FileData = 0x465C;

    /// <summary>Whether an ID belongs to a top-level element, which ends an unknown-size cluster.</summary>
    public static bool IsTopLevel(uint id) => id is Cluster or Cues or Tags or Chapters or Attachments or SeekHead or Info or Tracks or Segment or Ebml;
}

/// <summary>One TrackEntry: what the track carries and how its blocks are stored.</summary>
internal sealed class MatroskaTrack
{
    public int Number { get; set; }

    public int Type { get; set; }

    public string CodecName { get; set; } = string.Empty;

    public byte[] CodecPrivate { get; set; } = [];

    public long CodecDelayNs { get; set; }

    public long SeekPreRollNs { get; set; }

    public long DefaultDurationNs { get; set; }

    public string? Name { get; set; }

    public string? Language { get; set; } = "eng";

    public bool Enabled { get; set; } = true;

    public bool Default { get; set; } = true;

    public bool Forced { get; set; }

    public double SampleRate { get; set; } = 8000;

    public int Channels { get; set; } = 1;

    public int BitDepth { get; set; }

    public int Width { get; set; }

    public int Height { get; set; }

    public int DisplayWidth { get; set; }

    public int DisplayHeight { get; set; }

    public ColorInfo Color { get; set; } = ColorInfo.Unspecified;

    /// <summary>Bytes the muxer removed from the front of every frame (header stripping), put back on reading.</summary>
    public byte[] StrippedHeader { get; set; } = [];

    /// <summary>A content encoding rexplayer cannot undo (zlib, bzip2, LZO, encryption): the track's frames are unusable.</summary>
    public bool Unreadable { get; set; }

    public static MatroskaTrack Parse(ReadOnlySpan<byte> entry)
    {
        var track = new MatroskaTrack();
        foreach (var element in Ebml.Children(entry))
        {
            var body = entry[element.Start..element.End];
            switch (element.Id)
            {
                case MatroskaId.TrackNumber:
                    track.Number = (int)Math.Min(Ebml.UInt(body), int.MaxValue);
                    break;
                case MatroskaId.TrackType:
                    track.Type = (int)Ebml.UInt(body);
                    break;
                case MatroskaId.FlagEnabled:
                    track.Enabled = Ebml.UInt(body) != 0;
                    break;
                case MatroskaId.FlagDefault:
                    track.Default = Ebml.UInt(body) != 0;
                    break;
                case MatroskaId.FlagForced:
                    track.Forced = Ebml.UInt(body) != 0;
                    break;
                case MatroskaId.CodecId:
                    track.CodecName = Ebml.Text(body);
                    break;
                case MatroskaId.CodecPrivate:
                    track.CodecPrivate = body.ToArray();
                    break;
                case MatroskaId.CodecDelay:
                    track.CodecDelayNs = (long)Math.Min(Ebml.UInt(body), long.MaxValue);
                    break;
                case MatroskaId.SeekPreRoll:
                    track.SeekPreRollNs = (long)Math.Min(Ebml.UInt(body), long.MaxValue);
                    break;
                case MatroskaId.DefaultDuration:
                    track.DefaultDurationNs = (long)Math.Min(Ebml.UInt(body), long.MaxValue);
                    break;
                case MatroskaId.Name:
                    track.Name = Ebml.Text(body);
                    break;
                case MatroskaId.Language or MatroskaId.LanguageBcp47:
                    var language = Ebml.Text(body);
                    track.Language = language is "und" or "" ? null : language;
                    break;
                case MatroskaId.Audio:
                    track.ReadAudio(body);
                    break;
                case MatroskaId.Video:
                    track.ReadVideo(body);
                    break;
                case MatroskaId.ContentEncodings:
                    track.ReadEncodings(body);
                    break;
            }
        }

        return track;
    }

    private void ReadAudio(ReadOnlySpan<byte> audio)
    {
        foreach (var element in Ebml.Children(audio))
        {
            var body = audio[element.Start..element.End];
            switch (element.Id)
            {
                case MatroskaId.SamplingFrequency:
                    SampleRate = Ebml.Float(body);
                    break;
                case MatroskaId.OutputSamplingFrequency:
                    // HE-AAC signals its real (doubled) rate here; the output rate is what plays.
                    SampleRate = Ebml.Float(body);
                    break;
                case MatroskaId.Channels:
                    Channels = (int)Math.Min(Ebml.UInt(body), 64);
                    break;
                case MatroskaId.BitDepth:
                    BitDepth = (int)Math.Min(Ebml.UInt(body), 64);
                    break;
            }
        }
    }

    private void ReadVideo(ReadOnlySpan<byte> video)
    {
        foreach (var element in Ebml.Children(video))
        {
            var body = video[element.Start..element.End];
            var value = (int)Math.Min(Ebml.UInt(body), int.MaxValue);
            switch (element.Id)
            {
                case MatroskaId.PixelWidth:
                    Width = value;
                    break;
                case MatroskaId.PixelHeight:
                    Height = value;
                    break;
                case MatroskaId.DisplayWidth:
                    DisplayWidth = value;
                    break;
                case MatroskaId.DisplayHeight:
                    DisplayHeight = value;
                    break;
                case MatroskaId.Colour:
                    int matrix = 2, transfer = 2, primaries = 2, range = 0;
                    foreach (var colour in Ebml.Children(body))
                    {
                        var code = (int)Math.Min(Ebml.UInt(body[colour.Start..colour.End]), int.MaxValue);
                        switch (colour.Id)
                        {
                            case MatroskaId.MatrixCoefficients:
                                matrix = code;
                                break;
                            case MatroskaId.TransferCharacteristics:
                                transfer = code;
                                break;
                            case MatroskaId.Primaries:
                                primaries = code;
                                break;
                            case MatroskaId.Range:
                                range = code;
                                break;
                        }
                    }

                    Color = ColorInfo.FromCodes(primaries, transfer, matrix, range == 2);
                    break;
            }
        }
    }

    /// <summary>Header stripping (algorithm 3) is undone on reading; any other encoding makes the track unreadable.</summary>
    private void ReadEncodings(ReadOnlySpan<byte> encodings)
    {
        foreach (var encoding in Ebml.Children(encodings))
        {
            var body = encodings[encoding.Start..encoding.End];
            if (encoding.Id != MatroskaId.ContentEncoding || Ebml.Find(body, MatroskaId.ContentCompression) is not { } compression)
            {
                Unreadable = true;
                continue;
            }

            var settings = body[compression.Start..compression.End];
            var algorithm = Ebml.Find(settings, MatroskaId.ContentCompAlgo) is { } algo ? Ebml.UInt(settings[algo.Start..algo.End]) : 0;
            if (algorithm != 3)
            {
                Unreadable = true;
                continue;
            }

            StrippedHeader = Ebml.Find(settings, MatroskaId.ContentCompSettings) is { } stripped ? settings[stripped.Start..stripped.End].ToArray() : [];
        }
    }

    /// <summary>The track as the engine sees it.</summary>
    public TrackInfo Describe(MediaTime duration)
    {
        var (codec, privateData) = Map();
        var info = new TrackInfo
        {
            Id = Number,
            Codec = codec,
            CodecPrivate = privateData,
            Duration = duration,
            Language = Language,
            Title = Name,
            IsDefault = Default && Enabled,
            IsForced = Forced,
        };
        if (Type == 2)
        {
            var rate = (int)Math.Round(SampleRate);
            var (format, bigEndian) = PcmLayout();
            var bits = codec == CodecId.Flac && privateData.Length >= 34 ? ((privateData[12] & 1) << 4 | (privateData[13] >> 4)) + 1 : BitDepth;
            var blockAlign = format == SampleFormat.Unknown ? 0 : new AudioFormat(Math.Max(1, rate), Math.Max(1, Channels), format).BlockAlign;
            return info with
            {
                Audio = new AudioTrackInfo
                {
                    SampleRate = rate,
                    Channels = Channels,
                    Layout = ChannelLayouts.Default(Channels),
                    BitsPerSample = bits,
                    PcmFormat = format,
                    BigEndian = bigEndian,
                    BlockAlign = blockAlign,
                    SamplesPerBlock = blockAlign > 0 ? 1 : 0,
                    LeadingPadding = (int)Math.Min(((CodecDelayNs * rate) + 500_000_000) / 1_000_000_000, int.MaxValue),
                },
            };
        }

        if (Type == 1 && Width > 0 && Height > 0)
        {
            var aspect = DisplayWidth > 0 && DisplayHeight > 0
                ? new Rational((long)DisplayWidth * Height, (long)DisplayHeight * Width)
                : new Rational(1, 1);
            return info with
            {
                Video = new VideoTrackInfo
                {
                    Width = Width,
                    Height = Height,
                    PixelAspect = aspect,
                    FrameRate = DefaultDurationNs > 0 ? new Rational(1_000_000_000, DefaultDurationNs) : null,
                    Color = Color,
                },
            };
        }

        return info;
    }

    private (SampleFormat Format, bool BigEndian) PcmLayout() => CodecName switch
    {
        "A_PCM/INT/LIT" => (BitDepth switch { 8 => SampleFormat.U8, 24 => SampleFormat.S24, 32 => SampleFormat.S32, _ => SampleFormat.S16 }, false),
        "A_PCM/INT/BIG" => (BitDepth switch { 8 => SampleFormat.U8, 24 => SampleFormat.S24, 32 => SampleFormat.S32, _ => SampleFormat.S16 }, true),
        "A_PCM/FLOAT/IEEE" => (BitDepth == 64 ? SampleFormat.F64 : SampleFormat.F32, false),
        _ => (SampleFormat.Unknown, false),
    };

    /// <summary>The codec named by the CodecID string, and the configuration its decoder wants.</summary>
    private (CodecId Codec, byte[] Private) Map()
    {
        var name = CodecName;
        if (name.StartsWith("A_AAC", StringComparison.Ordinal))
        {
            return (CodecId.Aac, CodecPrivate);
        }

        if (name == "A_FLAC")
        {
            return (CodecId.Flac, FlacStreamInfo());
        }

        if (name == "A_MS/ACM" && CodecPrivate.Length >= 16)
        {
            return (WaveFormat.Parse(CodecPrivate, bigEndian: false).Codec, CodecPrivate);
        }

        if (name == "V_MS/VFW/FOURCC" && CodecPrivate.Length >= 20)
        {
            return (FourCC.ToString(CodecPrivate.AsSpan(16, 4)).ToUpperInvariant() switch
            {
                "H264" or "AVC1" or "X264" => CodecId.H264,
                "HEVC" or "HVC1" or "H265" => CodecId.Hevc,
                "DIVX" or "DX50" or "XVID" or "FMP4" or "MP4V" => CodecId.Mpeg4Part2,
                "MJPG" => CodecId.Mjpeg,
                "WMV3" => CodecId.Wmv3,
                "WVC1" => CodecId.Vc1,
                "VP80" => CodecId.Vp8,
                "VP90" => CodecId.Vp9,
                _ => CodecId.Unknown,
            }, CodecPrivate);
        }

        var codec = name switch
        {
            "A_MPEG/L3" => CodecId.Mp3,
            "A_MPEG/L2" => CodecId.Mp2,
            "A_MPEG/L1" => CodecId.Mp1,
            "A_AC3" or "A_AC3/BSID9" or "A_AC3/BSID10" => CodecId.Ac3,
            "A_EAC3" => CodecId.Eac3,
            "A_DTS" or "A_DTS/EXPRESS" or "A_DTS/LOSSLESS" => CodecId.Dts,
            "A_TRUEHD" or "A_MLP" => CodecId.TrueHd,
            "A_VORBIS" => CodecId.Vorbis,
            "A_OPUS" => CodecId.Opus,
            "A_ALAC" => CodecId.Alac,
            "A_PCM/INT/LIT" or "A_PCM/INT/BIG" or "A_PCM/FLOAT/IEEE" => CodecId.Pcm,
            "V_MPEG4/ISO/AVC" => CodecId.H264,
            "V_MPEGH/ISO/HEVC" => CodecId.Hevc,
            "V_AV1" => CodecId.Av1,
            "V_VP8" => CodecId.Vp8,
            "V_VP9" => CodecId.Vp9,
            "V_MPEG1" => CodecId.Mpeg1Video,
            "V_MPEG2" => CodecId.Mpeg2Video,
            "V_MPEG4/ISO/ASP" or "V_MPEG4/ISO/SP" or "V_MPEG4/ISO/AP" => CodecId.Mpeg4Part2,
            "V_MJPEG" => CodecId.Mjpeg,
            "V_THEORA" => CodecId.Theora,
            "V_UNCOMPRESSED" => CodecId.RawVideo,
            "S_TEXT/UTF8" or "S_TEXT/ASCII" => CodecId.SubRip,
            "S_TEXT/ASS" or "S_ASS" => CodecId.Ass,
            "S_TEXT/SSA" or "S_SSA" => CodecId.Ssa,
            "S_TEXT/WEBVTT" or "D_WEBVTT/SUBTITLES" => CodecId.WebVtt,
            "S_VOBSUB" => CodecId.VobSub,
            "S_HDMV/PGS" => CodecId.Pgs,
            "S_DVBSUB" => CodecId.DvbSubtitle,
            _ => CodecId.Unknown,
        };
        return (codec, CodecPrivate);
    }

    /// <summary>FLAC's private data is "fLaC" and its metadata blocks; the decoder wants the 34-byte STREAMINFO.</summary>
    private byte[] FlacStreamInfo()
    {
        var data = CodecPrivate.AsSpan();
        var at = data.StartsWith("fLaC"u8) ? 4 : 0;
        while (at + 4 <= data.Length)
        {
            var type = data[at] & 0x7F;
            var length = (data[at + 1] << 16) | (data[at + 2] << 8) | data[at + 3];
            if (type == 0 && at + 4 + 34 <= data.Length)
            {
                return data.Slice(at + 4, 34).ToArray();
            }

            at += 4 + length;
        }

        return [];
    }
}

/// <summary>The segment-level metadata: cue points, chapters, tags and attached cover art.</summary>
internal static class MatroskaMetadata
{
    /// <summary>
    /// Cue points as (time in timestamp units, track, cluster position relative to the segment
    /// data, block position relative to the cluster's data or 0 when not given).
    /// </summary>
    public static List<(long Time, int Track, long Position, long Relative)> ReadCues(ReadOnlySpan<byte> cues)
    {
        var points = new List<(long, int, long, long)>();
        foreach (var point in Ebml.Children(cues))
        {
            if (point.Id != MatroskaId.CuePoint)
            {
                continue;
            }

            var body = cues[point.Start..point.End];
            long time = -1;
            foreach (var child in Ebml.Children(body))
            {
                var value = body[child.Start..child.End];
                if (child.Id == MatroskaId.CueTime)
                {
                    time = (long)Math.Min(Ebml.UInt(value), long.MaxValue);
                }
                else if (child.Id == MatroskaId.CueTrackPositions && time >= 0)
                {
                    var track = Ebml.Find(value, MatroskaId.CueTrack) is { } t ? (int)Math.Min(Ebml.UInt(value[t.Start..t.End]), int.MaxValue) : 0;
                    if (Ebml.Find(value, MatroskaId.CueClusterPosition) is { } p)
                    {
                        var relative = ReadUIntOr(value, MatroskaId.CueRelativePosition, 0);
                        points.Add((time, track, (long)Math.Min(Ebml.UInt(value[p.Start..p.End]), long.MaxValue), relative));
                    }
                }
            }
        }

        points.Sort((a, b) => a.Item1.CompareTo(b.Item1));
        return points;
    }

    /// <summary>The chapters of the default edition (or the first), flattened in order, hidden ones left out.</summary>
    public static List<Chapter> ReadChapters(ReadOnlySpan<byte> chapters)
    {
        EbmlElement? chosen = null;
        foreach (var edition in Ebml.Children(chapters))
        {
            if (edition.Id != MatroskaId.EditionEntry)
            {
                continue;
            }

            var body = chapters[edition.Start..edition.End];
            if (Ebml.Find(body, MatroskaId.EditionFlagDefault) is { } flag && Ebml.UInt(body[flag.Start..flag.End]) != 0)
            {
                chosen = edition;
                break;
            }

            chosen ??= edition;
        }

        var result = new List<Chapter>();
        if (chosen is { } found)
        {
            ReadAtoms(chapters[found.Start..found.End], result);
        }

        result.Sort((a, b) => a.Start.CompareTo(b.Start));
        return result;
    }

    private static void ReadAtoms(ReadOnlySpan<byte> parent, List<Chapter> result)
    {
        foreach (var atom in Ebml.Children(parent))
        {
            if (atom.Id != MatroskaId.ChapterAtom)
            {
                continue;
            }

            var body = parent[atom.Start..atom.End];
            long start = 0;
            string? title = null;
            var hidden = false;
            foreach (var child in Ebml.Children(body))
            {
                var value = body[child.Start..child.End];
                switch (child.Id)
                {
                    case MatroskaId.ChapterTimeStart:
                        start = (long)Math.Min(Ebml.UInt(value), long.MaxValue);
                        break;
                    case MatroskaId.ChapterFlagHidden:
                        hidden = Ebml.UInt(value) != 0;
                        break;
                    case MatroskaId.ChapterDisplay when title is null && Ebml.Find(value, MatroskaId.ChapString) is { } text:
                        title = Ebml.Text(value[text.Start..text.End]);
                        break;
                }
            }

            if (!hidden)
            {
                result.Add(new Chapter(new MediaTime(start / 100), title ?? string.Empty));
                ReadAtoms(body, result);
            }
        }
    }

    /// <summary>Tags that describe the whole file (not one track) into <paramref name="metadata"/>.</summary>
    public static void ReadTags(ReadOnlySpan<byte> tags, IDictionary<string, string> metadata)
    {
        foreach (var tag in Ebml.Children(tags))
        {
            if (tag.Id != MatroskaId.Tag)
            {
                continue;
            }

            var body = tags[tag.Start..tag.End];
            if (Ebml.Find(body, MatroskaId.Targets) is { } targets && Ebml.Find(body[targets.Start..targets.End], MatroskaId.TargetTrackUid) is not null)
            {
                continue;
            }

            foreach (var simple in Ebml.Children(body))
            {
                if (simple.Id != MatroskaId.SimpleTag)
                {
                    continue;
                }

                var fields = body[simple.Start..simple.End];
                if (Ebml.Find(fields, MatroskaId.TagName) is { } name && Ebml.Find(fields, MatroskaId.TagString) is { } value)
                {
                    var text = Ebml.Text(fields[value.Start..value.End]);
                    if (text.Length > 0)
                    {
                        metadata[VorbisComments.CanonicalName(Ebml.Text(fields[name.Start..name.End]))] = text;
                    }
                }
            }
        }
    }

    /// <summary>The attached image named like a cover, or the first attached image.</summary>
    public static byte[]? ReadCover(ReadOnlySpan<byte> attachments)
    {
        byte[]? first = null;
        foreach (var file in Ebml.Children(attachments))
        {
            if (file.Id != MatroskaId.AttachedFile)
            {
                continue;
            }

            var body = attachments[file.Start..file.End];
            var type = Ebml.Find(body, MatroskaId.FileMediaType) is { } t ? Ebml.Text(body[t.Start..t.End]) : string.Empty;
            var name = Ebml.Find(body, MatroskaId.FileName) is { } n ? Ebml.Text(body[n.Start..n.End]) : string.Empty;
            if (!type.StartsWith("image/", StringComparison.Ordinal) || Ebml.Find(body, MatroskaId.FileData) is not { } data)
            {
                continue;
            }

            var bytes = body[data.Start..data.End].ToArray();
            if (name.StartsWith("cover", StringComparison.OrdinalIgnoreCase))
            {
                return bytes;
            }

            first ??= bytes;
        }

        return first;
    }

    /// <summary>A SeekHead's entries: the element each one points at and its position in the segment.</summary>
    public static List<(uint Id, long Position)> ReadSeekHead(ReadOnlySpan<byte> seekHead)
    {
        var entries = new List<(uint, long)>();
        foreach (var seek in Ebml.Children(seekHead))
        {
            var body = seekHead[seek.Start..seek.End];
            if (seek.Id == MatroskaId.Seek && Ebml.Find(body, MatroskaId.SeekId) is { } id && Ebml.Find(body, MatroskaId.SeekPosition) is { } position)
            {
                entries.Add(((uint)Ebml.UInt(body[id.Start..id.End]), (long)Math.Min(Ebml.UInt(body[position.Start..position.End]), long.MaxValue)));
            }
        }

        return entries;
    }

    public static long ReadUIntOr(ReadOnlySpan<byte> parent, uint id, long fallback) =>
        Ebml.Find(parent, id) is { } element ? (long)Math.Min(Ebml.UInt(parent[element.Start..element.End]), long.MaxValue) : fallback;

    public static int Int16(ReadOnlySpan<byte> data) => BinaryPrimitives.ReadInt16BigEndian(data);
}
