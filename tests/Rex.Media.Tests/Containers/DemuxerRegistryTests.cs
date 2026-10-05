using Rex.Media.Containers;
using Rex.Media.Containers.Riff;
using Rex.Media.IO;
using Rex.Media.Primitives;
using Rex.Media.TestKit;

namespace Rex.Media.Tests.Containers;

public sealed class DemuxerRegistryTests
{
    private static DemuxerRegistry Registry() => new DemuxerRegistry().Add(new WavDemuxerFactory()).Add(new AiffDemuxerFactory());

    [Fact]
    public void ContentWinsOverTheExtension()
    {
        var wav = Pcm.SineWav(440, 8000, 1, 0.01);

        var factory = Registry().Probe(new MemoryByteSource(wav, "misnamed.aiff"), CancellationToken.None);

        Assert.IsType<WavDemuxerFactory>(factory);
    }

    [Fact]
    public void OpenUsesTheBestFactory()
    {
        var aiff = new AiffBuilder().Common(1, 1, 16, 8000).SoundData(new byte[2]).Build();

        using var demuxer = Registry().Open(new MemoryByteSource(aiff, "x.wav"), CancellationToken.None);

        Assert.Equal("AIFF", demuxer.Info.FormatName);
        Assert.Equal(2, Registry().Factories.Count);
    }

    [Fact]
    public void UnknownBytesAreDescribedInTheError()
    {
        var error = Assert.Throws<MediaFormatException>(() => Registry().Open(new MemoryByteSource(new byte[] { 0xDE, 0xAD, 0xBE, 0xEF }, "mystery.bin"), CancellationToken.None));

        Assert.Equal("rexplayer does not recognise the format of mystery.bin. It starts with DE AD BE EF.", error.Message);
    }

    [Fact]
    public void AnEmptySourceIsCalledEmpty()
    {
        var error = Assert.Throws<MediaFormatException>(() => Registry().Open(new MemoryByteSource(Array.Empty<byte>(), "empty.wav"), CancellationToken.None));

        Assert.Equal("empty.wav is empty.", error.Message);
    }

    [Fact]
    public void ArgumentsAreChecked()
    {
        Assert.Throws<ArgumentNullException>(() => Registry().Add(null!));
        Assert.Throws<ArgumentNullException>(() => Registry().Probe(null!, CancellationToken.None));
        Assert.Throws<ArgumentNullException>(() => Registry().Open(null!, CancellationToken.None));
        Assert.Null(Registry().Probe([1, 2, 3], null));
    }

    [Fact]
    public void FourCharacterCodesConvertBothWays()
    {
        Assert.Equal("RIFF", FourCC.ToString("RIFF"u8));
        Assert.Equal("a?b?", FourCC.ToString(new byte[] { (byte)'a', 0x01, (byte)'b', 0xFF }));
        Assert.Equal(0x52494646u, FourCC.ToUInt32("RIFF"));
        Assert.True(FourCC.Matches("RIFFxxxx"u8, "RIFF"));
        Assert.False(FourCC.Matches("RIF"u8, "RIFF"));
        Assert.False(FourCC.Matches("RIFF"u8, "RIF"));
        Assert.False(FourCC.Matches("RIFX"u8, "RIFF"));
        Assert.Throws<ArgumentOutOfRangeException>(() => FourCC.ToString("RIF"u8));
        Assert.Throws<ArgumentOutOfRangeException>(() => FourCC.ToUInt32("RIFFX"));
        Assert.Throws<ArgumentNullException>(() => FourCC.ToUInt32(null!));
        Assert.Throws<ArgumentNullException>(() => FourCC.Matches("RIFF"u8, null!));
    }
}
