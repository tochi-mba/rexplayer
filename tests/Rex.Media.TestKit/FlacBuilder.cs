using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Rex.Media.Primitives;

namespace Rex.Media.TestKit;

/// <summary>Writes the subframe bits of a hand-built frame.</summary>
public delegate void BitBody(ref BitWriter writer);

/// <summary>How a test asks for a FLAC subframe to be coded.</summary>
public enum FlacSubframeKind
{
    Constant,
    Verbatim,
    Fixed,
    Lpc,
}

/// <summary>How a stereo frame is decorrelated (RFC 9639 section 9.1.3).</summary>
public enum FlacStereo
{
    Independent,
    LeftSide,
    SideRight,
    MidSide,
}

/// <summary>Every choice an encoder makes for one subframe, so a test can force each coding path.</summary>
public sealed record FlacSubframePlan
{
    public FlacSubframeKind Kind { get; init; } = FlacSubframeKind.Fixed;

    /// <summary>Predictor order: 0 to 4 for fixed, 1 to 32 for LPC.</summary>
    public int Order { get; init; } = 2;

    /// <summary>Quantised LPC coefficients, the newest sample's first. Any values code losslessly.</summary>
    public int[] Coefficients { get; init; } = [];

    public int Precision { get; init; } = 15;

    public int Shift { get; init; }

    public int PartitionOrder { get; init; }

    /// <summary>Rice parameters in 5 bits (coding method 1) instead of 4.</summary>
    public bool FiveBitParameters { get; init; }

    /// <summary>Store every partition as raw fixed-width values instead of Rice codes.</summary>
    public bool Escape { get; init; }

    /// <summary>Shift out zero low bits common to every sample and say so in the subframe header.</summary>
    public bool UseWastedBits { get; init; } = true;

    public static FlacSubframePlan Fixed(int order, int partitionOrder = 0) => new() { Kind = FlacSubframeKind.Fixed, Order = order, PartitionOrder = partitionOrder };

    /// <summary>An order-2 LPC subframe equivalent to the fixed order-2 predictor, at a given precision and shift.</summary>
    public static FlacSubframePlan SecondOrderLpc(int shift = 3) => new()
    {
        Kind = FlacSubframeKind.Lpc,
        Order = 2,
        Coefficients = [2 << shift, -(1 << shift)],
        Precision = shift + 3,
        Shift = shift,
    };
}

/// <summary>
/// A small FLAC encoder written from RFC 9639 for tests, independent of the decoder under test. It
/// codes exactly the way it is told: subframe kind, predictor, residual method, partitions,
/// escapes, wasted bits, stereo decorrelation, fixed or variable blocking, header field forms, and
/// the metadata blocks a demuxer meets in real files. Compression is not a goal; coverage is.
/// </summary>
public sealed class FlacBuilder
{
    private const int FlacHeaderMaxLength = 16;

    private readonly List<long> _frameOffsets = [];

    public int SampleRate { get; init; } = 44100;

    public int BitsPerSample { get; init; } = 16;

    public int BlockSize { get; init; } = 1152;

    /// <summary>When set, the stream uses variable blocking and cycles through these sizes.</summary>
    public IReadOnlyList<int>? VariableBlockSizes { get; init; }

    public FlacStereo Stereo { get; init; }

    /// <summary>Chooses the coding of each subframe from (frame index, channel).</summary>
    public Func<int, int, FlacSubframePlan> Plan { get; init; } = (_, _) => FlacSubframePlan.Fixed(2);

    /// <summary>Leave the sample rate out of frame headers ("as STREAMINFO says").</summary>
    public bool RateFromStreamInfo { get; init; }

    /// <summary>Leave the sample size out of frame headers.</summary>
    public bool BitsFromStreamInfo { get; init; }

    /// <summary>Write the total sample count into STREAMINFO (0 means unknown).</summary>
    public bool WriteTotalSamples { get; init; } = true;

    public string Vendor { get; init; } = "rexplayer tests";

    public IReadOnlyList<string>? Comments { get; init; }

    /// <summary>A PICTURE block's image bytes and type (3 is the front cover).</summary>
    public (byte[] Data, string Mime, int Type)? Picture { get; init; }

    /// <summary>A SEEKTABLE with a point every this many frames, plus <see cref="SeekPlaceholders"/> placeholders.</summary>
    public int SeekPointInterval { get; init; }

