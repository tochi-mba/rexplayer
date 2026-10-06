using Rex.Media.AppCore;
using Rex.Media.Codecs.Aac;
using Rex.Media.IO;
using Rex.Media.Primitives;
using Rex.Media.TestKit;

namespace Rex.Media.Tests.Codecs;

public sealed class AacConfigTests
{
    /// <summary>Packs fields of the given widths, most significant bit first.</summary>
    internal static byte[] Bits(params (long Value, int Count)[] fields)
    {
        var buffer = new byte[(fields.Sum(f => f.Count) + 7) / 8];
        var writer = new BitWriter(buffer);
        foreach (var (value, count) in fields)
        {
            writer.Write64((ulong)value, count);
        }

        return buffer;
    }

    private static AacConfig Parse(params (long Value, int Count)[] fields)
    {
        Assert.True(AacConfig.TryParse(Bits(fields), out var config));
        return config;
    }

    [Theory]
    [InlineData("mp4/h264-aac.mp4")]
    [InlineData("mkv/h264-aac-subtitles.mkv")]
    public void AnEncodersConfigReadsAsItWasWritten(string fixture)
    {
        using var source = new MemoryByteSource(File.ReadAllBytes(RepoPaths.Combine($"tests/fixtures/{fixture}")), fixture);
        using var demuxer = MediaRegistries.Demuxers().Probe(source, CancellationToken.None)!.Open(source, CancellationToken.None);
        var track = demuxer.Info.FirstTrack(MediaKind.Audio)!;

        Assert.True(AacConfig.TryParse(track.CodecPrivate, out var config));

        Assert.Equal((2, 48_000, 1, false), (config.ObjectType, config.SampleRate, config.Channels, config.Sbr));
    }

    [Fact]
    public void APlainLowComplexityConfigNamesItsRateAndChannels()
    {
        var config = Parse((2, 5), (4, 4), (2, 4), (0, 3));

        Assert.Equal((2, 44_100, 2, 44_100, 1024), (config.ObjectType, config.SampleRate, config.Channels, config.OutputSampleRate, config.FrameLength));
        Assert.False(config.Sbr);
        Assert.False(config.Ps);
        Assert.Equal(2, config.OutputChannels);
    }

    [Fact]
    public void ExplicitSpectralBandReplicationDoublesTheOutputRate()
    {
        var heAac = Parse((5, 5), (6, 4), (2, 4), (3, 4), (2, 5), (0, 3));
        var heAacV2 = Parse((29, 5), (6, 4), (1, 4), (3, 4), (2, 5), (0, 3));

        Assert.Equal((2, 24_000, 48_000, true, false), (heAac.ObjectType, heAac.SampleRate, heAac.OutputSampleRate, heAac.Sbr, heAac.Ps));
        Assert.Equal((true, true, 1, 2), (heAacV2.Sbr, heAacV2.Ps, heAacV2.Channels, heAacV2.OutputChannels));
    }

    [Fact]
    public void BackwardCompatibleSignallingFollowsTheCoreConfig()
    {
        var sbr = Parse((2, 5), (6, 4), (1, 4), (0, 3), (0x2B7, 11), (5, 5), (1, 1), (3, 4));
        var ps = Parse((2, 5), (6, 4), (1, 4), (0, 3), (0x2B7, 11), (5, 5), (1, 1), (3, 4), (0x548, 11), (1, 1));
        var noSbr = Parse((2, 5), (6, 4), (1, 4), (0, 3), (0x2B7, 11), (5, 5), (0, 1));
        var otherExtension = Parse((2, 5), (6, 4), (1, 4), (0, 3), (0x123, 11), (5, 5), (1, 1));
        var otherType = Parse((2, 5), (6, 4), (1, 4), (0, 3), (0x2B7, 11), (22, 5), (1, 1));
        var cutShort = Parse((2, 5), (6, 4), (1, 4), (0, 3), (0x2B7, 11), (5, 5));

        Assert.Equal((true, 48_000, false), (sbr.Sbr, sbr.OutputSampleRate, sbr.Ps));
        Assert.True(ps.Ps);
        Assert.All([noSbr, otherExtension, otherType, cutShort], c => Assert.False(c.Sbr));
    }

    [Fact]
    public void AnUnlistedOutputRateIsTwiceTheCore()
    {
        var config = Parse((5, 5), (8, 4), (1, 4), (13, 4), (2, 5), (0, 3));

        Assert.Equal(32_000, config.OutputSampleRate);
    }

    [Fact]
    public void AnExplicitRateAndAnEscapedObjectTypeAreRead()
    {
        var config = Parse((2, 5), (15, 4), (37_800, 24), (1, 4), (0, 3));

        Assert.Equal(37_800, config.SampleRate);
        Assert.False(AacConfig.TryParse(Bits((31, 5), (10, 6), (4, 4), (2, 4), (0, 3)), out _));
    }

    [Theory]
    [InlineData(7, 8)]
    [InlineData(11, 7)]
    [InlineData(12, 8)]
    [InlineData(14, 8)]
    [InlineData(13, 0)]
    [InlineData(6, 6)]
    public void ChannelConfigurationsCountTheirChannels(int configuration, int channels)
    {
        Assert.Equal(channels, Parse((2, 5), (3, 4), (configuration, 4), (0, 3)).Channels);
    }

