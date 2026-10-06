using System.Buffers.Binary;
using System.Text;

namespace Rex.Media.TestKit;

/// <summary>One track of a test MP4 file, with every table choice exposed.</summary>
public sealed class Mp4TrackSpec
{
    public int Id { get; init; } = 1;

    public string Handler { get; init; } = "soun";

    public uint Timescale { get; init; } = 8000;

    public required byte[] SampleEntry { get; init; }

    public IReadOnlyList<byte[]> Samples { get; init; } = [];

    /// <summary>The duration of every sample, unless <see cref="Durations"/> says otherwise.</summary>
    public uint Duration { get; init; } = 160;

    public IReadOnlyList<uint>? Durations { get; init; }

    public IReadOnlyList<int>? CompositionOffsets { get; init; }

    /// <summary>Write ctts as version 1 (signed offsets).</summary>
    public bool SignedCompositionOffsets { get; init; }

    /// <summary>One-based sync sample numbers, or null for no stss (every sample is sync).</summary>
    public IReadOnlyList<int>? SyncSamples { get; init; }

    public int SamplesPerChunk { get; init; } = 2;

    /// <summary>co64 instead of stco.</summary>
    public bool WideOffsets { get; init; }

    /// <summary>0 for stsz; 4, 8 or 16 for stz2 with that field size.</summary>
    public int CompactSizeBits { get; init; }

    /// <summary>Write stsz with one size for every sample (all samples must be that size).</summary>
    public bool FixedSize { get; init; }

    /// <summary>A whole elst box, or null for no edit list.</summary>
    public byte[]? EditList { get; init; }

    /// <summary>Version 1 (64-bit) track and media headers.</summary>
    public bool Version1Headers { get; init; }

    /// <summary>The first two entries of the display matrix (16.16 fixed point), for rotation.</summary>
    public (int A, int B) Matrix { get; init; } = (0x10000, 0);

    public int? ChapterTrack { get; init; }

    public string Language { get; init; } = "eng";

    public bool Enabled { get; init; } = true;

    public string HandlerName { get; init; } = string.Empty;

    /// <summary>Leave out the media header (or the sample table), to test files missing them.</summary>
    public bool OmitMediaHeader { get; init; }

    public bool OmitSampleTable { get; init; }

    public uint MediaDurationOverride { get; init; }
}

/// <summary>One movie fragment: a traf for one track, with its header and run flags chosen by the test.</summary>
public sealed record Mp4Fragment(int TrackId, IReadOnlyList<byte[]> Samples, uint Duration = 160)
{
    /// <summary>tfhd flags: 0x1 base offset, 0x8 default duration, 0x10 default size, 0x20 default flags, 0x20000 base is moof.</summary>
    public uint HeaderFlags { get; init; } = 0x20000;

    /// <summary>trun flags: 0x1 data offset, 0x4 first sample flags, 0x100 durations, 0x200 sizes, 0x400 flags, 0x800 composition.</summary>
    public uint RunFlags { get; init; } = 0x1 | 0x100 | 0x200;

    public IReadOnlyList<uint>? SampleFlags { get; init; }

    public uint FirstSampleFlags { get; init; }

    public uint DefaultFlags { get; init; }

    public long? BaseMediaDecodeTime { get; init; }

    public bool Version1DecodeTime { get; init; }

    public IReadOnlyList<int>? Compositions { get; init; }

    public bool SignedCompositions { get; init; }

    /// <summary>A second traf for this fragment, to test data offsets that follow on from the first.</summary>
    public Mp4Fragment? Next { get; init; }
}

/// <summary>Writes ISO base media files box by box for demuxer tests.</summary>
public static class Mp4Writer
{
    public static byte[] Box(string type, params byte[][] parts)
    {
        var body = Concat(parts);
        return Concat(U32(8 + body.Length), Ascii(type), body);
    }

    public static byte[] Full(string type, byte version, uint flags, params byte[][] parts) =>
        Box(type, [version, (byte)(flags >> 16), (byte)(flags >> 8), (byte)flags], Concat(parts));

    public static byte[] Concat(params byte[][] parts) => [.. parts.SelectMany(p => p)];

    public static byte[] U8(int value) => [(byte)value];

    public static byte[] U16(int value) => [(byte)(value >> 8), (byte)value];

    public static byte[] U24(int value) => [(byte)(value >> 16), (byte)(value >> 8), (byte)value];

