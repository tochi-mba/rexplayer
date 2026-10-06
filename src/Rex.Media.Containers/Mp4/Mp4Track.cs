// Spec: ISO/IEC 14496-12 clauses 8.3 (tkhd, tref), 8.4 (mdhd, hdlr), 8.5 (stsd and sample entries), 8.6 (stts, ctts, stss, edts/elst), 8.7 (stsz, stz2, stsc, stco, co64); ISO/IEC 14496-1 clause 7.2.6 (ES_Descriptor, DecoderConfigDescriptor); ISO/IEC 14496-3 clause 1.6.2.1 (AudioSpecificConfig); ISO/IEC 23003-5 (ipcm, fpcm, pcmC); Apple QuickTime File Format (sound sample description versions 1 and 2, sowt, twos, in24, in32, fl32, fl64, lpcm, enda, wave); "Encapsulation of FLAC in ISO base media file format" (fLaC, dfLa).
using System.Buffers.Binary;
using System.Text;
using Rex.Media.Primitives;

namespace Rex.Media.Containers.Mp4;

/// <summary>
/// One track of an MP4 or QuickTime file: what it carries and where every one of its samples lies.
/// Sample tables are expanded once into flat arrays, so reading and seeking are index lookups.
/// </summary>
internal sealed class Mp4Track
{
    /// <summary>The most samples rexplayer accepts in one track; more is a hostile file, not media.</summary>
    public const int MaxSamples = 50_000_000;

    public int Id { get; set; }

    public string Handler { get; set; } = string.Empty;

    public uint Timescale { get; set; } = 1;

    public long MediaDuration { get; set; }

    public string? Language { get; set; }

    public string? Name { get; set; }

    public bool Enabled { get; set; } = true;

    public int Rotation { get; set; }

    public int? ChapterTrackId { get; set; }

    /// <summary>The media time the presentation starts at (the first edit), subtracted from every timestamp.</summary>
    public long EditOffset { get; set; }

    /// <summary>The presented length in track time units, when an edit list cuts the media short.</summary>
    public long? EditDuration { get; set; }

    public string Format { get; set; } = string.Empty;

    public CodecId Codec { get; set; }

    public byte[] CodecPrivate { get; set; } = [];

    public int SampleRate { get; set; }

    public int Channels { get; set; }

    public int BitsPerSample { get; set; }

    public SampleFormat PcmFormat { get; set; }

    public bool BigEndian { get; set; }

    public int Width { get; set; }

    public int Height { get; set; }

    public Rational PixelAspect { get; set; } = new(1, 1);

    public ColorInfo Color { get; set; } = ColorInfo.Unspecified;

    public List<long> Offsets { get; } = [];

    public List<int> Sizes { get; } = [];

    public List<long> Dts { get; } = [];

    public List<int> CompositionOffsets { get; } = [];

    /// <summary>Which samples are sync samples, or null when every sample is.</summary>
    public List<bool>? Sync { get; set; }

    public int SampleCount => Offsets.Count;

    /// <summary>The decode time just after the last sample, where a movie fragment continues.</summary>
    public long NextDts { get; set; }

    public bool IsSync(int index) => Sync is null || Sync[index];

    /// <summary>Presentation time of a sample, in track units, with the edit applied.</summary>
    public long Presentation(int index) => Dts[index] + CompositionOffsets[index] - EditOffset;

