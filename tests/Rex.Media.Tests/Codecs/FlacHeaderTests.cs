using Rex.Media.Codecs.Flac;
using Rex.Media.Primitives;
using Rex.Media.TestKit;

namespace Rex.Media.Tests.Codecs;

public sealed class FlacHeaderTests
{
    private static readonly byte[] Md5 = [.. Enumerable.Range(1, 16).Select(i => (byte)i)];

    private static byte[] WithCrc(params byte[] head) => [.. head, (byte)Crc.Crc8Flac.Compute(head)];

    [Fact]
    public void StreamInfoRoundTripsThroughItsBytes()
    {
        var info = new FlacStreamInfo(1152, 4608, 14, 9000, 44_100, 2, 16, 0x9_8765_4321, Md5);

        var parsed = FlacStreamInfo.Parse(info.ToBytes());

        Assert.Equal(1152, parsed.MinBlockSize);
        Assert.Equal(4608, parsed.MaxBlockSize);
        Assert.Equal(14, parsed.MinFrameSize);
        Assert.Equal(9000, parsed.MaxFrameSize);
        Assert.Equal(44_100, parsed.SampleRate);
        Assert.Equal(2, parsed.Channels);
        Assert.Equal(16, parsed.BitsPerSample);
        Assert.Equal(0x9_8765_4321, parsed.TotalSamples);
        Assert.Equal(Md5, parsed.Md5);
        Assert.False(parsed.FixedBlockSize);
        Assert.True((info with { MinBlockSize = 4608 }).FixedBlockSize);
    }

    [Fact]
    public void AShortOrImpossibleStreamInfoIsRejected()
    {
        var good = new FlacStreamInfo(4096, 4096, 0, 0, 48_000, 1, 24, 0, Md5);

        Assert.Throws<MediaFormatException>(() => FlacStreamInfo.Parse(good.ToBytes().AsSpan(0, 33)));
        Assert.Throws<MediaFormatException>(() => FlacStreamInfo.Parse((good with { MinBlockSize = 15 }).ToBytes()));
        Assert.Throws<MediaFormatException>(() => FlacStreamInfo.Parse((good with { MaxBlockSize = 4095 }).ToBytes()));
        Assert.Throws<MediaFormatException>(() => FlacStreamInfo.Parse((good with { SampleRate = 0 }).ToBytes()));
        Assert.Throws<MediaFormatException>(() => FlacStreamInfo.Parse((good with { BitsPerSample = 3 }).ToBytes()));
    }

    [Theory]
    [InlineData(1, ChannelLayout.Mono)]
    [InlineData(2, ChannelLayout.Stereo)]
    [InlineData(3, ChannelLayout.Surround)]
    [InlineData(4, ChannelLayout.Quad)]
    [InlineData(5, ChannelLayout.FrontLeft | ChannelLayout.FrontRight | ChannelLayout.FrontCenter | ChannelLayout.BackLeft | ChannelLayout.BackRight)]
    [InlineData(6, ChannelLayout.Surround51Back)]
    [InlineData(7, ChannelLayout.Surround61)]
    [InlineData(8, ChannelLayout.Surround71)]
    public void ChannelCountsMapToTheSpeakerOrderOfTheSpecification(int channels, ChannelLayout layout)
    {
        Assert.Equal(layout, FlacStreamInfo.Layout(channels));
    }

    [Theory]
    [InlineData(192)]
    [InlineData(576)]
    [InlineData(1152)]
    [InlineData(2304)]
    [InlineData(4608)]
    [InlineData(256)]
    [InlineData(512)]
    [InlineData(1024)]
    [InlineData(2048)]
    [InlineData(4096)]
    [InlineData(8192)]
    [InlineData(16384)]
    [InlineData(32768)]
    [InlineData(1)]
    [InlineData(100)]
    [InlineData(300)]
    [InlineData(65536)]
    public void EveryBlockSizeFormReadsBack(int blockSize)
    {
        var bytes = FlacBuilder.FrameHeader(false, blockSize, 44_100, 1, 16, 3);

        Assert.True(FlacFrameHeader.TryParse(bytes, out var header));
        Assert.Equal(blockSize, header.BlockSize);
        Assert.Equal(bytes.Length, header.Length);
    }

    [Theory]
    [InlineData(88_200)]
    [InlineData(176_400)]
    [InlineData(192_000)]
    [InlineData(8000)]
    [InlineData(16_000)]
    [InlineData(22_050)]
    [InlineData(24_000)]
    [InlineData(32_000)]
    [InlineData(44_100)]
    [InlineData(48_000)]
    [InlineData(96_000)]
    [InlineData(22_000)]
    [InlineData(11_025)]
    [InlineData(100_010)]
    [InlineData(0)]
    public void EverySampleRateFormReadsBack(int rate)
    {
        Assert.True(FlacFrameHeader.TryParse(FlacBuilder.FrameHeader(false, 4096, rate, 0, 16, 0), out var header));
        Assert.Equal(rate, header.SampleRate);
    }

    [Theory]
    [InlineData(8)]
    [InlineData(12)]
    [InlineData(16)]
    [InlineData(20)]
    [InlineData(24)]
    [InlineData(32)]
    [InlineData(0)]
    public void EverySampleSizeCodeReadsBack(int bits)
    {
        Assert.True(FlacFrameHeader.TryParse(FlacBuilder.FrameHeader(false, 4096, 44_100, 0, bits, 0), out var header));
        Assert.Equal(bits, header.BitsPerSample);
    }