    [Fact]
    public void AProgramConfigElementLaysOutTheChannels()
    {
        var config = Parse(
            (2, 5), (3, 4), (0, 4), (0, 3),
            (0, 4), (1, 2), (3, 4),
            (2, 4), (1, 4), (1, 4), (1, 2), (1, 3), (1, 4),
            (1, 1), (0, 4), (1, 1), (0, 4), (1, 1), (0, 3),
            (0, 1), (0, 4), (1, 1), (1, 4), (1, 1), (2, 4), (1, 1), (3, 4),
            (0, 4), (0, 4), (0, 5),
            (0, 2),
            (2, 8), (0x41, 8), (0x42, 8));

        Assert.Equal(8, config.Channels);
    }

    [Fact]
    public void ShortFramesCoreCoderDelaysAndExtensionsAreStepped()
    {
        var shortFrames = Parse((2, 5), (3, 4), (2, 4), (1, 1), (1, 1), (100, 14), (0, 1));
        var layered = Parse((6, 5), (3, 4), (2, 4), (0, 3), (5, 3));
        var errorResilient = Parse((17, 5), (3, 4), (2, 4), (0, 1), (0, 1), (1, 1), (0, 3), (0, 1));
        var bsac = Parse((22, 5), (3, 4), (2, 4), (0, 1), (0, 1), (1, 1), (0, 16), (0, 1));
        var layeredExtension = Parse((20, 5), (3, 4), (2, 4), (0, 1), (0, 1), (1, 1), (0, 3), (0, 3), (0, 1));

        Assert.Equal(960, shortFrames.FrameLength);
        Assert.Equal([6, 17, 22, 20], new[] { layered, errorResilient, bsac, layeredExtension }.Select(c => c.ObjectType));
    }

    [Fact]
    public void ConfigsThatAreNotAacAreRefused()
    {
        Assert.False(AacConfig.TryParse([0x12], out _));
        Assert.False(AacConfig.TryParse(Bits((2, 5), (13, 4), (2, 4), (0, 3)), out _));
        Assert.False(AacConfig.TryParse(Bits((9, 5), (3, 4), (2, 4), (0, 3)), out _));
        Assert.False(AacConfig.TryParse(Bits((2, 5), (15, 4), (0, 24), (2, 4), (0, 3)), out _));
        Assert.False(AacConfig.TryParse(Bits((2, 5), (3, 4), (0, 4), (0, 3), (0, 10), (15, 4), (15, 4)), out var cut));
        Assert.Equal(0, cut.SampleRate);
    }

    [Fact]
    public void AShortConfigIsBuiltFromAnAdtsHeadersFields()
    {
        var bytes = AacConfig.Build(2, 48_000, 2);

        Assert.Equal([0x11, 0x90], bytes);
        Assert.True(AacConfig.TryParse(bytes, out var config));
        Assert.Equal((2, 48_000, 2), (config.ObjectType, config.SampleRate, config.Channels));
        Assert.Throws<ArgumentOutOfRangeException>(() => AacConfig.Build(0, 48_000, 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => AacConfig.Build(31, 48_000, 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => AacConfig.Build(2, 47_999, 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => AacConfig.Build(2, 48_000, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => AacConfig.Build(2, 48_000, 16));
    }

    [Fact]
    public void AdtsHeadersDescribeTheirFrame()
    {
        // LC, 44.1 kHz, stereo, no CRC, 371 bytes, one raw data block.
        byte[] header = [0xFF, 0xF1, 0x50, 0x80, 0x2E, 0x7F, 0xFC];

        Assert.True(AdtsHeader.TryParse(header, out var adts));

        Assert.Equal((2, 44_100, 2, 371, 7, 1), (adts.ObjectType, adts.SampleRate, adts.ChannelConfiguration, adts.FrameLength, adts.HeaderLength, adts.Blocks));
        Assert.Equal([0x12, 0x10], adts.ToAudioSpecificConfig());
        Assert.True(AdtsHeader.TryParse([0xFF, 0xF8, 0x5D, 0x00, 0x2E, 0x7F, 0xFF], out var crc));
        Assert.Equal((9, 4, 4), (crc.HeaderLength, crc.Blocks, crc.ChannelConfiguration));
    }

    [Theory]
    [InlineData(new byte[] { 0xFF, 0xF1, 0x50, 0x80, 0x2E, 0x7F })]
    [InlineData(new byte[] { 0xFE, 0xF1, 0x50, 0x80, 0x2E, 0x7F, 0xFC })]
    [InlineData(new byte[] { 0xFF, 0xE1, 0x50, 0x80, 0x2E, 0x7F, 0xFC })]
    [InlineData(new byte[] { 0xFF, 0xF3, 0x50, 0x80, 0x2E, 0x7F, 0xFC })]
    [InlineData(new byte[] { 0xFF, 0xF1, 0x74, 0x80, 0x2E, 0x7F, 0xFC })]
    [InlineData(new byte[] { 0xFF, 0xF1, 0x50, 0x80, 0x00, 0xDF, 0xFC })]
    public void BytesThatAreNotAnAdtsHeaderAreRefused(byte[] data)
    {
        Assert.False(AdtsHeader.TryParse(data, out _));
    }
}