    /// <summary>Reads a trak box. Returns null for a track with no usable sample description.</summary>
    public static Mp4Track? Parse(ReadOnlySpan<byte> moov, Mp4Box trak, uint movieTimescale)
    {
        var track = new Mp4Track();
        if (Mp4Boxes.Path(moov, trak, "tkhd") is { } tkhd)
        {
            track.ReadTrackHeader(Mp4Boxes.Body(moov, tkhd));
        }

        if (Mp4Boxes.Path(moov, trak, "tref", "chap") is { } chap && chap.Length >= 4)
        {
            track.ChapterTrackId = (int)Mp4Boxes.U32(moov, chap.Start);
        }

        if (Mp4Boxes.Path(moov, trak, "mdia", "mdhd") is not { } mdhd || Mp4Boxes.Path(moov, trak, "mdia", "hdlr") is not { } hdlr)
        {
            return null;
        }

        track.ReadMediaHeader(Mp4Boxes.Body(moov, mdhd));
        var handler = Mp4Boxes.Body(moov, hdlr);
        track.Handler = handler.Length >= 12 ? FourCC.ToString(handler.Slice(8, 4)) : string.Empty;
        if (handler.Length > 24)
        {
            var name = Encoding.UTF8.GetString(handler[24..]).Trim('\0', ' ');
            track.Name = name.Length > 0 && !name.EndsWith("Handler", StringComparison.Ordinal) ? name : null;
        }

        if (Mp4Boxes.Path(moov, trak, "edts", "elst") is { } elst)
        {
            track.ReadEditList(Mp4Boxes.Body(moov, elst), movieTimescale);
        }

        if (Mp4Boxes.Path(moov, trak, "mdia", "minf", "stbl") is not { } stbl || Mp4Boxes.Path(moov, stbl, "stsd") is not { } stsd)
        {
            return null;
        }

        track.ReadSampleDescription(Mp4Boxes.Body(moov, stsd));
        track.ReadSampleTables(moov, stbl);
        return track;
    }

    private void ReadTrackHeader(ReadOnlySpan<byte> body)
    {
        var version = body[0];
        Enabled = (body[3] & 1) != 0;
        Id = (int)Mp4Boxes.U32(body, version == 1 ? 20 : 12);
        var matrix = version == 1 ? 52 : 40;
        if (body.Length >= matrix + 36)
        {
            var a = (int)Mp4Boxes.U32(body, matrix);
            var b = (int)Mp4Boxes.U32(body, matrix + 4);
            Rotation = (a, b) switch
            {
                (0, > 0) => 90,
                ( < 0, 0) => 180,
                (0, < 0) => 270,
                _ => 0,
            };
        }
    }

    private void ReadMediaHeader(ReadOnlySpan<byte> body)
    {
        var version = body[0];
        var at = version == 1 ? 20 : 12;
        Timescale = Math.Max(1, Mp4Boxes.U32(body, at));
        MediaDuration = version == 1 ? (long)Math.Min(Mp4Boxes.U64(body, at + 4), long.MaxValue) : Mp4Boxes.U32(body, at + 4);
        var language = Mp4Boxes.U16(body, at + (version == 1 ? 12 : 8));
        var code = new string([(char)(((language >> 10) & 31) + 0x60), (char)(((language >> 5) & 31) + 0x60), (char)((language & 31) + 0x60)]);
        Language = code is "und" or "```" ? null : code;
    }

    /// <summary>The first edit that shows media sets where the presentation starts and, if shorter, how long it lasts.</summary>
    private void ReadEditList(ReadOnlySpan<byte> body, uint movieTimescale)
    {
        var version = body[0];
        var count = Mp4Boxes.U32(body, 4);
        var entry = version == 1 ? 20 : 12;
        for (var i = 0; i < count && 8 + ((i + 1) * entry) <= body.Length; i++)
        {
            var at = 8 + (i * entry);
            var segment = version == 1 ? (long)Math.Min(Mp4Boxes.U64(body, at), long.MaxValue) : Mp4Boxes.U32(body, at);
            var mediaTime = version == 1 ? (long)Mp4Boxes.U64(body, at + 8) : (int)Mp4Boxes.U32(body, at + 4);
            if (mediaTime < 0)
            {
                // An empty edit: a pause before the media starts, which rexplayer does not insert.
                continue;
            }

            EditOffset = mediaTime;
            if (segment > 0 && movieTimescale > 0)
            {
                EditDuration = (long)((Int128)segment * Timescale / movieTimescale);
            }

            return;
        }
    }

    private void ReadSampleDescription(ReadOnlySpan<byte> body)
    {
        var entries = Mp4Boxes.Children(body, 8);
        if (entries.Count == 0)
        {
            return;
        }

        var entry = entries[0];
        Format = entry.Type;
        var data = body[entry.Start..entry.End];
        switch (Handler)
        {
            case "soun":
                ReadAudioEntry(data);
                break;
            case "vide":
                ReadVideoEntry(data);
                break;
            default:
                Codec = Format switch
                {
                    "tx3g" or "text" => CodecId.MovText,
                    "wvtt" => CodecId.WebVtt,
                    "c608" => CodecId.Cea608,
                    _ => CodecId.Unknown,
                };
                break;
        }
    }

