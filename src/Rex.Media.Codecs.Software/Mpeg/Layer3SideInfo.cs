// Spec: ISO/IEC 11172-3 clause 2.4.1.7 and 2.4.2.7 (Layer III side information); ISO/IEC 13818-3 clause 2.4.1.7 (one granule, 8-bit main_data_begin, 9-bit scalefac_compress).
using Rex.Media.Codecs.Mpeg;
using Rex.Media.Primitives;

namespace Rex.Media.Codecs.Software.Mpeg;

/// <summary>How one granule of one channel is coded: the side information for it.</summary>
internal sealed class GranuleInfo
{
    public int Part23Length { get; set; }

    public int BigValues { get; set; }

    public int GlobalGain { get; set; }

    public int ScalefacCompress { get; set; }

    public bool WindowSwitching { get; set; }

    /// <summary>0 normal, 1 start, 2 short (three windows), 3 stop.</summary>
    public int BlockType { get; set; }

    public bool MixedBlock { get; set; }

    public int[] TableSelect { get; } = new int[3];

    public int[] SubblockGain { get; } = new int[3];

    /// <summary>The first spectral line of region 1 and of region 2 of the big values.</summary>
    public int Region1Start { get; set; }

    public int Region2Start { get; set; }

    public bool Preflag { get; set; }

    public bool ScalefacScale { get; set; }

    public bool Count1TableB { get; set; }

    /// <summary>Whether all of the granule's lines are in three short windows (mixed blocks only above 36).</summary>
    public bool IsShort => WindowSwitching && BlockType == 2;
}

/// <summary>The side information of one frame: where its main data starts and how each granule is coded.</summary>
internal sealed class Layer3SideInfo
{
    public int MainDataBegin { get; private set; }

    /// <summary>Scalefactor selection information: [channel][group] reuse of granule 0's scalefactors (MPEG-1 only).</summary>
    public bool[][] Scfsi { get; } = [new bool[4], new bool[4]];

    public GranuleInfo[][] Granules { get; } = [[new(), new()], [new(), new()]];

    public void Read(ReadOnlySpan<byte> data, MpegAudioHeader header)
    {
        var reader = new BitReader(data);
        var channels = header.Channels;
        var lsf = header.IsLowSampleRate;
        MainDataBegin = (int)reader.ReadBits(lsf ? 8 : 9);
        reader.SkipBits(lsf ? channels : (channels == 1 ? 5 : 3));
        if (!lsf)
        {
            for (var ch = 0; ch < channels; ch++)
            {
                for (var group = 0; group < 4; group++)
                {
                    Scfsi[ch][group] = reader.ReadBit();
                }
            }
        }

        var bands = Layer3Tables.LongBands[header.SampleRateIndex];
        var shortBands = Layer3Tables.ShortBands[header.SampleRateIndex];
        for (var gr = 0; gr < (lsf ? 1 : 2); gr++)
        {
            for (var ch = 0; ch < channels; ch++)
            {
                var g = Granules[gr][ch];
                g.Part23Length = (int)reader.ReadBits(12);
                g.BigValues = (int)reader.ReadBits(9);
                if (g.BigValues > 288)
                {
                    throw new MediaFormatException("A Layer III granule claims more than 576 big values.");
                }

                g.GlobalGain = (int)reader.ReadBits(8);
                g.ScalefacCompress = (int)reader.ReadBits(lsf ? 9 : 4);
                g.WindowSwitching = reader.ReadBit();
                if (g.WindowSwitching)
                {
                    g.BlockType = (int)reader.ReadBits(2);
                    g.MixedBlock = reader.ReadBit();
                    g.TableSelect[0] = (int)reader.ReadBits(5);
                    g.TableSelect[1] = (int)reader.ReadBits(5);
                    g.TableSelect[2] = 0;
                    for (var window = 0; window < 3; window++)
                    {
                        g.SubblockGain[window] = (int)reader.ReadBits(3);
                    }

                    if (g.BlockType == 0)
                    {
                        throw new MediaFormatException("A Layer III granule switches windows to the normal block type.");
                    }

                    // Region 0 is implicit: the first 36 lines of short blocks (three short bands),
                    // otherwise the first eight long bands; region 1 runs to the end.
                    g.Region1Start = g.BlockType == 2 && !g.MixedBlock ? shortBands[3] * 3 : bands[8];
                    g.Region2Start = 576;
                }
                else
                {
                    g.BlockType = 0;
                    g.MixedBlock = false;
                    for (var region = 0; region < 3; region++)
                    {
                        g.TableSelect[region] = (int)reader.ReadBits(5);
                    }

                    var region0Count = (int)reader.ReadBits(4);
                    var region1Count = (int)reader.ReadBits(3);
                    g.Region1Start = bands[Math.Min(region0Count + 1, 22)];
                    g.Region2Start = bands[Math.Min(region0Count + region1Count + 2, 22)];
                }

                g.Preflag = !lsf && reader.ReadBit();
                g.ScalefacScale = reader.ReadBit();
                g.Count1TableB = reader.ReadBit();
            }
        }
    }
}