    public int SeekPlaceholders { get; init; }

    /// <summary>CUESHEET tracks as (sample offset, track number); a lead-out track is added.</summary>
    public IReadOnlyList<(long Offset, int Number)>? CueTracks { get; init; }

    public int Padding { get; init; }

    /// <summary>An APPLICATION block with this id, which readers must skip.</summary>
    public string? Application { get; init; }

    /// <summary>Metadata blocks written exactly as given, before any padding, for malformed-block tests.</summary>
    public IReadOnlyList<(int Type, byte[] Body)> RawBlocks { get; init; } = [];

    /// <summary>An ID3v2 tag of this many payload bytes in front of the stream, as some taggers write.</summary>
    public int Id3PrefixBytes { get; init; }

    /// <summary>Byte offsets of each frame from the first frame, after <see cref="Build"/>.</summary>
    public IReadOnlyList<long> FrameOffsets => _frameOffsets;

    /// <summary>The offset of the first frame in the file, after <see cref="Build"/>.</summary>
    public long FirstFrameOffset { get; private set; }

    /// <summary>A deterministic tone plus a little noise, as integers of <paramref name="bits"/> bits.</summary>
    public static int[] Tone(int samples, int bits, double frequency, int sampleRate, double amplitude = 0.5, int seed = 1)
    {
        var random = new Random(seed);
        var peak = (1L << (bits - 1)) - 1;
        var result = new int[samples];
        for (var i = 0; i < samples; i++)
        {
            var value = (amplitude * Math.Sin(2 * Math.PI * frequency * i / sampleRate)) + ((random.NextDouble() - 0.5) * 0.01);
            result[i] = (int)Math.Clamp(Math.Round(value * peak), -peak - 1, peak);
        }

        return result;
    }

    /// <summary>Encodes <paramref name="channels"/> (one array per channel) into a complete FLAC file.</summary>
    public byte[] Build(int[][] channels)
    {
        ArgumentNullException.ThrowIfNull(channels);
        var total = channels[0].Length;
        var frames = new List<byte[]>();
        var blockSizes = new List<int>();
        _frameOffsets.Clear();
        long offset = 0;
        long position = 0;
        var index = 0;
        while (position < total)
        {
            var size = (int)Math.Min(VariableBlockSizes is { } sizes ? sizes[index % sizes.Count] : BlockSize, total - position);
            var frame = EncodeFrame(channels, (int)position, size, index);
            _frameOffsets.Add(offset);
            frames.Add(frame);
            blockSizes.Add(size);
            offset += frame.Length;
            position += size;
            index++;
        }

        using var file = new MemoryStream();
        if (Id3PrefixBytes > 0)
        {
            file.Write("ID3"u8);
            file.Write([4, 0, 0]);
            var size = Id3PrefixBytes;
            file.Write([(byte)((size >> 21) & 0x7F), (byte)((size >> 14) & 0x7F), (byte)((size >> 7) & 0x7F), (byte)(size & 0x7F)]);
            file.Write(new byte[size]);
        }

        file.Write("fLaC"u8);
        var blocks = MetadataBlocks(channels, blockSizes, frames);
        for (var i = 0; i < blocks.Count; i++)
        {
            var (type, body) = blocks[i];
            file.WriteByte((byte)(type | (i == blocks.Count - 1 ? 0x80 : 0)));
            file.Write([(byte)(body.Length >> 16), (byte)(body.Length >> 8), (byte)body.Length]);
            file.Write(body);
        }

        FirstFrameOffset = file.Position;
        foreach (var frame in frames)
        {
            file.Write(frame);
        }

        return file.ToArray();
    }

    /// <summary>The STREAMINFO MD5 rule: signed little-endian samples, interleaved, in whole bytes.</summary>
    public static byte[] Md5(int[][] channels, int bits)
    {
        ArgumentNullException.ThrowIfNull(channels);
        var width = (bits + 7) / 8;
        var bytes = new byte[channels[0].Length * channels.Length * width];
        var at = 0;
        for (var i = 0; i < channels[0].Length; i++)
        {
            foreach (var channel in channels)
            {
                for (var b = 0; b < width; b++)
                {
                    bytes[at++] = (byte)(channel[i] >> (8 * b));
                }
            }
        }

#pragma warning disable CA5351 // The format defines this checksum; it is not used for security.
        return MD5.HashData(bytes);
#pragma warning restore CA5351
    }