    private void ReadAudioEntry(ReadOnlySpan<byte> data)
    {
        if (data.Length < 28)
        {
            return;
        }

        var version = Mp4Boxes.U16(data, 8);
        Channels = Mp4Boxes.U16(data, 16);
        BitsPerSample = Mp4Boxes.U16(data, 18);
        SampleRate = (int)(Mp4Boxes.U32(data, 24) >> 16);
        var children = 28;
        if (version == 1)
        {
            children += 16;
        }
        else if (version == 2 && data.Length >= 64)
        {
            SampleRate = (int)BitConverter.UInt64BitsToDouble(Mp4Boxes.U64(data, 32));
            Channels = (int)Mp4Boxes.U32(data, 40);
            BitsPerSample = (int)Mp4Boxes.U32(data, 48);
            var flags = Mp4Boxes.U32(data, 52);
            if (Format == "lpcm")
            {
                BigEndian = (flags & 2) != 0;
                PcmFormat = (flags & 1) != 0 ? (BitsPerSample == 64 ? SampleFormat.F64 : SampleFormat.F32) : PcmSize(BitsPerSample);
            }

            children = 64;
        }

        var boxes = data.Length > children ? Mp4Boxes.Children(data, children) : [];
        Codec = Format switch
        {
            "mp4a" => CodecId.Aac,
            ".mp3" => CodecId.Mp3,
            "fLaC" => CodecId.Flac,
            "Opus" => CodecId.Opus,
            "alac" => CodecId.Alac,
            "ac-3" => CodecId.Ac3,
            "ec-3" => CodecId.Eac3,
            "dtsc" or "dtsh" or "dtsl" or "dtse" => CodecId.Dts,
            "samr" => CodecId.AmrNb,
            "sawb" => CodecId.AmrWb,
            "ulaw" => CodecId.Mulaw,
            "alaw" => CodecId.Alaw,
            "sowt" or "twos" or "raw " or "in24" or "in32" or "fl32" or "fl64" or "lpcm" or "ipcm" or "fpcm" => CodecId.Pcm,
            _ => CodecId.Unknown,
        };
        if (Codec == CodecId.Pcm && Format != "lpcm")
        {
            (PcmFormat, BigEndian) = Format switch
            {
                "sowt" => (PcmSize(BitsPerSample), false),
                "twos" => (PcmSize(BitsPerSample), BitsPerSample > 8),
                "raw " => (SampleFormat.U8, false),
                "in24" => (SampleFormat.S24, true),
                "in32" => (SampleFormat.S32, true),
                "fl32" => (SampleFormat.F32, true),
                "fl64" => (SampleFormat.F64, true),
                _ => (PcmSize(BitsPerSample), true),
            };
        }

        foreach (var box in boxes)
        {
            ReadAudioChild(data, box);
        }

        if (Codec == CodecId.Pcm && PcmFormat is SampleFormat.S8 or SampleFormat.U8)
        {
            BigEndian = false;
        }
    }

    private void ReadAudioChild(ReadOnlySpan<byte> data, Mp4Box box)
    {
        var body = data[box.Start..box.End];
        switch (box.Type)
        {
            case "esds":
                ReadElementaryStreamDescriptor(body);
                break;
            case "wave":
                // QuickTime wraps the real descriptor (and an endianness box) one level down.
                foreach (var inner in Mp4Boxes.Children(body))
                {
                    ReadAudioChild(body, inner);
                }

                break;
            case "enda" when body.Length >= 2:
                BigEndian = BinaryPrimitives.ReadUInt16BigEndian(body) == 0;
                break;
            case "dfLa" when body.Length >= 8 + 34:
                CodecPrivate = body.Slice(8, 34).ToArray();
                BitsPerSample = ((body[8 + 12] & 1) << 4 | (body[8 + 13] >> 4)) + 1;
                break;
            case "dOps":
            case "alac":
            case "dac3":
            case "dec3":
                CodecPrivate = body.ToArray();
                break;
            case "pcmC" when body.Length >= 6:
                BigEndian = (body[4] & 1) == 0;
                BitsPerSample = body[5];
                PcmFormat = Format == "fpcm" ? (BitsPerSample == 64 ? SampleFormat.F64 : SampleFormat.F32) : PcmSize(BitsPerSample);
                break;
        }
    }