    [Theory]
    [InlineData(0, 1, FlacChannelMode.Independent)]
    [InlineData(1, 2, FlacChannelMode.Independent)]
    [InlineData(7, 8, FlacChannelMode.Independent)]
    [InlineData(8, 2, FlacChannelMode.LeftSide)]
    [InlineData(9, 2, FlacChannelMode.SideRight)]
    [InlineData(10, 2, FlacChannelMode.MidSide)]
    public void ChannelCodesGiveTheCountAndTheStereoMode(int code, int channels, FlacChannelMode mode)
    {
        Assert.True(FlacFrameHeader.TryParse(FlacBuilder.FrameHeader(false, 4096, 44_100, code, 16, 0), out var header));
        Assert.Equal(channels, header.Channels);
        Assert.Equal(mode, header.ChannelMode);
    }

    [Theory]
    [InlineData(false, 0UL)]
    [InlineData(false, 0x7FUL)]
    [InlineData(false, 0x80UL)]
    [InlineData(false, 0x7FFUL)]
    [InlineData(false, 0x800UL)]
    [InlineData(false, 0xFFFFUL)]
    [InlineData(false, 0x10000UL)]
    [InlineData(false, 0x1FFFFFUL)]
    [InlineData(false, 0x200000UL)]
    [InlineData(false, 0x3FFFFFFUL)]
    [InlineData(false, 0x4000000UL)]
    [InlineData(false, 0x7FFFFFFFUL)]
    [InlineData(true, 0x80000000UL)]
    [InlineData(true, 0xF_FFFF_FFFFUL)]
    public void FrameAndSampleNumbersOfEveryLengthReadBack(bool variable, ulong number)
    {
        Assert.True(FlacFrameHeader.TryParse(FlacBuilder.FrameHeader(variable, 4096, 44_100, 1, 16, number), out var header));

        Assert.Equal((long)number, header.CodedNumber);
        Assert.Equal(variable, header.VariableBlockSize);
        Assert.Equal(variable ? (long)number : (long)number * 1152, header.FirstSample(1152));
    }

    [Fact]
    public void TheSyncCodeIsFourteenOnesAZeroAndTheStrategyBit()
    {
        Assert.True(FlacFrameHeader.IsSync(0xFF, 0xF8));
        Assert.True(FlacFrameHeader.IsSync(0xFF, 0xF9));
        Assert.False(FlacFrameHeader.IsSync(0xFF, 0xFA));
        Assert.False(FlacFrameHeader.IsSync(0xFE, 0xF8));
    }

    public static TheoryData<string, byte[]> Invalid => new()
    {
        { "too short", [0xFF, 0xF8, 0x69, 0x18, 0x00] },
        { "no sync", WithCrc(0xFF, 0xF0, 0x69, 0x18, 0x00) },
        { "reserved block size", WithCrc(0xFF, 0xF8, 0x09, 0x18, 0x00) },
        { "forbidden rate", WithCrc(0xFF, 0xF8, 0x6F, 0x18, 0x00) },
        { "reserved channels", WithCrc(0xFF, 0xF8, 0x69, 0xB8, 0x00) },
        { "reserved sample size", WithCrc(0xFF, 0xF8, 0x69, 0x16, 0x00) },
        { "reserved bit", WithCrc(0xFF, 0xF8, 0x69, 0x19, 0x00) },
        { "continuation as lead byte", WithCrc(0xFF, 0xF8, 0x69, 0x18, 0x80) },
        { "eight-byte number", WithCrc(0xFF, 0xF8, 0x69, 0x18, 0xFF, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80) },
        { "seven-byte frame number", WithCrc(0xFF, 0xF8, 0x69, 0x18, 0xFE, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80) },
        { "bad continuation", WithCrc(0xFF, 0xF8, 0x69, 0x18, 0xC2, 0x40) },
        { "number cut short", [0xFF, 0xF8, 0x69, 0x18, 0xE0, 0x80] },
        { "8-bit block size missing", [0xFF, 0xF8, 0x69, 0x18, 0xC2, 0x80] },
        { "16-bit block size missing", [0xFF, 0xF8, 0x79, 0x18, 0xC2, 0x80, 0x00] },
        { "kHz rate missing", [0xFF, 0xF8, 0x9C, 0x18, 0xC2, 0x80] },
        { "Hz rate missing", [0xFF, 0xF8, 0x9D, 0x18, 0xC2, 0x80, 0x00] },
        { "CRC missing", [0xFF, 0xF8, 0x99, 0x18, 0xC2, 0x80] },
        { "CRC wrong", BadCrc(WithCrc(0xFF, 0xF8, 0x99, 0x18, 0x00)) },
    };

    private static byte[] BadCrc(byte[] header)
    {
        header[^1] ^= 0xFF;
        return header;
    }

    [Theory]
    [MemberData(nameof(Invalid))]
    public void BrokenHeadersAreNotHeaders(string why, byte[] bytes)
    {
        Assert.False(FlacFrameHeader.TryParse(bytes, out _), why);
    }
}