    private List<(int Type, byte[] Body)> MetadataBlocks(int[][] channels, List<int> blockSizes, List<byte[]> frames)
    {
        var blocks = new List<(int, byte[])> { (0, StreamInfo(channels, blockSizes, frames)) };
        if (Application is not null)
        {
            blocks.Add((2, [.. Encoding.ASCII.GetBytes(Application), 1, 2, 3]));
        }

        if (SeekPointInterval > 0)
        {
            blocks.Add((3, SeekTable(blockSizes)));
        }

        if (Comments is not null)
        {
            blocks.Add((4, VorbisComments(Vendor, Comments)));
        }

        if (Picture is { } picture)
        {
            blocks.Add((6, PictureBlock(picture.Data, picture.Mime, picture.Type)));
        }

        if (CueTracks is not null)
        {
            blocks.Add((5, CueSheet(CueTracks, channels[0].Length)));
        }

        blocks.AddRange(RawBlocks);
        if (Padding > 0)
        {
            blocks.Add((1, new byte[Padding]));
        }

        return blocks;
    }

    private byte[] StreamInfo(int[][] channels, List<int> blockSizes, List<byte[]> frames)
    {
        var allButLast = blockSizes.Count > 1 ? blockSizes.Take(blockSizes.Count - 1).ToList() : blockSizes;
        var minBlock = VariableBlockSizes is null ? BlockSize : Math.Max(16, allButLast.Min());
        var maxBlock = VariableBlockSizes is null ? BlockSize : Math.Max(minBlock, blockSizes.Max());
        var body = new byte[34];
        var writer = new BitWriter(body);
        writer.Write((uint)minBlock, 16);
        writer.Write((uint)maxBlock, 16);
        writer.Write((uint)frames.Min(f => f.Length), 24);
        writer.Write((uint)frames.Max(f => f.Length), 24);
        writer.Write((uint)SampleRate, 20);
        writer.Write((uint)(channels.Length - 1), 3);
        writer.Write((uint)(BitsPerSample - 1), 5);
        writer.Write64(WriteTotalSamples ? (ulong)channels[0].Length : 0, 36);
        Md5(channels, BitsPerSample).CopyTo(body, 18);
        return body;
    }

    private byte[] SeekTable(List<int> blockSizes)
    {
        var points = new List<byte[]>();
        long sample = 0;
        for (var i = 0; i < blockSizes.Count; i++)
        {
            if (i % SeekPointInterval == 0)
            {
                var point = new byte[18];
                BinaryPrimitives.WriteUInt64BigEndian(point, (ulong)sample);
                BinaryPrimitives.WriteUInt64BigEndian(point.AsSpan(8), (ulong)_frameOffsets[i]);
                BinaryPrimitives.WriteUInt16BigEndian(point.AsSpan(16), (ushort)blockSizes[i]);
                points.Add(point);
            }

            sample += blockSizes[i];
        }

        for (var i = 0; i < SeekPlaceholders; i++)
        {
            var placeholder = new byte[18];
            BinaryPrimitives.WriteUInt64BigEndian(placeholder, ulong.MaxValue);
            points.Add(placeholder);
        }

        return [.. points.SelectMany(p => p)];
    }

    /// <summary>A Vorbis comment block: little-endian lengths, a vendor string and KEY=value entries.</summary>
    public static byte[] VorbisComments(string vendor, IReadOnlyList<string> comments)
    {
        ArgumentNullException.ThrowIfNull(vendor);
        ArgumentNullException.ThrowIfNull(comments);
        using var body = new MemoryStream();
        WriteLittleString(body, vendor);
        Span<byte> count = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(count, (uint)comments.Count);
        body.Write(count);
        foreach (var comment in comments)
        {
            WriteLittleString(body, comment);
        }

        return body.ToArray();
    }

    private static byte[] PictureBlock(byte[] data, string mime, int type)
    {
        using var body = new MemoryStream();
        WriteBig(body, (uint)type);
        WriteBig(body, (uint)mime.Length);
        body.Write(Encoding.ASCII.GetBytes(mime));
        var description = "cover"u8;
        WriteBig(body, (uint)description.Length);
        body.Write(description);
        WriteBig(body, 1);
        WriteBig(body, 1);
        WriteBig(body, 24);
        WriteBig(body, 0);
        WriteBig(body, (uint)data.Length);
        body.Write(data);
        return body.ToArray();
    }