    private static SampleFormat PcmSize(int bits) => bits switch
    {
        8 => SampleFormat.S8,
        24 => SampleFormat.S24,
        32 => SampleFormat.S32,
        _ => SampleFormat.S16,
    };

    /// <summary>The ES descriptor names the codec of an "mp4a" or "mp4v" entry and carries its configuration.</summary>
    private void ReadElementaryStreamDescriptor(ReadOnlySpan<byte> body)
    {
        var at = 4;
        if (!TryDescriptor(body, ref at, 0x03, out var esEnd))
        {
            return;
        }

        // After the ES_ID and flags come, in this order, the stream it depends on, a URL and the
        // OCR stream, each present only when its flag is set.
        var flags = body[at + 2];
        at += 3 + ((flags & 0x80) != 0 ? 2 : 0);
        if ((flags & 0x40) != 0 && at < esEnd)
        {
            at += 1 + body[at];
        }

        at += (flags & 0x20) != 0 ? 2 : 0;

        if (!TryDescriptor(body, ref at, 0x04, out var configEnd) || at + 13 > configEnd)
        {
            return;
        }

        var objectType = body[at];
        Codec = objectType switch
        {
            0x40 or 0x66 or 0x67 or 0x68 => CodecId.Aac,
            0x69 or 0x6B => CodecId.Mp3,
            0xA5 => CodecId.Ac3,
            0xA6 => CodecId.Eac3,
            0xA9 => CodecId.Dts,
            0xDD => CodecId.Vorbis,
            0x20 => CodecId.Mpeg4Part2,
            >= 0x60 and <= 0x65 => CodecId.Mpeg2Video,
            0x6A => CodecId.Mpeg1Video,
            0x6C => CodecId.Mjpeg,
            _ => CodecId.Unknown,
        };
        at += 13;
        if (TryDescriptor(body, ref at, 0x05, out var specificEnd))
        {
            CodecPrivate = body[at..specificEnd].ToArray();
            if (Codec == CodecId.Aac)
            {
                ReadAudioSpecificConfig(CodecPrivate);
            }
        }
    }

    /// <summary>Moves into a descriptor of <paramref name="tag"/> at <paramref name="at"/>; its size is a 7-bit varint.</summary>
    private static bool TryDescriptor(ReadOnlySpan<byte> body, ref int at, byte tag, out int end)
    {
        end = 0;
        if (at >= body.Length || body[at] != tag)
        {
            return false;
        }

        at++;
        var size = 0;
        for (var i = 0; i < 4 && at < body.Length; i++)
        {
            var b = body[at++];
            size = (size << 7) | (b & 0x7F);
            if ((b & 0x80) == 0)
            {
                break;
            }
        }

        end = Math.Min(body.Length, at + size);
        return true;
    }

    /// <summary>The AAC configuration gives the true rate and channel count (the sample entry often says 2 and 44100 regardless).</summary>
    private void ReadAudioSpecificConfig(byte[] config)
    {
        if (config.Length < 2)
        {
            return;
        }

        var reader = new BitReader(config);
        var objectType = (int)reader.ReadBits(5);
        if (objectType == 31)
        {
            objectType = 32 + (int)reader.ReadBits(6);
        }

        var index = (int)reader.ReadBits(4);
        int[] rates = [96000, 88200, 64000, 48000, 44100, 32000, 24000, 22050, 16000, 12000, 11025, 8000, 7350];
        if (index == 15)
        {
            if (reader.BitsRemaining < 28)
            {
                return;
            }

            SampleRate = (int)reader.ReadBits(24);
        }
        else if (index < rates.Length)
        {
            SampleRate = rates[index];
        }

        if (reader.BitsRemaining >= 4)
        {
            var configuration = (int)reader.ReadBits(4);
            Channels = configuration switch
            {
                >= 1 and <= 6 => configuration,
                7 => 8,
                _ => Channels,
            };
        }
    }

