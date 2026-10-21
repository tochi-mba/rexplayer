using Rex.Media.Codecs.Aac;

namespace Rex.Media.Tests.Codecs;

/// <summary>The esds form of an AAC configuration, built byte for byte as MPEG-4 Systems lays it out.</summary>
public sealed class EsDescriptorTests
{
    [Fact]
    public void AnAacLcConfigIsWrappedInAnEsDescriptor()
    {
        // AAC LC, 48 kHz, stereo: object type 2, frequency index 3, channel configuration 2.
        byte[] audioSpecificConfig = [0x11, 0x90];

        var descriptor = EsDescriptor.ForAac(audioSpecificConfig);

        byte[] expected =
        [
            0x03, 25, 0, 0, 0,
            0x04, 17, 0x40, 0x15, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
            0x05, 2, 0x11, 0x90,
            0x06, 1, 0x02,
        ];
        Assert.Equal(expected, descriptor);
    }

    [Theory]
    [InlineData(0, new byte[] { 0x00 })]
    [InlineData(127, new byte[] { 0x7F })]
    [InlineData(128, new byte[] { 0x81, 0x00 })]
    [InlineData(300, new byte[] { 0x82, 0x2C })]
    [InlineData(16_384, new byte[] { 0x81, 0x80, 0x00 })]
    public void SizesUseSevenBitsAByteWithTheTopBitMarkingMore(int length, byte[] encoded)
    {
        Assert.Equal(encoded, EsDescriptor.Size(length));
    }

    [Fact]
    public void ALongConfigurationGetsAMultiByteSize()
    {
        var descriptor = EsDescriptor.ForAac(new byte[200]);

        // The decoder-specific info alone is 200 bytes, so every enclosing size takes two bytes.
        Assert.Equal([0x03, 0x81], descriptor[..2]);
        Assert.Throws<ArgumentOutOfRangeException>(() => EsDescriptor.Size(-1));
    }
}