    private static byte[] CueSheet(IReadOnlyList<(long Offset, int Number)> tracks, long totalSamples)
    {
        using var body = new MemoryStream();
        body.Write(new byte[128]);
        body.Write(new byte[8]);
        body.WriteByte(0x80);
        body.Write(new byte[258]);
        body.WriteByte((byte)(tracks.Count + 1));
        Span<byte> value = stackalloc byte[8];
        foreach (var (offset, number) in tracks.Append((totalSamples, 170)))
        {
            BinaryPrimitives.WriteUInt64BigEndian(value, (ulong)offset);
            body.Write(value);
            body.WriteByte((byte)number);
            body.Write(new byte[12]);
            body.WriteByte(0);
            body.Write(new byte[13]);
            var indexes = number == 170 ? 0 : 1;
            body.WriteByte((byte)indexes);
            for (var i = 0; i < indexes; i++)
            {
                body.Write(new byte[8]);
                body.WriteByte(1);
                body.Write(new byte[3]);
            }
        }

        return body.ToArray();
    }

    private byte[] EncodeFrame(int[][] channels, int start, int size, int index)
    {
        var buffer = new byte[65536 + (size * channels.Length * 16)];
        var writer = new BitWriter(buffer);
        var channelCode = Stereo switch
        {
            FlacStereo.LeftSide => 8,
            FlacStereo.SideRight => 9,
            FlacStereo.MidSide => 10,
            _ => channels.Length - 1,
        };
        var variable = VariableBlockSizes is not null;
        var header = FrameHeader(variable, size, RateFromStreamInfo ? 0 : SampleRate, channelCode, BitsFromStreamInfo ? 0 : BitsPerSample, variable ? (ulong)start : (ulong)index);
        foreach (var b in header)
        {
            writer.Write(b, 8);
        }

        var coded = Decorrelate(channels, start, size);
        for (var channel = 0; channel < coded.Length; channel++)
        {
            var side = Stereo switch
            {
                FlacStereo.SideRight => channel == 0,
                FlacStereo.LeftSide or FlacStereo.MidSide => channel == 1,
                _ => false,
            };
            WriteSubframe(ref writer, coded[channel], side ? BitsPerSample + 1 : BitsPerSample, Plan(index, channel));
        }

        writer.AlignToByte();
        var length = writer.BytesWritten;
        var crc = Crc.Crc16Flac.Compute(buffer.AsSpan(0, length));
        writer.Write(crc, 16);
        return buffer[..writer.BytesWritten];
    }

    private long[][] Decorrelate(int[][] channels, int start, int size)
    {
        var coded = new long[channels.Length][];
        for (var c = 0; c < channels.Length; c++)
        {
            coded[c] = channels[c].AsSpan(start, size).ToArray().Select(v => (long)v).ToArray();
        }

        if (Stereo == FlacStereo.Independent)
        {
            return coded;
        }

        var left = coded[0];
        var right = coded[1];
        var side = left.Zip(right, (l, r) => l - r).ToArray();
        return Stereo switch
        {
            FlacStereo.LeftSide => [left, side],
            FlacStereo.SideRight => [side, right],
            _ => [left.Zip(right, (l, r) => (l + r) >> 1).ToArray(), side],
        };
    }

    /// <summary>
    /// A frame header with its CRC-8. A rate or sample size of 0 writes the "as STREAMINFO says"
    /// code; the block size and rate use the short codes when one exists and the extra bytes otherwise.
    /// </summary>
    public static byte[] FrameHeader(bool variable, int blockSize, int sampleRate, int channelCode, int bitsPerSample, ulong number)
    {
        var buffer = new byte[FlacHeaderMaxLength];
        var writer = new BitWriter(buffer);
        writer.Write(0b11111111111110, 14);
        writer.Write(0, 1);
        writer.WriteBit(variable);
        var (blockCode, blockExtraBits) = BlockCode(blockSize);
        writer.Write((uint)blockCode, 4);
        var (rateCode, rateExtra, rateExtraBits) = sampleRate == 0 ? (0, 0, 0) : RateCode(sampleRate);
        writer.Write((uint)rateCode, 4);
        writer.Write((uint)channelCode, 4);
        writer.Write((uint)SizeCode(bitsPerSample), 3);
        writer.Write(0, 1);
        WriteCodedNumber(ref writer, number);
        if (blockExtraBits > 0)
        {
            writer.Write((uint)(blockSize - 1), blockExtraBits);
        }

        if (rateExtraBits > 0)
        {
            writer.Write((uint)rateExtra, rateExtraBits);
        }

        writer.Write(Crc.Crc8Flac.Compute(buffer.AsSpan(0, writer.BytesWritten)), 8);
        return buffer[..writer.BytesWritten];
    }

