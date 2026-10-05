using Rex.Media.Codecs.Mpeg;
using Rex.Media.Codecs.Software.Mpeg;
using Rex.Media.Primitives;

namespace Rex.Media.Tests.Codecs;

/// <summary>Everything a test chooses about one granule of one channel of a Layer III frame.</summary>
internal sealed class Layer3Channel
{
    public int GlobalGain { get; set; } = 210;

    public int ScalefacCompress { get; set; }

    public bool WindowSwitching { get; set; }

    public int BlockType { get; set; }

    public bool Mixed { get; set; }

    public int[] TableSelect { get; set; } = [24, 24, 24];

    public int Region0Count { get; set; } = 15;

    public int Region1Count { get; set; } = 7;

    public int[] SubblockGain { get; } = new int[3];

    public bool Preflag { get; set; }

    public bool ScalefacScale { get; set; }

    public bool Count1TableB { get; set; }

    /// <summary>The quantized spectrum, in bitstream order (short blocks band by band, window by window).</summary>
    public int[] Values { get; } = new int[576];

    /// <summary>Lines from here on are coded as count1 quadruples (values -1, 0 or 1); the rest are big values.</summary>
    public int? Count1Start { get; set; }

    public int[] ScalefacLong { get; } = new int[22];

    public int[] ScalefacShort { get; } = new int[39];

    /// <summary>For the lower sampling frequencies: the four slen values and partition sizes the compress value implies.</summary>
    public int[] LowRateSlen { get; set; } = [0, 0, 0, 0];

    public int[] LowRatePartitions { get; set; } = [6, 5, 5, 5];

    public int? BigValuesOverride { get; set; }

    public int? Part23Override { get; set; }

    public static Layer3Channel Short(bool mixed = false) => new() { WindowSwitching = true, BlockType = 2, Mixed = mixed, TableSelect = [24, 24, 0] };
}

/// <summary>
/// Writes Layer III frames field by field for decoder tests: the paths an encoder rarely or never
/// takes (intensity stereo, mixed blocks, the lower-rate intensity tables) and malformed fields.
/// Main data starts in the frame itself unless a test says otherwise.
/// </summary>
internal static class Layer3FrameWriter
{
    private static readonly int[] Linbits = [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 2, 3, 4, 6, 8, 10, 13, 4, 5, 6, 7, 8, 9, 11, 13];
    private static readonly int[] Slen1 = [0, 0, 0, 0, 3, 1, 1, 1, 2, 2, 2, 3, 3, 3, 4, 4];
    private static readonly int[] Slen2 = [0, 1, 2, 3, 0, 1, 2, 3, 1, 2, 3, 1, 2, 3, 2, 3];

