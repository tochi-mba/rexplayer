using Rex.Media.Codecs;
using Rex.Media.Codecs.Aac;
using Rex.Media.Primitives;

namespace Rex.Media.Tests.Codecs;

/// <summary>How each track is handed to the Mac's AudioToolbox, decided and tested on every platform.</summary>
public sealed class AudioToolboxSetupTests
{
    private static TrackInfo Track(CodecId codec, byte[] config, int channels = 2) =>
        new() { Id = 1, Codec = codec, CodecPrivate = config, Audio = new AudioTrackInfo { SampleRate = 48_000, Channels = channels } };

    /// <summary>An Opus identification header: version 1, the channels, a pre-skip of 312, 48 kHz input, no gain, the mapping family.</summary>
    private static byte[] OpusHead(byte channels, byte family = 0, params byte[] mapping) =>
        [.. "OpusHead"u8, 1, channels, 0x38, 0x01, 0x80, 0xBB, 0, 0, 0, 0, family, .. mapping];

    [Fact]
    public void AacLcIsCodedAndPlayedAtItsRateWithItsConfigurationAsAnEsDescriptor()
    {
        byte[] lcStereo48k = [0x11, 0x90];

        var setup = AudioToolboxSetup.For(Track(CodecId.Aac, lcStereo48k));

        Assert.Equal((AudioToolboxFormat.Aac, 48_000, 48_000, 2, 1024), (setup.Format, setup.CodedRate, setup.OutputRate, setup.Channels, setup.FramesPerPacket));
        Assert.Equal(EsDescriptor.ForAac(lcStereo48k), setup.Cookie);
        Assert.Equal(1024, setup.LongestPacket);
        Assert.Equal(0, setup.SamplesIn([0xFC, 0xFF]));
    }

    [Fact]
    public void HeAacPlaysAtTwiceItsCoreRateWithTwiceTheSamplesAPacket()
    {
        // HE-AAC with explicit SBR: object type 5, core 24 kHz (index 6), stereo, extension index 3 (48 kHz), then LC.
        byte[] heAac = [0x2B, 0x11, 0x88, 0x00];

        var setup = AudioToolboxSetup.For(Track(CodecId.Aac, heAac));

        Assert.Equal((24_000, 48_000, 2048), (setup.CodedRate, setup.OutputRate, setup.FramesPerPacket));
    }

    [Fact]
    public void AacThatCannotBeConfiguredIsRefused()
    {
        Assert.Throws<MediaFormatException>(() => AudioToolboxSetup.For(Track(CodecId.Aac, [])));
        Assert.Throws<MediaFormatException>(() => AudioToolboxSetup.For(Track(CodecId.Aac, [0x10, 0x00], channels: 0)));

        // LC at 48 kHz with the reserved channel configuration 8: it parses but names no channels,
        // and the track names none either.
        var noChannels = Assert.Throws<MediaFormatException>(() => AudioToolboxSetup.For(Track(CodecId.Aac, [0x11, 0xC0], channels: 0)));
        Assert.Contains("channel count", noChannels.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void StereoOpusDecodesAt48kHzWithItsIdentificationHeaderAndPacketsOfTheirOwnLength()
    {
        var head = OpusHead(2);

        var setup = AudioToolboxSetup.For(Track(CodecId.Opus, head));

        Assert.Equal((AudioToolboxFormat.Opus, 48_000, 48_000, 2, 0), (setup.Format, setup.CodedRate, setup.OutputRate, setup.Channels, setup.FramesPerPacket));
        Assert.Equal([.. head[..10], 0, 0, .. head[12..]], setup.Cookie);
        Assert.Equal(AudioToolboxSetup.LongestOpusPacket, setup.LongestPacket);

        // TOC byte: CELT full band 20 ms (configuration 31), one frame.
        Assert.Equal(960, setup.SamplesIn([0xF8, 0x00]));
    }

    [Fact]
    public void OpusIsGivenNoPreSkipAsPlaybackLeavesThoseSamplesOutByTheirTimes()
    {
        var head = OpusHead(2);

        var setup = AudioToolboxSetup.For(Track(CodecId.Opus, head));

        Assert.Equal(0, Rex.Media.Codecs.Opus.OpusHead.Parse(setup.Cookie).PreSkip);
        Assert.Equal(312, Rex.Media.Codecs.Opus.OpusHead.Parse(head).PreSkip);
    }

    [Fact]
    public void OpusThatComesOutShortOfItsPacketIsTimedToEndWhereThePacketEnds()
    {
        var opus = AudioToolboxSetup.For(Track(CodecId.Opus, OpusHead(2)));
        var start = MediaTime.FromSamples(-312, 48_000);

        // The decoder's 2.5 ms delay kept back from the first packet: the rest starts 120 samples in.
        Assert.Equal(MediaTime.FromSamples(-192, 48_000), opus.OutputStart(start, 960, 840));
        Assert.Equal(start, opus.OutputStart(start, 960, 960));
        Assert.Equal(MediaTime.FromSamples(-360, 48_000), opus.OutputStart(start, 960, 1008));
        Assert.Equal(MediaTime.Unknown, opus.OutputStart(MediaTime.Unknown, 960, 840));
        Assert.Equal(start, opus.OutputStart(start, 0, 840));
    }

    [Fact]
    public void AacComesOutWholeSoItIsTimedFromItsPacket()
    {
        var aac = AudioToolboxSetup.For(Track(CodecId.Aac, [0x11, 0x90]));
        var start = MediaTime.FromSamples(1024, 48_000);

        Assert.Equal(start, aac.OutputStart(start, 0, 512));
    }

    [Fact]
    public void MonoOpusAndMappingFamilyOneAreTakenButSurroundIsLeftAlone()
    {
        Assert.Equal(1, AudioToolboxSetup.For(Track(CodecId.Opus, OpusHead(1), channels: 1)).Channels);
        Assert.Equal(2, AudioToolboxSetup.For(Track(CodecId.Opus, OpusHead(2, 1, 1, 1, 0, 1))).Channels);
        var surround = Assert.Throws<MediaFormatException>(() => AudioToolboxSetup.For(Track(CodecId.Opus, OpusHead(6, 1, 4, 2, 0, 4, 1, 2, 3, 5), channels: 6)));
        Assert.Contains("6 channels", surround.Message, StringComparison.Ordinal);
        Assert.Throws<MediaFormatException>(() => AudioToolboxSetup.For(Track(CodecId.Opus, OpusHead(2, 255, 1, 0, 0, 1))));
    }

    [Fact]
    public void OpusWithoutItsHeaderAndOtherCodecsAreRefused()
    {
        Assert.Throws<MediaFormatException>(() => AudioToolboxSetup.For(Track(CodecId.Opus, [])));
        Assert.Throws<MediaFormatException>(() => AudioToolboxSetup.For(Track(CodecId.Vorbis, [1])));
        Assert.Throws<ArgumentNullException>(() => AudioToolboxSetup.For(null!));
    }
}