    /// <summary>
    /// A frame from a header and hand-written subframe bits, padded and closed with its CRC-16 (or a
    /// wrong one), for decoder tests that need exactly one malformed field.
    /// </summary>
    public static byte[] Frame(byte[] header, BitBody body, bool breakCrc = false)
    {
        ArgumentNullException.ThrowIfNull(header);
        ArgumentNullException.ThrowIfNull(body);
        var buffer = new byte[header.Length + (1 << 20)];
        var writer = new BitWriter(buffer);
        foreach (var b in header)
        {
            writer.Write(b, 8);
        }

        body(ref writer);
        writer.AlignToByte();
        var crc = Crc.Crc16Flac.Compute(buffer.AsSpan(0, writer.BytesWritten));
        writer.Write(breakCrc ? crc ^ 1 : crc, 16);
        return buffer[..writer.BytesWritten];
    }
    private static void WriteCodedNumber(ref BitWriter writer, ulong value)
    {
        if (value < 0x80)
        {
            writer.Write((uint)value, 8);
            return;
        }

        var bytes = 2;
        while (bytes < 7 && value >= 1UL << ((5 * bytes) + 1))
        {
            bytes++;
        }

        var leadBits = 7 - bytes;
        var lead = (0xFF00u >> bytes) & 0xFF;
        writer.Write(lead | ((uint)(value >> (6 * (bytes - 1))) & ((1u << leadBits) - 1)), 8);
        for (var i = bytes - 2; i >= 0; i--)
        {
            writer.Write(0x80u | (uint)((value >> (6 * i)) & 0x3F), 8);
        }
    }

    private static (int Code, int ExtraBits) BlockCode(int size)
    {
        if (size == 192)
        {
            return (1, 0);
        }

        for (var code = 2; code <= 5; code++)
        {
            if (size == 576 << (code - 2))
            {
                return (code, 0);
            }
        }

        for (var code = 8; code <= 15; code++)
        {
            if (size == 256 << (code - 8))
            {
                return (code, 0);
            }
        }

        return size <= 256 ? (6, 8) : (7, 16);
    }

    private static (int Code, int Extra, int ExtraBits) RateCode(int rate) => rate switch
    {
        88200 => (1, 0, 0),
        176400 => (2, 0, 0),
        192000 => (3, 0, 0),
        8000 => (4, 0, 0),
        16000 => (5, 0, 0),
        22050 => (6, 0, 0),
        24000 => (7, 0, 0),
        32000 => (8, 0, 0),
        44100 => (9, 0, 0),
        48000 => (10, 0, 0),
        96000 => (11, 0, 0),
        _ when rate % 1000 == 0 && rate / 1000 <= 255 => (12, rate / 1000, 8),
        _ when rate <= 65535 => (13, rate, 16),
        _ => (14, rate / 10, 16),
    };

    internal static int SizeCode(int bits) => bits switch
    {
        8 => 1,
        12 => 2,
        16 => 4,
        20 => 5,
        24 => 6,
        32 => 7,
        _ => 0,
    };

