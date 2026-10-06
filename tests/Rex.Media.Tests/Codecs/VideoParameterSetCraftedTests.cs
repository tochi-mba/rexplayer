using System.Numerics;
using Rex.Media.Codecs.H264;
using Rex.Media.Codecs.Hevc;
using Rex.Media.Codecs.Video;
using Rex.Media.Primitives;

namespace Rex.Media.Tests.Codecs;

/// <summary>Parameter sets written field by field, for the syntax encoders rarely produce.</summary>
public sealed class VideoParameterSetCraftedTests
{
    private static (long Value, int Count) U(long value, int count) => (value, count);

    /// <summary>ue(v): the value plus one, after as many zero bits as that has bits beyond the first.</summary>
    private static (long Value, int Count) Ue(long value)
    {
        var length = 63 - BitOperations.LeadingZeroCount((ulong)(value + 1));
        return (value + 1, (2 * length) + 1);
    }

    private static (long Value, int Count) Se(long value) => Ue(value > 0 ? (2 * value) - 1 : -2 * value);

    private static (long Value, int Count)[] Repeat((long Value, int Count) field, int times) => Enumerable.Repeat(field, times).ToArray();

    /// <summary>A NAL unit: the header bytes, then the fields with emulation prevention bytes inserted.</summary>
    private static byte[] Unit(byte[] header, params (long Value, int Count)[][] parts)
    {
        var payload = AacConfigTests.Bits([.. parts.SelectMany(p => p)]);
        var escaped = new List<byte>(header);
        var zeros = 0;
        foreach (var b in payload)
        {
            if (zeros >= 2 && b <= 3)
            {
                escaped.Add(3);
                zeros = 0;
            }

            escaped.Add(b);
            zeros = b == 0 ? zeros + 1 : 0;
        }

        return [.. escaped];
    }

    [Fact]
    public void AnInterlacedMainProfileSpsWithPictureOrderCyclesAndAnExplicitAspect()
    {
        var sps = Unit(
            [0x67],
            [U(77, 8), U(0, 8), U(30, 8), Ue(0), Ue(0), Ue(1), U(0, 1), Se(-1), Se(2), Ue(2), Se(1), Se(-3)],
            [Ue(4), U(0, 1), Ue(9), Ue(4), U(0, 1), U(1, 1), U(1, 1), U(1, 1), Ue(1), Ue(1), Ue(0), Ue(2)],
            [U(1, 1), U(1, 1), U(255, 8), U(4, 16), U(3, 16), U(1, 1), U(0, 1), U(1, 1), U(5, 3), U(1, 1), U(0, 1), U(1, 1), Ue(1), Ue(1), U(1, 1), U(0, 32), U(50, 32)]);

        Assert.True(H264Sps.TryParse(sps, out var info));

        Assert.Equal((77, 30, 156, 152, 1, 8), (info.Profile, info.Level, info.Width, info.Height, info.ChromaFormat, info.BitDepth));
        Assert.True(info.Interlaced);
        Assert.Equal(new Rational(4, 3), info.PixelAspect);
        Assert.True(info.Color.FullRange);
        Assert.Equal(ColorPrimaries.Unspecified, info.Color.Primaries);
        Assert.Null(info.FrameRate);
    }

    [Fact]
    public void AHigh444SpsWithSeparatePlanesAndScalingLists()
    {
        var sps = Unit(
            [0x67],
            [U(244, 8), U(0, 8), U(40, 8), Ue(0), Ue(3), U(1, 1), Ue(2), Ue(2), U(0, 1), U(1, 1)],
            [U(1, 1), Se(-8), .. Repeat(U(0, 1), 5), U(1, 1), .. Repeat(Se(0), 64), .. Repeat(U(0, 1), 5)],
            [Ue(0), Ue(2), Ue(1), U(0, 1), Ue(3), Ue(2), U(1, 1), U(0, 1), U(1, 1), Ue(1), Ue(1), Ue(1), Ue(0)],
            [U(1, 1), U(1, 1), U(17, 8), U(0, 1), U(0, 1), U(0, 1), U(0, 1)]);

        Assert.True(H264Sps.TryParse(sps, out var info));

        Assert.Equal((62, 47, 3, 10), (info.Width, info.Height, info.ChromaFormat, info.BitDepth));
        Assert.False(info.Interlaced);
        Assert.Equal(new Rational(1, 1), info.PixelAspect);
        Assert.Null(info.FrameRate);
    }