    private void ReadVideoEntry(ReadOnlySpan<byte> data)
    {
        if (data.Length < 78)
        {
            return;
        }

        Width = Mp4Boxes.U16(data, 24);
        Height = Mp4Boxes.U16(data, 26);
        Codec = Format switch
        {
            "avc1" or "avc3" => CodecId.H264,
            "hvc1" or "hev1" or "dvh1" or "dvhe" => CodecId.Hevc,
            "av01" => CodecId.Av1,
            "vp08" => CodecId.Vp8,
            "vp09" => CodecId.Vp9,
            "jpeg" or "mjpa" or "mjpb" => CodecId.Mjpeg,
            "s263" or "h263" => CodecId.H263,
            "mp4v" => CodecId.Mpeg4Part2,
            "vc-1" => CodecId.Vc1,
            _ => CodecId.Unknown,
        };
        foreach (var box in Mp4Boxes.Children(data, 78))
        {
            var body = data[box.Start..box.End];
            switch (box.Type)
            {
                case "avcC" or "hvcC" or "av1C" or "vpcC" or "dvc1":
                    CodecPrivate = body.ToArray();
                    break;
                case "esds":
                    ReadElementaryStreamDescriptor(body);
                    break;
                case "pasp" when body.Length >= 8:
                    var h = Mp4Boxes.U32(body, 0);
                    var v = Mp4Boxes.U32(body, 4);
                    if (h > 0 && v > 0)
                    {
                        PixelAspect = new Rational(h, v);
                    }

                    break;
                case "colr" when body.Length >= 11 && FourCC.Matches(body, "nclx"):
                    Color = ColorInfo.FromCodes(Mp4Boxes.U16(body, 4), Mp4Boxes.U16(body, 6), Mp4Boxes.U16(body, 8), (body[10] & 0x80) != 0);
                    break;
            }
        }
    }

    private void ReadSampleTables(ReadOnlySpan<byte> moov, Mp4Box stbl)
    {
        var sizes = ReadSizes(moov, stbl);
        if (sizes is null)
        {
            return;
        }

        var count = sizes.Length;
        if (Codec == CodecId.Pcm && PcmFormat != SampleFormat.Unknown && Channels > 0)
        {
            // Old QuickTime files count uncompressed audio in one-byte "samples"; a sample is a frame.
            var frame = new AudioFormat(Math.Max(SampleRate, 1), Channels, PcmFormat).BlockAlign;
            for (var i = 0; i < count; i++)
            {
                sizes[i] = Math.Max(sizes[i], frame);
            }
        }

        var chunks = ReadChunkOffsets(moov, stbl);
        var perChunk = ReadSampleToChunk(moov, stbl);
        var sample = 0;
        for (var chunk = 0; chunk < chunks.Count && sample < count; chunk++)
        {
            var samples = SamplesInChunk(perChunk, chunk + 1);
            var offset = chunks[chunk];
            for (var i = 0; i < samples && sample < count; i++, sample++)
            {
                Offsets.Add(offset);
                Sizes.Add(sizes[sample]);
                offset += sizes[sample];
            }
        }

        if (Mp4Boxes.Path(moov, stbl, "stts") is { } stts)
        {
            var body = Mp4Boxes.Body(moov, stts);
            long dts = 0;
            var entries = Mp4Boxes.U32(body, 4);
            for (var e = 0; e < entries && 8 + (e * 8) + 8 <= body.Length && Dts.Count < SampleCount; e++)
            {
                var run = Mp4Boxes.U32(body, 8 + (e * 8));
                var delta = Mp4Boxes.U32(body, 12 + (e * 8));
                for (var i = 0u; i < run && Dts.Count < SampleCount; i++)
                {
                    Dts.Add(dts);
                    dts += delta;
                }
            }

            NextDts = dts;
        }

        while (Dts.Count < SampleCount)
        {
            Dts.Add(Dts.Count == 0 ? 0 : Dts[^1]);
        }

        if (Mp4Boxes.Path(moov, stbl, "ctts") is { } ctts)
        {
            var body = Mp4Boxes.Body(moov, ctts);
            var signed = body[0] == 1;
            var entries = Mp4Boxes.U32(body, 4);
            for (var e = 0; e < entries && 8 + (e * 8) + 8 <= body.Length && CompositionOffsets.Count < SampleCount; e++)
            {
                var run = Mp4Boxes.U32(body, 8 + (e * 8));
                var raw = Mp4Boxes.U32(body, 12 + (e * 8));
                var offset = signed ? (int)raw : (int)Math.Min(raw, int.MaxValue);
                for (var i = 0u; i < run && CompositionOffsets.Count < SampleCount; i++)
                {
                    CompositionOffsets.Add(offset);
                }
            }
        }

        while (CompositionOffsets.Count < SampleCount)
        {
            CompositionOffsets.Add(0);
        }

        if (Mp4Boxes.Path(moov, stbl, "stss") is { } stss)
        {
            var body = Mp4Boxes.Body(moov, stss);
            Sync = [.. new bool[SampleCount]];
            var entries = Mp4Boxes.U32(body, 4);
            for (var e = 0; e < entries && 8 + (e * 4) + 4 <= body.Length; e++)
            {
                var number = Mp4Boxes.U32(body, 8 + (e * 4));
                if (number >= 1 && number <= SampleCount)
                {
                    Sync[(int)number - 1] = true;
                }
            }
        }
    }

