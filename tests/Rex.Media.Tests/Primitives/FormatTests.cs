using Rex.Media.Primitives;

namespace Rex.Media.Tests.Primitives;

public sealed class FormatTests
{
    [Theory]
    [InlineData(1, ChannelLayout.Mono)]
    [InlineData(2, ChannelLayout.Stereo)]
    [InlineData(3, ChannelLayout.Surround)]
    [InlineData(4, ChannelLayout.Quad)]
    [InlineData(5, ChannelLayout.Surround50)]
    [InlineData(6, ChannelLayout.Surround51)]
    [InlineData(7, ChannelLayout.Surround61)]
    [InlineData(8, ChannelLayout.Surround71)]
    [InlineData(9, ChannelLayout.None)]
    public void EachChannelCountHasADefaultLayout(int channels, ChannelLayout expected)
    {
        Assert.Equal(expected, ChannelLayouts.Default(channels));
        if (expected != ChannelLayout.None)
        {
            Assert.Equal(channels, expected.ChannelCount());
        }
    }

    [Fact]
    public void SpeakersComeOutInMaskOrder()
    {
        Assert.Equal(
            [ChannelLayout.FrontLeft, ChannelLayout.FrontRight, ChannelLayout.FrontCenter, ChannelLayout.LowFrequency, ChannelLayout.SideLeft, ChannelLayout.SideRight],
            ChannelLayout.Surround51.Speakers());
    }

    [Theory]
    [InlineData(ChannelLayout.Mono, 1, "Mono")]
    [InlineData(ChannelLayout.Stereo, 2, "Stereo")]
    [InlineData(ChannelLayout.Surround, 3, "3.0")]
    [InlineData(ChannelLayout.Quad, 4, "Quadraphonic")]
    [InlineData(ChannelLayout.Surround50, 5, "5.0")]
    [InlineData(ChannelLayout.Surround51, 6, "5.1")]
    [InlineData(ChannelLayout.Surround51Back, 6, "5.1")]
    [InlineData(ChannelLayout.Surround61, 7, "6.1")]
    [InlineData(ChannelLayout.Surround71, 8, "7.1")]
    [InlineData(ChannelLayout.FrontLeft, 1, "1 channel")]
    [InlineData(ChannelLayout.None, 10, "10 channels")]
    public void LayoutsDescribeThemselves(ChannelLayout layout, int channels, string expected)
    {
        Assert.Equal(expected, layout.Describe(channels));
    }

    [Theory]
    [InlineData(SampleFormat.U8, 1)]
    [InlineData(SampleFormat.S16, 2)]
    [InlineData(SampleFormat.S24, 3)]
    [InlineData(SampleFormat.S32, 4)]
    [InlineData(SampleFormat.F32, 4)]
    [InlineData(SampleFormat.F64, 8)]
    [InlineData(SampleFormat.Unknown, 0)]
    public void SampleSizesMatchTheirFormat(SampleFormat format, int bytes)
    {
        var audio = new AudioFormat(48_000, 2, format);

        Assert.Equal(bytes, audio.BytesPerSample);
        Assert.Equal(bytes * 2, audio.BlockAlign);
    }

    [Fact]
    public void AnAudioFormatUsesItsMaskOnlyWhenTheMaskMatchesTheChannelCount()
    {
        Assert.Equal(ChannelLayout.Surround51Back, new AudioFormat(48_000, 6, SampleFormat.F32, ChannelLayout.Surround51Back).Layout);
        Assert.Equal(ChannelLayout.Stereo, new AudioFormat(48_000, 2, SampleFormat.F32, ChannelLayout.Surround51).Layout);
        Assert.Equal(ChannelLayout.Stereo, new AudioFormat(48_000, 2, SampleFormat.F32).Layout);
    }

    [Fact]
    public void AnAudioFormatDescribesItself()
    {
        var format = new AudioFormat(44_100, 2, SampleFormat.S16, bigEndian: true);

        Assert.True(format.BigEndian);
        Assert.Equal("44100 Hz, Stereo, S16", format.ToString());
    }

    [Fact]
    public void ImpossibleAudioFormatsAreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new AudioFormat(0, 2, SampleFormat.S16));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AudioFormat(48_000, 0, SampleFormat.S16));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AudioFormat(48_000, AudioFormat.MaxChannels + 1, SampleFormat.S16));
    }

    [Fact]
    public void EveryCodecHasAKindAndADisplayName()
    {
        foreach (var codec in Enum.GetValues<CodecId>())
        {
            var name = codec.DisplayName();
            Assert.False(string.IsNullOrWhiteSpace(name));
            if (codec == CodecId.Unknown)
            {
                Assert.Equal(MediaKind.Unknown, codec.Kind());
                Assert.Equal("Unknown", name);
            }
            else
            {
                Assert.NotEqual(MediaKind.Unknown, codec.Kind());
                Assert.NotEqual("Unknown", name);
            }
        }
    }

    [Fact]
    public void CodecKindsFollowTheirGroup()
    {
        Assert.Equal(MediaKind.Audio, CodecId.Flac.Kind());
        Assert.Equal(MediaKind.Video, CodecId.H264.Kind());
        Assert.Equal(MediaKind.Subtitle, CodecId.SubRip.Kind());
    }

    [Fact]
    public void AMediaFormatExceptionCarriesItsMessageAndCause()
    {
        var inner = new InvalidOperationException("inner");

        Assert.Equal("broken", new MediaFormatException("broken").Message);
        Assert.Same(inner, new MediaFormatException("broken", inner).InnerException);
        Assert.NotNull(new MediaFormatException().Message);
    }
}