    [Fact]
    public void H264UnitsThatAreNotAReadableSpsAreRefused()
    {
        var plain = Unit([0x67], [U(66, 8), U(0, 8), U(10, 8), Ue(0), Ue(0), Ue(2), Ue(1), U(0, 1), Ue(3), Ue(2), U(1, 1), U(0, 1), U(0, 1), U(0, 1)]);
        var cropAway = Unit([0x67], [U(66, 8), U(0, 8), U(10, 8), Ue(0), Ue(0), Ue(2), Ue(1), U(0, 1), Ue(0), Ue(0), U(1, 1), U(0, 1), U(1, 1), Ue(8), Ue(0), Ue(0), Ue(0), U(0, 1)]);

        Assert.True(H264Sps.TryParse(plain, out var info));
        Assert.Equal((64, 48), (info.Width, info.Height));
        Assert.False(H264Sps.TryParse([0x68, .. plain[1..]], out _));
        Assert.False(H264Sps.TryParse(plain.AsSpan(0, 3), out _));
        Assert.False(H264Sps.TryParse(plain.AsSpan(0, 6), out _));
        Assert.False(H264Sps.TryParse(cropAway, out _));
    }

    private static readonly (long Value, int Count)[] PlainHevcStart =
    [
        U(0, 4), U(0, 3), U(1, 1), U(0, 3), U(1, 5), U(0, 32), U(0, 48), U(60, 8),
        Ue(0), Ue(1), Ue(64), Ue(48), U(0, 1), Ue(0), Ue(0), Ue(4), U(1, 1), Ue(0), Ue(0), Ue(0),
        .. Enumerable.Repeat(Ue(0), 6), U(0, 1), U(0, 2), U(0, 1),
    ];

    [Fact]
    public void AnHevcSpsWithSubLayersScalingListsPcmPredictedReferenceSetsAndFields()
    {
        var sps = Unit(
            [0x42, 0x01],
            [U(0, 4), U(2, 3), U(1, 1), U(0, 2), U(0, 1), U(4, 5), U(0, 32), U(0, 48), U(93, 8), U(1, 1), U(1, 1), U(0, 1), U(1, 1), U(0, 12), U(0, 44), U(0, 44), U(0, 8), U(0, 8)],
            [Ue(0), Ue(0), Ue(64), Ue(48), U(1, 1), Ue(1), Ue(2), Ue(3), Ue(1), Ue(4), Ue(4), Ue(4), U(0, 1), Ue(4), Ue(0), Ue(0), .. Repeat(Ue(0), 6)],
            [U(1, 1), U(1, 1)],
            [U(1, 1), .. Repeat(Se(1), 16), .. Enumerable.Range(0, 5).SelectMany(_ => new[] { U(0, 1), Ue(0) })],
            [.. Enumerable.Range(0, 6).SelectMany(_ => new[] { U(0, 1), Ue(0) })],
            [U(1, 1), Se(-2), .. Repeat(Se(0), 64), .. Enumerable.Range(0, 5).SelectMany(_ => new[] { U(0, 1), Ue(0) })],
            [U(0, 1), Ue(0), U(1, 1), Se(3), .. Repeat(Se(0), 64)],
            [U(0, 2), U(1, 1), U(0, 8), Ue(0), Ue(0), U(0, 1)],
            [Ue(3), Ue(2), Ue(1), Ue(0), U(1, 1), Ue(1), U(0, 1), Ue(5), U(1, 1)],
            [U(1, 1), U(0, 1), Ue(0), U(1, 1), U(0, 1), U(1, 1), U(0, 1), U(0, 1), U(1, 1)],
            [U(1, 1), U(1, 1), Ue(2), U(0, 1), U(0, 1), U(1, 1), U(0, 1), U(1, 1), U(0, 1), U(0, 1)],
            [U(1, 1), Ue(2), U(0, 9), U(1, 9), U(0, 2)],
            [U(1, 1), U(1, 1), U(1, 8), U(0, 1), U(1, 1), U(5, 3), U(0, 1), U(1, 1), U(9, 8), U(18, 8), U(9, 8), U(0, 1)],
            [U(0, 1), U(1, 1), U(0, 1), U(1, 1), Ue(0), Ue(0), Ue(0), Ue(0), U(1, 1), U(1, 32), U(60, 32)]);

        Assert.True(HevcSps.TryParse(sps, out var info));

        Assert.Equal((4, 93, 61, 44, 0, 12), (info.Profile, info.Level, info.Width, info.Height, info.ChromaFormat, info.BitDepth));
        Assert.True(info.Interlaced);
        Assert.Equal((ColorPrimaries.Bt2020, ColorTransfer.Hlg, false), (info.Color.Primaries, info.Color.Transfer, info.Color.FullRange));
        Assert.Equal(new Rational(60, 1), info.FrameRate);
    }