    /// <summary>A frame with header <paramref name="header"/> (two CRC bytes follow it when it says so).</summary>
    public static byte[] Frame(byte[] header, Layer3Channel[][] granules, bool[][]? scfsi = null, int mainDataBegin = 0)
    {
        Assert.True(MpegAudioHeader.TryParse(header, out var parsed));
        var lsf = parsed.IsLowSampleRate;
        var channels = parsed.Channels;
        var main = new byte[4096];
        var writer = new BitWriter(main);
        var lengths = new int[2, 2];
        var bigValues = new int[2, 2];
        for (var gr = 0; gr < granules.Length; gr++)
        {
            for (var ch = 0; ch < channels; ch++)
            {
                var g = granules[gr][ch];
                var start = writer.BitPosition;
                if (lsf)
                {
                    WriteLowRateScalefactors(ref writer, g);
                }
                else
                {
                    WriteScalefactors(ref writer, g, gr == 1 && scfsi is not null ? scfsi[ch] : null);
                }

                bigValues[gr, ch] = WriteSpectrum(ref writer, g, parsed);
                lengths[gr, ch] = g.Part23Override ?? (int)(writer.BitPosition - start);
            }
        }

        writer.AlignToByte();
        var mainBytes = main[..writer.BytesWritten];
        var side = new byte[parsed.SideInfoLength];
        var sideWriter = new BitWriter(side);
        sideWriter.Write((uint)mainDataBegin, lsf ? 8 : 9);
        sideWriter.Write(0, lsf ? channels : (channels == 1 ? 5 : 3));
        if (!lsf)
        {
            for (var ch = 0; ch < channels; ch++)
            {
                for (var group = 0; group < 4; group++)
                {
                    sideWriter.WriteBit(scfsi?[ch][group] ?? false);
                }
            }
        }

        for (var gr = 0; gr < granules.Length; gr++)
        {
            for (var ch = 0; ch < channels; ch++)
            {
                var g = granules[gr][ch];
                sideWriter.Write((uint)lengths[gr, ch], 12);
                sideWriter.Write((uint)(g.BigValuesOverride ?? bigValues[gr, ch]), 9);
                sideWriter.Write((uint)g.GlobalGain, 8);
                sideWriter.Write((uint)g.ScalefacCompress, lsf ? 9 : 4);
                sideWriter.WriteBit(g.WindowSwitching);
                if (g.WindowSwitching)
                {
                    sideWriter.Write((uint)g.BlockType, 2);
                    sideWriter.WriteBit(g.Mixed);
                    sideWriter.Write((uint)g.TableSelect[0], 5);
                    sideWriter.Write((uint)g.TableSelect[1], 5);
                    foreach (var gain in g.SubblockGain)
                    {
                        sideWriter.Write((uint)gain, 3);
                    }
                }
                else
                {
                    foreach (var table in g.TableSelect)
                    {
                        sideWriter.Write((uint)table, 5);
                    }

                    sideWriter.Write((uint)g.Region0Count, 4);
                    sideWriter.Write((uint)g.Region1Count, 3);
                }

                if (!lsf)
                {
                    sideWriter.WriteBit(g.Preflag);
                }

                sideWriter.WriteBit(g.ScalefacScale);
                sideWriter.WriteBit(g.Count1TableB);
            }
        }

        var frame = new byte[parsed.FrameLength];
        header.CopyTo(frame, 0);
        side.CopyTo(frame, parsed.PayloadOffset);
        var mainAt = parsed.PayloadOffset + side.Length;
        Assert.True(mainAt + mainBytes.Length <= frame.Length, "The test frame's data does not fit its bitrate.");
        mainBytes.CopyTo(frame, mainAt);
        return frame;
    }

    private static void WriteScalefactors(ref BitWriter writer, Layer3Channel g, bool[]? reuse)
    {
        var slen1 = Slen1[g.ScalefacCompress];
        var slen2 = Slen2[g.ScalefacCompress];
        if (g.WindowSwitching && g.BlockType == 2)
        {
            if (g.Mixed)
            {
                for (var sfb = 0; sfb < 8; sfb++)
                {
                    writer.Write((uint)g.ScalefacLong[sfb], slen1);
                }
            }

            for (var sfb = g.Mixed ? 3 : 0; sfb < 12; sfb++)
            {
                for (var window = 0; window < 3; window++)
                {
                    writer.Write((uint)g.ScalefacShort[(sfb * 3) + window], sfb < 6 ? slen1 : slen2);
                }
            }

            return;
        }

        int[] groups = [0, 6, 11, 16, 21];
        for (var group = 0; group < 4; group++)
        {
            if (reuse?[group] == true)
            {
                continue;
            }

            for (var sfb = groups[group]; sfb < groups[group + 1]; sfb++)
            {
                writer.Write((uint)g.ScalefacLong[sfb], group < 2 ? slen1 : slen2);
            }
        }
    }

    private static void WriteLowRateScalefactors(ref BitWriter writer, Layer3Channel g)
    {
        var shortBlocks = g.WindowSwitching && g.BlockType == 2;
        var slot = 0;
        for (var partition = 0; partition < 4; partition++)
        {
            for (var n = 0; n < g.LowRatePartitions[partition]; n++, slot++)
            {
                int value;
                if (!shortBlocks || (g.Mixed && slot < 6))
                {
                    value = g.ScalefacLong[slot];
                }
                else
                {
                    value = g.ScalefacShort[g.Mixed ? slot - 6 + 9 : slot];
                }

                writer.Write((uint)value, g.LowRateSlen[partition]);
            }
        }
    }

