using System.Numerics;
using Rex.Media.Codecs.Mpeg;
using Rex.Media.Codecs.Software.Mpeg;
using Rex.Media.Primitives;

namespace Rex.Media.Tests.Codecs;

public sealed class MpegAudioHeaderTests
{
    /// <summary>Header bytes: version bits 0 (2.5), 2 (MPEG-2) or 3 (MPEG-1); layer bits 1 (III) to 3 (I).</summary>
    internal static byte[] Header(int versionBits, int layerBits, int bitrateIndex, int rateIndex, bool padding = false, bool crc = false, int mode = 1, int modeExtension = 0, int emphasis = 0) =>
    [
        0xFF,
        (byte)(0xE0 | (versionBits << 3) | (layerBits << 1) | (crc ? 0 : 1)),
        (byte)((bitrateIndex << 4) | (rateIndex << 2) | (padding ? 2 : 0)),
        (byte)((mode << 6) | (modeExtension << 4) | emphasis),
    ];

    private static MpegAudioHeader Parse(byte[] bytes)
    {
        Assert.True(MpegAudioHeader.TryParse(bytes, out var header));
        return header;
    }

    [Theory]
    [InlineData(3, 1, 9, 0, false, 417, 1152, 128, 44_100)]
    [InlineData(3, 1, 9, 0, true, 418, 1152, 128, 44_100)]
    [InlineData(3, 1, 14, 2, false, 1440, 1152, 320, 32_000)]
    [InlineData(3, 2, 11, 1, false, 672, 1152, 224, 48_000)]
    [InlineData(3, 3, 12, 1, true, 388, 384, 384, 48_000)]
    [InlineData(2, 1, 8, 0, false, 208, 576, 64, 22_050)]
    [InlineData(2, 2, 14, 1, false, 960, 1152, 160, 24_000)]
    [InlineData(2, 3, 14, 2, false, 768, 384, 256, 16_000)]
    [InlineData(0, 1, 1, 2, false, 72, 576, 8, 8000)]
    [InlineData(0, 1, 2, 1, false, 96, 576, 16, 12_000)]
    public void FrameLengthsFollowTheBitrateAndRateTables(int version, int layer, int bitrateIndex, int rateIndex, bool padding, int length, int samples, int kbps, int rate)
    {
        var header = Parse(Header(version, layer, bitrateIndex, rateIndex, padding));

        Assert.Equal(length, header.FrameLength);
        Assert.Equal(samples, header.SamplesPerFrame);
        Assert.Equal(kbps, header.BitrateKbps);
        Assert.Equal(rate, header.SampleRate);
        Assert.Equal(version != 3, header.IsLowSampleRate);
    }

    [Theory]
    [InlineData(3, 0, 44_100, 0)]
    [InlineData(3, 1, 48_000, 1)]
    [InlineData(3, 2, 32_000, 2)]
    [InlineData(2, 0, 22_050, 3)]
    [InlineData(2, 1, 24_000, 4)]
    [InlineData(2, 2, 16_000, 5)]
    [InlineData(0, 0, 11_025, 6)]
    [InlineData(0, 1, 12_000, 7)]
    [InlineData(0, 2, 8000, 8)]
    public void EveryRateHasItsBandTable(int version, int rateIndex, int rate, int tableIndex)
    {
        var header = Parse(Header(version, 1, 1, rateIndex));

        Assert.Equal(rate, header.SampleRate);
        Assert.Equal(tableIndex, header.SampleRateIndex);
    }

    [Fact]
    public void ChannelsSideInformationCrcAndCodecAreDerived()
    {
        var stereo = Parse(Header(3, 1, 9, 0, crc: true, mode: 1, modeExtension: 3));
        var mono = Parse(Header(3, 1, 9, 0, mode: 3));
        var lowStereo = Parse(Header(2, 1, 8, 0, mode: 2));
        var lowMono = Parse(Header(2, 1, 8, 0, mode: 3, emphasis: 1));

        Assert.Equal((2, 32, 6, MpegChannelMode.JointStereo, 3), (stereo.Channels, stereo.SideInfoLength, stereo.PayloadOffset, stereo.Mode, stereo.ModeExtension));
        Assert.True(stereo.HasCrc);
        Assert.Equal((1, 17, 4), (mono.Channels, mono.SideInfoLength, mono.PayloadOffset));
        Assert.Equal((2, 17, MpegChannelMode.DualChannel), (lowStereo.Channels, lowStereo.SideInfoLength, lowStereo.Mode));
        Assert.Equal((1, 9, 1), (lowMono.Channels, lowMono.SideInfoLength, lowMono.Emphasis));
        Assert.Equal(CodecId.Mp3, stereo.Codec);
        Assert.Equal(CodecId.Mp2, Parse(Header(3, 2, 9, 0)).Codec);
        Assert.Equal(CodecId.Mp1, Parse(Header(3, 3, 9, 0)).Codec);
        Assert.Equal(MpegVersion.Mpeg25, Parse(Header(0, 1, 1, 0)).Version);
    }