    [Fact]
    public void HevcUnitsThatAreNotAReadableSpsAreRefused()
    {
        var plain = Unit([0x42, 0x01], PlainHevcStart, [Ue(0), U(0, 1), U(0, 2), U(0, 1)]);
        var tooManySets = Unit([0x42, 0x01], PlainHevcStart, [Ue(65)]);
        var tooManyPictures = Unit([0x42, 0x01], PlainHevcStart, [Ue(1), Ue(17), Ue(0)]);
        var noTiming = Unit([0x42, 0x01], PlainHevcStart, [Ue(0), U(0, 1), U(0, 2), U(1, 1), U(0, 4), U(0, 1), U(0, 4), U(0, 1)]);

        Assert.True(HevcSps.TryParse(plain, out var info));
        Assert.Equal((64, 48, 1, 60), (info.Width, info.Height, info.Profile, info.Level));
        Assert.True(HevcSps.TryParse(noTiming, out var untimed));
        Assert.Null(untimed.FrameRate);
        Assert.False(HevcSps.TryParse([0x40, 0x01, .. plain[2..]], out _));
        Assert.False(HevcSps.TryParse(plain.AsSpan(0, 3), out _));
        Assert.False(HevcSps.TryParse(plain.AsSpan(0, 8), out _));
        Assert.False(HevcSps.TryParse(tooManySets, out _));
        Assert.False(HevcSps.TryParse(tooManyPictures, out _));
    }

    [Fact]
    public void AvcRecordsThatAreMalformedAreRefused()
    {
        byte[] good = [1, 100, 0, 30, 0xFF, 0xE1, 0, 2, 0x67, 1, 1, 0, 2, 0x68, 2];

        var config = AvcConfig.Parse(good);

        Assert.Equal((4, 100, 30), (config.LengthSize, config.Profile, config.Level));
        Assert.Null(config.Sequence());
        Assert.Null(new AvcConfig().Sequence());
        Assert.Throws<MediaFormatException>(() => AvcConfig.Parse(good.AsSpan(0, 6)));
        Assert.Throws<MediaFormatException>(() => AvcConfig.Parse([2, .. good[1..]]));
        Assert.Throws<MediaFormatException>(() => AvcConfig.Parse([1, 100, 0, 30, 0xFE, .. good[5..]]));
        Assert.Throws<MediaFormatException>(() => AvcConfig.Parse(good.AsSpan(0, 10)));
        Assert.Throws<MediaFormatException>(() => AvcConfig.Parse(good.AsSpan(0, 9)));
        Assert.Throws<MediaFormatException>(() => AvcConfig.Parse(good.AsSpan(0, 7)));
        Assert.Equal(1, AvcConfig.Parse([1, 66, 0, 10, 0xFC, 0xE0, 0]).LengthSize);
    }

    private static byte[] HevcRecord(byte lengthByte, params byte[][] arrays)
    {
        var fixedPart = new byte[23];
        fixedPart[0] = 1;
        fixedPart[1] = 2;
        fixedPart[12] = 93;
        fixedPart[21] = lengthByte;
        fixedPart[22] = (byte)arrays.Length;
        return [.. fixedPart, .. arrays.SelectMany(a => a)];
    }