    private static int[]? ReadSizes(ReadOnlySpan<byte> moov, Mp4Box stbl)
    {
        if (Mp4Boxes.Path(moov, stbl, "stsz") is { } stsz)
        {
            var body = Mp4Boxes.Body(moov, stsz);
            var fixedSize = Mp4Boxes.U32(body, 4);
            var count = CheckedCount(Mp4Boxes.U32(body, 8));
            if (fixedSize != 0)
            {
                return [.. Enumerable.Repeat((int)Math.Min(fixedSize, int.MaxValue), count)];
            }

            if (12 + (4L * count) > body.Length)
            {
                throw Mp4Boxes.Truncated();
            }

            var sizes = new int[count];
            for (var i = 0; i < count; i++)
            {
                sizes[i] = (int)Math.Min(Mp4Boxes.U32(body, 12 + (i * 4)), int.MaxValue);
            }

            return sizes;
        }

        if (Mp4Boxes.Path(moov, stbl, "stz2") is { } stz2)
        {
            var body = Mp4Boxes.Body(moov, stz2);
            var field = body[7];
            var count = CheckedCount(Mp4Boxes.U32(body, 8));
            if (field is not (4 or 8 or 16) || 12 + ((long)count * field / 8) > body.Length)
            {
                throw new MediaFormatException("An MP4 compact sample size table is malformed.");
            }

            var sizes = new int[count];
            var reader = new BitReader(body[12..]);
            for (var i = 0; i < count; i++)
            {
                sizes[i] = (int)reader.ReadBits(field);
            }

            return sizes;
        }

        return null;
    }

    private static int CheckedCount(uint count) =>
        count <= MaxSamples ? (int)count : throw new MediaFormatException("An MP4 track claims more samples than rexplayer accepts.");

    private static List<long> ReadChunkOffsets(ReadOnlySpan<byte> moov, Mp4Box stbl)
    {
        var offsets = new List<long>();
        var wide = false;
        var box = Mp4Boxes.Path(moov, stbl, "stco");
        if (box is null)
        {
            box = Mp4Boxes.Path(moov, stbl, "co64");
            wide = true;
        }

        if (box is { } found)
        {
            var body = Mp4Boxes.Body(moov, found);
            var count = Mp4Boxes.U32(body, 4);
            var size = wide ? 8 : 4;
            for (var i = 0; i < count && 8 + ((i + 1) * size) <= body.Length; i++)
            {
                offsets.Add(wide ? (long)Math.Min(Mp4Boxes.U64(body, 8 + (i * 8)), long.MaxValue) : Mp4Boxes.U32(body, 8 + (i * 4)));
            }
        }

        return offsets;
    }

    private static List<(uint FirstChunk, uint Samples)> ReadSampleToChunk(ReadOnlySpan<byte> moov, Mp4Box stbl)
    {
        var runs = new List<(uint, uint)>();
        if (Mp4Boxes.Path(moov, stbl, "stsc") is { } stsc)
        {
            var body = Mp4Boxes.Body(moov, stsc);
            var count = Mp4Boxes.U32(body, 4);
            for (var i = 0; i < count && 8 + ((i + 1) * 12) <= body.Length; i++)
            {
                runs.Add((Mp4Boxes.U32(body, 8 + (i * 12)), Mp4Boxes.U32(body, 12 + (i * 12))));
            }
        }

        return runs;
    }

    private static long SamplesInChunk(List<(uint FirstChunk, uint Samples)> runs, int chunk)
    {
        long samples = 0;
        foreach (var (first, count) in runs)
        {
            if (first > chunk)
            {
                break;
            }

            samples = count;
        }

        return samples;
    }
}