    /// <summary>Huffman-codes the spectrum; returns the number of big-value pairs.</summary>
    private static int WriteSpectrum(ref BitWriter writer, Layer3Channel g, MpegAudioHeader header)
    {
        var longBands = Layer3Tables.LongBands[header.SampleRateIndex];
        var shortBands = Layer3Tables.ShortBands[header.SampleRateIndex];
        var lastNonzero = Array.FindLastIndex(g.Values, v => v != 0);
        var count1Start = g.Count1Start ?? ((lastNonzero + 2) & ~1);
        var bigValues = count1Start / 2;
        int region1, region2;
        if (g.WindowSwitching)
        {
            region1 = g.BlockType == 2 && !g.Mixed ? shortBands[3] * 3 : longBands[8];
            region2 = 576;
        }
        else
        {
            region1 = longBands[Math.Min(g.Region0Count + 1, 22)];
            region2 = longBands[Math.Min(g.Region0Count + g.Region1Count + 2, 22)];
        }

        for (var i = 0; i < count1Start; i += 2)
        {
            var table = g.TableSelect[i < region1 ? 0 : i < region2 ? 1 : 2];
            if (table == 0)
            {
                Assert.True(g.Values[i] == 0 && g.Values[i + 1] == 0, "A region coded with table 0 must be silent.");
                continue;
            }

            WritePair(ref writer, table, g.Values[i], g.Values[i + 1]);
        }

        var quads = g.Count1TableB ? Layer3Tables.QuadsB : Layer3Tables.QuadsA;
        for (var i = count1Start; i + 4 <= 576 && i <= lastNonzero; i += 4)
        {
            var symbol = 0;
            for (var k = 0; k < 4; k++)
            {
                Assert.InRange(g.Values[i + k], -1, 1);
                symbol = (symbol << 1) | Math.Abs(g.Values[i + k]);
            }

            WriteCode(ref writer, quads[symbol]);
            for (var k = 0; k < 4; k++)
            {
                if (g.Values[i + k] != 0)
                {
                    writer.WriteBit(g.Values[i + k] < 0);
                }
            }
        }

        return bigValues;
    }

    private static void WritePair(ref BitWriter writer, int table, int x, int y)
    {
        var (entries, size) = table switch
        {
            1 => (Layer3Tables.Pairs1, 2),
            2 => (Layer3Tables.Pairs2, 3),
            3 => (Layer3Tables.Pairs3, 3),
            5 => (Layer3Tables.Pairs5, 4),
            6 => (Layer3Tables.Pairs6, 4),
            7 => (Layer3Tables.Pairs7, 6),
            8 => (Layer3Tables.Pairs8, 6),
            9 => (Layer3Tables.Pairs9, 6),
            10 => (Layer3Tables.Pairs10, 8),
            11 => (Layer3Tables.Pairs11, 8),
            12 => (Layer3Tables.Pairs12, 8),
            13 => (Layer3Tables.Pairs13, 16),
            15 => (Layer3Tables.Pairs15, 16),
            >= 24 => (Layer3Tables.Pairs24, 16),
            _ => (Layer3Tables.Pairs16, 16),
        };
        var linbits = Linbits[table];
        var codedX = Math.Min(Math.Abs(x), size - 1);
        var codedY = Math.Min(Math.Abs(y), size - 1);
        WriteCode(ref writer, entries[(codedX * size) + codedY]);
        WriteValueTail(ref writer, x, codedX, linbits);
        WriteValueTail(ref writer, y, codedY, linbits);
    }

    private static void WriteValueTail(ref BitWriter writer, int value, int coded, int linbits)
    {
        if (coded == 15 && linbits > 0)
        {
            writer.Write((uint)(Math.Abs(value) - 15), linbits);
        }

        if (value != 0)
        {
            writer.WriteBit(value < 0);
        }
    }

    private static void WriteCode(ref BitWriter writer, uint entry) => writer.Write(entry & 0xFFFFFF, (int)(entry >> 24));
}