    [Fact]
    public void FreeFormatHasNoDeclaredLengthAndStreamsAreMatchedByVersionLayerAndRate()
    {
        var free = Parse(Header(3, 1, 0, 0, padding: true));

        Assert.Equal(0, free.FrameLength);
        Assert.Equal(418, free.FrameLengthAt(128_000));
        Assert.Equal(388, Parse(Header(3, 3, 0, 1, padding: true)).FrameLengthAt(384_000));
        Assert.True(free.SameStreamAs(Parse(Header(3, 1, 9, 0, mode: 3))));
        Assert.False(free.SameStreamAs(Parse(Header(3, 1, 9, 1))));
        Assert.False(free.SameStreamAs(Parse(Header(3, 2, 9, 0))));
        Assert.False(free.SameStreamAs(Parse(Header(2, 1, 9, 0))));
    }

    public static TheoryData<string, byte[]> Invalid => new()
    {
        { "too short", [0xFF, 0xFB, 0x90] },
        { "no sync", [0xFF, 0x1B, 0x90, 0x40] },
        { "reserved version", Header(1, 1, 9, 0) },
        { "reserved layer", Header(3, 0, 9, 0) },
        { "bad bitrate", Header(3, 1, 15, 0) },
        { "reserved rate", Header(3, 1, 9, 3) },
        { "reserved emphasis", Header(3, 1, 9, 0, emphasis: 2) },
    };

    [Theory]
    [MemberData(nameof(Invalid))]
    public void InvalidHeadersAreNotHeaders(string why, byte[] bytes)
    {
        Assert.False(MpegAudioHeader.TryParse(bytes, out _), why);
    }
}

public sealed class Layer3TableTests
{
    public static TheoryData<string, uint[]> CodeTables => new()
    {
        { "1", Layer3Tables.Pairs1 }, { "2", Layer3Tables.Pairs2 }, { "3", Layer3Tables.Pairs3 },
        { "5", Layer3Tables.Pairs5 }, { "6", Layer3Tables.Pairs6 }, { "7", Layer3Tables.Pairs7 },
        { "8", Layer3Tables.Pairs8 }, { "9", Layer3Tables.Pairs9 }, { "10", Layer3Tables.Pairs10 },
        { "11", Layer3Tables.Pairs11 }, { "12", Layer3Tables.Pairs12 }, { "13", Layer3Tables.Pairs13 },
        { "15", Layer3Tables.Pairs15 }, { "16", Layer3Tables.Pairs16 }, { "24", Layer3Tables.Pairs24 },
        { "A", Layer3Tables.QuadsA }, { "B", Layer3Tables.QuadsB },
    };

    /// <summary>
    /// A transcription error almost always breaks one of these: every code table of the standard is a
    /// complete prefix code, so its code lengths satisfy Kraft's equality and no code begins another.
    /// </summary>
    [Theory]
    [MemberData(nameof(CodeTables))]
    public void EveryCodeTableIsACompletePrefixCode(string name, uint[] entries)
    {
        var codes = entries.Select(e => (Length: (int)(e >> 24), Code: e & 0xFFFFFF)).ToList();
        var kraft = codes.Aggregate(BigInteger.Zero, (sum, c) => sum + (BigInteger.One << (32 - c.Length)));

        Assert.True(kraft == BigInteger.One << 32, $"Table {name} is not complete.");
        Assert.All(codes, c => Assert.True(c.Code >> c.Length == 0, $"Table {name} has a code longer than its length."));
        foreach (var a in codes)
        {
            foreach (var b in codes)
            {
                if (a != b && a.Length <= b.Length)
                {
                    Assert.False(b.Code >> (b.Length - a.Length) == a.Code, $"Table {name}: one code starts another.");
                }
            }
        }
    }

    [Fact]
    public void TheSynthesisWindowMirrorsAroundItsCentre()
    {
        Assert.Equal(512, Layer3Tables.Window.Length);
        Assert.Equal(0, Layer3Tables.Window[0]);
        for (var i = 1; i < 256; i++)
        {
            Assert.Equal(Math.Abs(Layer3Tables.Window[i]), Math.Abs(Layer3Tables.Window[512 - i]));
        }
    }

    [Fact]
    public void BandTablesCoverTheSpectrumInOrder()
    {
        Assert.All(Layer3Tables.LongBands, bands =>
        {
            Assert.Equal(23, bands.Length);
            Assert.Equal(576, bands[^1]);
            Assert.True(bands.Zip(bands.Skip(1)).All(p => p.First < p.Second));
        });
        Assert.All(Layer3Tables.ShortBands, bands =>
        {
            Assert.Equal(14, bands.Length);
            Assert.Equal(192, bands[^1]);
            Assert.True(bands.Zip(bands.Skip(1)).All(p => p.First < p.Second));
        });
        Assert.Equal(22, Layer3Tables.Pretab.Length);
        Assert.Equal(8, Layer3Tables.AliasCoefficients.Length);
    }
}