    [Fact]
    public void HevcRecordsThatAreMalformedAreRefused()
    {
        byte[] seiOnly = [0x27, 0, 1, 0, 2, 0x4E, 0x01];
        byte[] badSps = [0x21, 0, 1, 0, 3, 0x42, 0x01, 0x01];

        var config = HevcConfig.Parse(HevcRecord(0x0F, seiOnly, badSps));

        Assert.Equal((4, 2, 93), (config.LengthSize, config.Profile, config.Level));
        Assert.Equal(2, config.Units.Count);
        Assert.Null(config.Sequence());
        Assert.Null(HevcConfig.Parse(HevcRecord(0x0F, seiOnly, [0x22, 0, 1, 0, 0])).Sequence());
        Assert.Equal(2, HevcConfig.Parse(HevcRecord(0x0D)).LengthSize);
        Assert.Throws<MediaFormatException>(() => HevcConfig.Parse(new byte[22]));
        Assert.Throws<MediaFormatException>(() => HevcConfig.Parse([2, .. HevcRecord(0x0F)[1..]]));
        Assert.Throws<MediaFormatException>(() => HevcConfig.Parse(HevcRecord(0x0E)));
        Assert.Throws<MediaFormatException>(() => HevcConfig.Parse(HevcRecord(0x0F, [0x20, 0])));
        Assert.Throws<MediaFormatException>(() => HevcConfig.Parse(HevcRecord(0x0F, [0x20, 0, 1, 0])));
        Assert.Throws<MediaFormatException>(() => HevcConfig.Parse(HevcRecord(0x0F, [0x20, 0, 1, 0, 5, 1])));
    }

    [Fact]
    public void AnnexBStreamsSplitAtThreeAndFourByteStartCodes()
    {
        byte[] stream = [0, 0, 0, 1, 0x67, 1, 0, 0, 0, 0, 1, 0x68, 2, 0, 0, 1, 0x65, 3, 0];

        var units = NalUnits.SplitAnnexB(stream).Select(range => stream[range]).ToList();

        Assert.Equal([[0x67, 1], [0x68, 2], [0x65, 3, 0]], units);
        Assert.Empty(NalUnits.SplitAnnexB([1, 2, 3, 4]));
        Assert.Empty(NalUnits.SplitAnnexB([]));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    public void LengthPrefixedSamplesBecomeAnnexB(int lengthSize)
    {
        byte[] Prefixed(byte[] unit) => [.. new byte[lengthSize - 1], (byte)unit.Length, .. unit];
        byte[] sample = [.. Prefixed([0x65, 1, 2]), .. Prefixed([0x06, 3])];

        var plain = NalUnits.ToAnnexB(sample, lengthSize);
        var withSets = NalUnits.ToAnnexB(sample, lengthSize, NalUnits.JoinAnnexB([[0x67, 9], [0x68, 8]]));

        Assert.Equal([0, 0, 0, 1, 0x65, 1, 2, 0, 0, 0, 1, 0x06, 3], plain);
        Assert.Equal([0, 0, 0, 1, 0x67, 9, 0, 0, 0, 1, 0x68, 8, .. plain], withSets);
    }

    [Fact]
    public void ALengthRunningPastTheSampleIsCutThere()
    {
        var units = NalUnits.SplitLengthPrefixed([0, 0, 0, 9, 0x65, 1, 0, 0], 4);

        Assert.Equal([new Range(4, 8)], units);
        Assert.Empty(NalUnits.SplitLengthPrefixed([0, 0, 1], 4));
        Assert.Throws<ArgumentOutOfRangeException>(() => NalUnits.SplitLengthPrefixed([], 3));
        Assert.Throws<ArgumentNullException>(() => NalUnits.JoinAnnexB(null!));
    }

    [Theory]
    [InlineData(new byte[] { 0, 0, 3, 1 }, new byte[] { 0, 0, 1 })]
    [InlineData(new byte[] { 0, 0, 3, 3 }, new byte[] { 0, 0, 3 })]
    [InlineData(new byte[] { 0, 0, 0, 3, 0, 0, 3 }, new byte[] { 0, 0, 0, 0, 0 })]
    [InlineData(new byte[] { 0, 3, 0, 3 }, new byte[] { 0, 3, 0, 3 })]
    public void EmulationPreventionBytesAreRemoved(byte[] escaped, byte[] payload)
    {
        Assert.Equal(payload, NalUnits.Unescape(escaped));
    }
}
