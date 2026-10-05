using System.Buffers.Binary;
using Rex.Media.Containers.Riff;
using Rex.Media.Primitives;

namespace Rex.Media.Tests.Containers;

public sealed class WaveFormatTests
{
    private static byte[] Block(ushort tag, int channels, int rate, int blockAlign, int bits, byte[]? extra = null)
    {
        var block = new byte[extra is null ? 16 : 18 + extra.Length];
        BinaryPrimitives.WriteUInt16LittleEndian(block, tag);
        BinaryPrimitives.WriteUInt16LittleEndian(block.AsSpan(2), (ushort)channels);
        BinaryPrimitives.WriteUInt32LittleEndian(block.AsSpan(4), (uint)rate);
        BinaryPrimitives.WriteUInt32LittleEndian(block.AsSpan(8), (uint)(rate * blockAlign));
        BinaryPrimitives.WriteUInt16LittleEndian(block.AsSpan(12), (ushort)blockAlign);
        BinaryPrimitives.WriteUInt16LittleEndian(block.AsSpan(14), (ushort)bits);
        if (extra is not null)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(block.AsSpan(16), (ushort)extra.Length);
            extra.CopyTo(block, 18);
        }

        return block;
    }

    [Theory]
    [InlineData(WaveFormat.Pcm, 1, 8, SampleFormat.U8)]
    [InlineData(WaveFormat.Pcm, 2, 16, SampleFormat.S16)]
    [InlineData(WaveFormat.Pcm, 3, 24, SampleFormat.S24)]
    [InlineData(WaveFormat.Pcm, 4, 32, SampleFormat.S32)]
    [InlineData(WaveFormat.Pcm, 5, 40, SampleFormat.Unknown)]
    [InlineData(WaveFormat.IeeeFloat, 4, 32, SampleFormat.F32)]
    [InlineData(WaveFormat.IeeeFloat, 8, 64, SampleFormat.F64)]
    [InlineData(WaveFormat.IeeeFloat, 2, 16, SampleFormat.Unknown)]
    [InlineData(WaveFormat.Alaw, 1, 8, SampleFormat.Unknown)]
    public void PcmStorageIsJudgedByTheContainerSize(ushort tag, int bytesPerSample, int bits, SampleFormat expected)
    {
        var format = WaveFormat.Parse(Block(tag, 1, 8000, bytesPerSample, bits), bigEndian: false);

        Assert.Equal(expected, format.PcmFormat);
    }

    [Theory]
    [InlineData(WaveFormat.Pcm, CodecId.Pcm)]
    [InlineData(WaveFormat.IeeeFloat, CodecId.Pcm)]
    [InlineData(WaveFormat.Alaw, CodecId.Alaw)]
    [InlineData(WaveFormat.Mulaw, CodecId.Mulaw)]
    [InlineData(WaveFormat.AdpcmIma, CodecId.AdpcmIma)]
    [InlineData(WaveFormat.AdpcmMs, CodecId.AdpcmMs)]
    [InlineData(WaveFormat.Mpeg, CodecId.Mp2)]
    [InlineData(WaveFormat.MpegLayer3, CodecId.Mp3)]
    [InlineData(WaveFormat.Aac, CodecId.Aac)]
    [InlineData(WaveFormat.AacLatm, CodecId.Aac)]
    [InlineData(WaveFormat.Wma, CodecId.Wma)]
    [InlineData(WaveFormat.WmaPro, CodecId.WmaPro)]
    [InlineData(WaveFormat.WmaLossless, CodecId.WmaLossless)]
    [InlineData(WaveFormat.Ac3, CodecId.Ac3)]
    [InlineData(WaveFormat.Dts, CodecId.Dts)]
    [InlineData((ushort)0x1234, CodecId.Unknown)]
    public void FormatTagsMapToCodecs(ushort tag, CodecId expected)
    {
        Assert.Equal(expected, WaveFormat.Parse(Block(tag, 2, 44_100, 4, 16), bigEndian: false).Codec);
    }

    [Fact]
    public void AnExtensibleBlockResolvesToItsSubformatAndMask()
    {
        var extra = new byte[22];
        BinaryPrimitives.WriteUInt16LittleEndian(extra, 20);
        BinaryPrimitives.WriteUInt32LittleEndian(extra.AsSpan(2), (uint)ChannelLayout.Surround51);
        BinaryPrimitives.WriteUInt16LittleEndian(extra.AsSpan(6), WaveFormat.IeeeFloat);

        var format = WaveFormat.Parse(Block(WaveFormat.Extensible, 6, 48_000, 24, 32, extra), bigEndian: false);

        Assert.Equal(WaveFormat.IeeeFloat, format.FormatTag);
        Assert.Equal(20, format.ValidBitsPerSample);
        Assert.Equal(ChannelLayout.Surround51, format.ChannelMask);
        Assert.Equal(SampleFormat.F32, format.PcmFormat);
        Assert.Empty(format.Extra);
        Assert.Equal(ChannelLayout.Surround51, format.ToTrack(1, MediaTime.Unknown).Audio!.Layout);
    }

    [Fact]
    public void ExtensibleExtraBytesBeyondTheStandardBlockAreKept()
    {
        var extra = new byte[24];
        BinaryPrimitives.WriteUInt16LittleEndian(extra.AsSpan(6), WaveFormat.Pcm);
        extra[22] = 7;

        var format = WaveFormat.Parse(Block(WaveFormat.Extensible, 2, 48_000, 4, 16, extra), bigEndian: false);

        Assert.Equal(new byte[] { 7, 0 }, format.Extra);
        Assert.Equal(16, format.ValidBitsPerSample);
    }

    [Fact]
    public void AShortExtensibleBlockStaysUnknown()
    {
        var format = WaveFormat.Parse(Block(WaveFormat.Extensible, 2, 48_000, 4, 16, new byte[4]), bigEndian: false);

        Assert.Equal(CodecId.Unknown, format.Codec);
    }

    [Fact]
    public void AMaskThatDisagreesWithTheChannelCountFallsBackToTheDefault()
    {
        var extra = new byte[22];
        BinaryPrimitives.WriteUInt32LittleEndian(extra.AsSpan(2), (uint)ChannelLayout.Surround51);
        BinaryPrimitives.WriteUInt16LittleEndian(extra.AsSpan(6), WaveFormat.Pcm);

        var format = WaveFormat.Parse(Block(WaveFormat.Extensible, 2, 48_000, 4, 16, extra), bigEndian: false);

        Assert.Equal(ChannelLayout.Stereo, format.ToTrack(0, MediaTime.Unknown).Audio!.Layout);
    }

    [Fact]
    public void TheFourteenByteFormOfTheBlockIsAccepted()
    {
        var format = WaveFormat.Parse(Block(WaveFormat.Pcm, 1, 8000, 2, 16).AsSpan(0, 14), bigEndian: false);

        Assert.Equal(0, format.BitsPerSample);
        Assert.Equal(CodecId.Pcm, format.Codec);
    }

    [Fact]
    public void ImpossibleBlocksAreRejected()
    {
        Assert.Throws<MediaFormatException>(() => WaveFormat.Parse(new byte[10], bigEndian: false));
        Assert.Throws<MediaFormatException>(() => WaveFormat.Parse(Block(1, 0, 8000, 2, 16), bigEndian: false));
        Assert.Throws<MediaFormatException>(() => WaveFormat.Parse(Block(1, 33, 8000, 66, 16), bigEndian: false));
        Assert.Throws<MediaFormatException>(() => WaveFormat.Parse(Block(1, 1, 0, 2, 16), bigEndian: false));
        Assert.Throws<MediaFormatException>(() => WaveFormat.Parse(Block(1, 1, 800_000, 2, 16), bigEndian: false));
    }

    [Fact]
    public void BigEndianBlocksAreReadBigEndian()
    {
        var block = new byte[16];
        BinaryPrimitives.WriteUInt16BigEndian(block, 1);
        BinaryPrimitives.WriteUInt16BigEndian(block.AsSpan(2), 2);
        BinaryPrimitives.WriteUInt32BigEndian(block.AsSpan(4), 22_050);
        BinaryPrimitives.WriteUInt32BigEndian(block.AsSpan(8), 88_200);
        BinaryPrimitives.WriteUInt16BigEndian(block.AsSpan(12), 4);
        BinaryPrimitives.WriteUInt16BigEndian(block.AsSpan(14), 16);

        var format = WaveFormat.Parse(block, bigEndian: true);

        Assert.Equal(22_050, format.SampleRate);
        Assert.Equal(2, format.Channels);
    }

    [Fact]
    public void AdpcmSamplesPerBlockComeFromTheExtraBytesOrTheBlockSize()
    {
        Assert.Equal(1017, WaveFormat.Parse(Block(WaveFormat.AdpcmIma, 2, 44_100, 1024, 4), bigEndian: false).SamplesPerBlock);
        Assert.Equal(500, WaveFormat.Parse(Block(WaveFormat.AdpcmMs, 1, 22_050, 256, 4, [0xF4, 0x01]), bigEndian: false).SamplesPerBlock);
        Assert.Equal(1, WaveFormat.Parse(Block(WaveFormat.AdpcmMs, 1, 22_050, 256, 4), bigEndian: false).SamplesPerBlock);
        Assert.Equal(1, WaveFormat.Parse(Block(WaveFormat.AdpcmIma, 2, 44_100, 4, 4), bigEndian: false).SamplesPerBlock);
        Assert.Equal(1, WaveFormat.Parse(Block(WaveFormat.Pcm, 2, 44_100, 4, 16), bigEndian: false).SamplesPerBlock);
    }

    [Fact]
    public void ATrackWithoutAnAverageRateHasNoBitRate()
    {
        var block = Block(WaveFormat.Pcm, 1, 8000, 2, 16);
        BinaryPrimitives.WriteUInt32LittleEndian(block.AsSpan(8), 0);

        var track = WaveFormat.Parse(block, bigEndian: false).ToTrack(3, MediaTime.Zero);

        Assert.Null(track.BitRate);
        Assert.Equal(3, track.Id);
    }
}