    private static void WriteSubframe(ref BitWriter writer, long[] samples, int bits, FlacSubframePlan plan)
    {
        var wasted = 0;
        if (plan.UseWastedBits && samples.Any(s => s != 0))
        {
            wasted = samples.Where(s => s != 0).Min(s => System.Numerics.BitOperations.TrailingZeroCount(s));
            samples = samples.Select(s => s >> wasted).ToArray();
            bits -= wasted;
        }

        writer.Write(0, 1);
        var type = plan.Kind switch
        {
            FlacSubframeKind.Constant => 0,
            FlacSubframeKind.Verbatim => 1,
            FlacSubframeKind.Fixed => 8 + plan.Order,
            _ => 31 + plan.Order,
        };
        writer.Write((uint)type, 6);
        writer.WriteBit(wasted > 0);
        if (wasted > 0)
        {
            writer.WriteUnary((uint)(wasted - 1));
        }

        switch (plan.Kind)
        {
            case FlacSubframeKind.Constant:
                if (samples.Distinct().Count() != 1)
                {
                    throw new InvalidOperationException("A constant subframe needs equal samples.");
                }

                WriteSigned(ref writer, samples[0], bits);
                return;
            case FlacSubframeKind.Verbatim:
                foreach (var sample in samples)
                {
                    WriteSigned(ref writer, sample, bits);
                }

                return;
        }

        var order = plan.Order;
        for (var i = 0; i < order; i++)
        {
            WriteSigned(ref writer, samples[i], bits);
        }

        var residual = new long[samples.Length];
        if (plan.Kind == FlacSubframeKind.Fixed)
        {
            for (var i = order; i < samples.Length; i++)
            {
                residual[i] = samples[i] - FixedPrediction(samples, i, order);
            }
        }
        else
        {
            writer.Write((uint)(plan.Precision - 1), 4);
            writer.WriteSigned(plan.Shift, 5);
            foreach (var coefficient in plan.Coefficients)
            {
                writer.WriteSigned(coefficient, plan.Precision);
            }

            for (var i = order; i < samples.Length; i++)
            {
                long sum = 0;
                for (var j = 0; j < order; j++)
                {
                    sum += plan.Coefficients[j] * samples[i - 1 - j];
                }

                residual[i] = samples[i] - (sum >> plan.Shift);
            }
        }

        WriteResidual(ref writer, residual, order, plan);
    }

    private static long FixedPrediction(long[] s, int i, int order) => order switch
    {
        0 => 0,
        1 => s[i - 1],
        2 => (2 * s[i - 1]) - s[i - 2],
        3 => (3 * s[i - 1]) - (3 * s[i - 2]) + s[i - 3],
        _ => (4 * s[i - 1]) - (6 * s[i - 2]) + (4 * s[i - 3]) - s[i - 4],
    };

    private static void WriteResidual(ref BitWriter writer, long[] residual, int order, FlacSubframePlan plan)
    {
        writer.Write(plan.FiveBitParameters ? 1u : 0u, 2);
        writer.Write((uint)plan.PartitionOrder, 4);
        var parameterBits = plan.FiveBitParameters ? 5 : 4;
        var escape = (1u << parameterBits) - 1;
        var partitions = 1 << plan.PartitionOrder;
        var perPartition = residual.Length / partitions;
        for (var p = 0; p < partitions; p++)
        {
            var start = p == 0 ? order : p * perPartition;
            var values = residual.AsSpan(start, ((p + 1) * perPartition) - start);
            if (plan.Escape)
            {
                writer.Write(escape, parameterBits);
                // A signed field of w bits holds -2^(w-1) .. 2^(w-1) - 1; all-zero partitions need no bits.
                var width = 0;
                foreach (var value in values)
                {
                    if (value != 0)
                    {
                        width = Math.Max(width, 65 - System.Numerics.BitOperations.LeadingZeroCount((ulong)(value ^ (value >> 63))));
                    }
                }

                writer.Write((uint)width, 5);
                foreach (var value in values)
                {
                    if (width > 0)
                    {
                        writer.WriteSigned((int)value, width);
                    }
                }

                continue;
            }

            ulong sum = 0;
            foreach (var value in values)
            {
                sum += Fold(value);
            }

            var mean = values.Length == 0 ? 0 : sum / (ulong)values.Length;
            var k = 0;
            while (k < escape - 1 && 1UL << (k + 1) <= mean)
            {
                k++;
            }

            writer.Write((uint)k, parameterBits);
            foreach (var value in values)
            {
                var folded = Fold(value);
                writer.WriteUnary((uint)(folded >> k));
                writer.Write((uint)(folded & ((1UL << k) - 1)), k);
            }
        }
    }

    private static ulong Fold(long value) => (ulong)((value << 1) ^ (value >> 63));

    private static void WriteSigned(ref BitWriter writer, long value, int bits)
    {
        if (bits > 32)
        {
            writer.Write64((ulong)value, bits);
            return;
        }

        writer.Write((uint)value, bits);
    }

    private static void WriteLittleString(Stream stream, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(length, (uint)bytes.Length);
        stream.Write(length);
        stream.Write(bytes);
    }

    private static void WriteBig(Stream stream, uint value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
        stream.Write(bytes);
    }
}