    public static byte[] U32(long value)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, (uint)value);
        return bytes;
    }

    public static byte[] U64(long value)
    {
        var bytes = new byte[8];
        BinaryPrimitives.WriteUInt64BigEndian(bytes, (ulong)value);
        return bytes;
    }

    public static byte[] Ascii(string text) => Encoding.Latin1.GetBytes(text);

    /// <summary>An ISO audio sample entry (version 0) with optional child boxes.</summary>
    public static byte[] AudioEntry(string format, int channels, int bits, int rate, params byte[][] children) =>
        Box(format, new byte[6], U16(1), U16(0), new byte[6], U16(channels), U16(bits), U16(0), U16(0), U32((long)rate << 16), Concat(children));

    /// <summary>A QuickTime sound description of version 1 or 2 (version 2 carries the rate as a double and the PCM flags).</summary>
    public static byte[] QuickTimeAudioEntry(string format, int version, int channels, int bits, int rate, uint pcmFlags = 0, params byte[][] children)
    {
        if (version == 1)
        {
            return Box(format, new byte[6], U16(1), U16(1), new byte[6], U16(channels), U16(bits), U16(0), U16(0), U32((long)rate << 16), new byte[16], Concat(children));
        }

        return Box(format, new byte[6], U16(1), U16(2), new byte[6], U16(3), U16(16), U16(0xFFFE), U16(0), U32(0x10000),
            U32(72), U64(BitConverter.DoubleToInt64Bits(rate)), U32(channels), U32(0x7F000000), U32(bits), U32(pcmFlags), U32(channels * bits / 8), U32(1),
            Concat(children));
    }

    /// <summary>A visual sample entry with optional child boxes.</summary>
    public static byte[] VideoEntry(string format, int width, int height, params byte[][] children) =>
        Box(format, new byte[6], U16(1), new byte[16], U16(width), U16(height), U32(0x480000), U32(0x480000), U32(0), U16(1), new byte[32], U16(24), U16(0xFFFF), Concat(children));

    /// <summary>An esds box: ES descriptor (with the given flags), decoder config and optional decoder-specific info.</summary>
    public static byte[] Esds(byte objectType, byte[]? specific = null, byte esFlags = 0)
    {
        var extra = Concat(
            (esFlags & 0x80) != 0 ? U16(1) : [],
            (esFlags & 0x40) != 0 ? [3, .. "url"u8] : [],
            (esFlags & 0x20) != 0 ? U16(2) : []);
        var specificDescriptor = specific is null ? [] : Descriptor(0x05, specific);
        var config = Descriptor(0x04, Concat([objectType, 0x15], U24(0), U32(0), U32(0), specificDescriptor));
        return Full("esds", 0, 0, Descriptor(0x03, Concat(U16(1), [esFlags], extra, config)));
    }

    /// <summary>A descriptor with its size written in the four-byte varint form encoders often use.</summary>
    public static byte[] Descriptor(byte tag, byte[] body) =>
        [tag, (byte)(0x80 | ((body.Length >> 21) & 0x7F)), (byte)(0x80 | ((body.Length >> 14) & 0x7F)), (byte)(0x80 | ((body.Length >> 7) & 0x7F)), (byte)(body.Length & 0x7F), .. body];

    /// <summary>A whole progressive file: ftyp (unless null), moov with the tracks, and their samples in one mdat.</summary>
    public static byte[] File(IReadOnlyList<Mp4TrackSpec> tracks, byte[][]? moovExtras = null, bool movieHeaderV1 = false, string? brand = "isom", uint movieTimescale = 1000)
    {
        ArgumentNullException.ThrowIfNull(tracks);
        var ftyp = brand is null ? [] : Box("ftyp", Ascii(brand), U32(0), Ascii(brand));
        var trial = Moov(tracks, moovExtras ?? [], movieHeaderV1, movieTimescale, 0);
        var mdatStart = ftyp.Length + trial.Length + 8;
        var moov = Moov(tracks, moovExtras ?? [], movieHeaderV1, movieTimescale, mdatStart);
        var payload = Concat([.. tracks.SelectMany(t => t.Samples)]);
        return Concat(ftyp, moov, U32(8 + payload.Length), Ascii("mdat"), payload);
    }

    /// <summary>A fragmented file: a moov whose tracks hold no samples, then a moof and mdat for each fragment.</summary>
    public static byte[] FragmentedFile(IReadOnlyList<Mp4TrackSpec> tracks, IReadOnlyList<Mp4Fragment> fragments, byte[]? trex = null)
    {
        ArgumentNullException.ThrowIfNull(fragments);
        var ftyp = Box("ftyp", Ascii("iso6"), U32(0), Ascii("iso6"));
        var moov = Moov(tracks, trex is null ? [] : [Box("mvex", trex)], false, 1000, 0);
        var file = new List<byte>(Concat(ftyp, moov));
        var sequence = 1;
        foreach (var fragment in fragments)
        {
            var trafs = new List<Mp4Fragment>();
            for (var f = fragment; f is not null; f = f.Next)
            {
                trafs.Add(f);
            }

            // Lay out the moof once to learn its size, then again with the real data offsets.
            byte[] Moof(long moofStart, int moofLength)
            {
                var parts = new List<byte[]> { Full("mfhd", 0, 0, U32(sequence)) };
                var dataStart = moofStart + moofLength + 8;
                long previousEnd = moofStart;
                for (var i = 0; i < trafs.Count; i++)
                {
                    var traf = trafs[i];
                    var samplesStart = dataStart + trafs.Take(i).Sum(t => t.Samples.Sum(s => s.Length));
                    var useBase = (traf.HeaderFlags & 0x1) != 0;
                    var reference = useBase ? samplesStart : (traf.HeaderFlags & 0x20000) != 0 || i == 0 ? moofStart : previousEnd;
                    parts.Add(Traf(traf, useBase ? samplesStart : 0, samplesStart - reference));
                    previousEnd = samplesStart + traf.Samples.Sum(s => s.Length);
                }

                return Box("moof", Concat([.. parts]));
            }

            var start = file.Count;
            var length = Moof(start, 0).Length;
            var moof = Moof(start, length);
            var payload = Concat([.. trafs.SelectMany(t => t.Samples)]);
            file.AddRange(moof);
            file.AddRange(U32(8 + payload.Length));
            file.AddRange(Ascii("mdat"));
            file.AddRange(payload);
            sequence++;
        }

        return [.. file];
    }

    private static byte[] Traf(Mp4Fragment fragment, long baseOffset, long dataOffset)
    {
        var header = Concat(
            U32(fragment.TrackId),
            (fragment.HeaderFlags & 0x1) != 0 ? U64(baseOffset) : [],
            (fragment.HeaderFlags & 0x8) != 0 ? U32(fragment.Duration) : [],
            (fragment.HeaderFlags & 0x10) != 0 ? U32(fragment.Samples.Count == 0 ? 0 : fragment.Samples[0].Length) : [],
            (fragment.HeaderFlags & 0x20) != 0 ? U32(fragment.DefaultFlags) : []);
        var run = new List<byte[]> { U32(fragment.Samples.Count) };
        if ((fragment.RunFlags & 0x1) != 0)
        {
            run.Add(U32(dataOffset));
        }

        if ((fragment.RunFlags & 0x4) != 0)
        {
            run.Add(U32(fragment.FirstSampleFlags));
        }

        for (var i = 0; i < fragment.Samples.Count; i++)
        {
            run.Add((fragment.RunFlags & 0x100) != 0 ? U32(fragment.Duration) : []);
            run.Add((fragment.RunFlags & 0x200) != 0 ? U32(fragment.Samples[i].Length) : []);
            run.Add((fragment.RunFlags & 0x400) != 0 ? U32(fragment.SampleFlags?[i] ?? 0) : []);
            run.Add((fragment.RunFlags & 0x800) != 0 ? U32(fragment.Compositions?[i] ?? 0) : []);
        }

        var parts = new List<byte[]> { Full("tfhd", 0, fragment.HeaderFlags, header) };
        if (fragment.BaseMediaDecodeTime is { } decodeTime)
        {
            parts.Add(fragment.Version1DecodeTime ? Full("tfdt", 1, 0, U64(decodeTime)) : Full("tfdt", 0, 0, U32(decodeTime)));
        }

        parts.Add(Full("trun", fragment.SignedCompositions ? (byte)1 : (byte)0, fragment.RunFlags, Concat([.. run])));
        // A track id below zero writes a traf with no header, which readers must pass over.
        return Box("traf", Concat([.. fragment.TrackId < 0 ? parts.Skip(1) : parts]));
    }

    private static byte[] Moov(IReadOnlyList<Mp4TrackSpec> tracks, byte[][] extras, bool version1, uint timescale, long mdatStart)
    {
        var duration = tracks.Count == 0 ? 0 : tracks.Max(t => (long)t.Samples.Count * t.Duration * timescale / t.Timescale);
        var mvhd = version1
            ? Full("mvhd", 1, 0, U64(0), U64(0), U32(timescale), U64(duration), new byte[80])
            : Full("mvhd", 0, 0, U32(0), U32(0), U32(timescale), U32(duration), new byte[80]);
        var parts = new List<byte[]> { mvhd };
        var offset = mdatStart;
        foreach (var track in tracks)
        {
            parts.Add(Trak(track, offset));
            offset += track.Samples.Sum(s => s.Length);
        }

        parts.AddRange(extras);
        return Box("moov", Concat([.. parts]));
    }

    private static byte[] Trak(Mp4TrackSpec track, long dataStart)
    {
        var matrix = Concat(U32(track.Matrix.A), U32(track.Matrix.B), U32(0), U32(-track.Matrix.B), U32(track.Matrix.A), U32(0), U32(0), U32(0), U32(0x40000000));
        var flags = track.Enabled ? 3u : 2u;
        var tkhd = track.Version1Headers
            ? Full("tkhd", 1, flags, U64(0), U64(0), U32(track.Id), U32(0), U64(0), new byte[16], matrix, U32(0), U32(0))
            : Full("tkhd", 0, flags, U32(0), U32(0), U32(track.Id), U32(0), U32(0), new byte[16], matrix, U32(0), U32(0));
        var language = track.Language.Length == 3 ? ((track.Language[0] - 0x60) << 10) | ((track.Language[1] - 0x60) << 5) | (track.Language[2] - 0x60) : 0x55C4;
        var mediaDuration = track.MediaDurationOverride != 0 ? track.MediaDurationOverride : (uint)(track.Durations?.Sum(d => (long)d) ?? (long)track.Samples.Count * track.Duration);
        var mdhd = track.Version1Headers
            ? Full("mdhd", 1, 0, U64(0), U64(0), U32(track.Timescale), U64(mediaDuration), U16(language), U16(0))
            : Full("mdhd", 0, 0, U32(0), U32(0), U32(track.Timescale), U32(mediaDuration), U16(language), U16(0));
        var hdlr = Full("hdlr", 0, 0, U32(0), Ascii(track.Handler), new byte[12], Ascii(track.HandlerName), [0]);
        var parts = new List<byte[]> { tkhd };
        if (track.ChapterTrack is { } chapter)
        {
            parts.Add(Box("tref", Box("chap", U32(chapter))));
        }

        if (track.EditList is { } elst)
        {
            parts.Add(Box("edts", elst));
        }

        var mdia = new List<byte[]>();
        if (!track.OmitMediaHeader)
        {
            mdia.Add(mdhd);
        }

        mdia.Add(hdlr);
        if (!track.OmitSampleTable)
        {
            mdia.Add(Box("minf", Box("stbl", SampleTable(track, dataStart))));
        }

        parts.Add(Box("mdia", Concat([.. mdia])));
        return Box("trak", Concat([.. parts]));
    }

    private static byte[] SampleTable(Mp4TrackSpec track, long dataStart)
    {
        var count = track.Samples.Count;
        var parts = new List<byte[]> { Full("stsd", 0, 0, U32(1), track.SampleEntry) };
        var durations = track.Durations ?? Enumerable.Repeat(track.Duration, count).ToList();
        parts.Add(Full("stts", 0, 0, U32(count), Concat([.. durations.Select(d => Concat(U32(1), U32(d)))])));
        if (track.CompositionOffsets is { } offsets)
        {
            parts.Add(Full("ctts", track.SignedCompositionOffsets ? (byte)1 : (byte)0, 0, U32(count), Concat([.. offsets.Select(o => Concat(U32(1), U32(o)))])));
        }

        if (track.SyncSamples is { } sync)
        {
            parts.Add(Full("stss", 0, 0, U32(sync.Count), Concat([.. sync.Select(s => U32(s))])));
        }

        var chunks = (count + track.SamplesPerChunk - 1) / Math.Max(1, track.SamplesPerChunk);
        parts.Add(Full("stsc", 0, 0, U32(1), U32(1), U32(track.SamplesPerChunk), U32(1)));
        if (track.CompactSizeBits > 0)
        {
            var bits = track.CompactSizeBits;
            var packed = new byte[((count * bits) + 7) / 8];
            for (var i = 0; i < count; i++)
            {
                var value = track.Samples[i].Length;
                var at = i * bits;
                for (var b = 0; b < bits; b++)
                {
                    if (((value >> (bits - 1 - b)) & 1) != 0)
                    {
                        packed[(at + b) / 8] |= (byte)(0x80 >> ((at + b) % 8));
                    }
                }
            }

            parts.Add(Full("stz2", 0, 0, U24(0), U8(bits), U32(count), packed));
        }
        else if (track.FixedSize)
        {
            parts.Add(Full("stsz", 0, 0, U32(count == 0 ? 0 : track.Samples[0].Length), U32(count)));
        }
        else
        {
            parts.Add(Full("stsz", 0, 0, U32(0), U32(count), Concat([.. track.Samples.Select(s => U32(s.Length))])));
        }

        var chunkOffsets = new List<long>();
        var offset = dataStart;
        for (var i = 0; i < count; i++)
        {
            if (i % Math.Max(1, track.SamplesPerChunk) == 0)
            {
                chunkOffsets.Add(offset);
            }

            offset += track.Samples[i].Length;
        }

        parts.Add(track.WideOffsets
            ? Full("co64", 0, 0, U32(chunks), Concat([.. chunkOffsets.Select(U64)]))
            : Full("stco", 0, 0, U32(chunks), Concat([.. chunkOffsets.Select(U32)])));
        return Concat([.. parts]);
    }
}
