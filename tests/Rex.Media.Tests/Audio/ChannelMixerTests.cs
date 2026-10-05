using Rex.Media.Audio;
using Rex.Media.Primitives;
using Rex.Media.TestKit;

namespace Rex.Media.Tests.Audio;

public sealed class ChannelMixerTests
{
    private const float Minus3dB = 0.70710677f;

    private static float[,] Matrix(ChannelLayout input, ChannelLayout output) => ChannelMixer.BuildMatrix(input, output);

    [Fact]
    [Capability("AU-12")]
    public void StereoFoldsToMonoAtHalfEach()
    {
        var matrix = Matrix(ChannelLayout.Stereo, ChannelLayout.Mono);

        Assert.Equal(0.5f, matrix[0, 0]);
        Assert.Equal(0.5f, matrix[0, 1]);
    }

    [Fact]
    public void FiveOneFoldsToStereoWithoutTheLfeAndWithoutClipping()
    {
        var matrix = Matrix(ChannelLayout.Surround51, ChannelLayout.Stereo);
        var scale = 1 + (2 * Minus3dB);

        // FL FR FC LFE SL SR
        Assert.Equal(1 / scale, matrix[0, 0], 5);
        Assert.Equal(0, matrix[0, 1]);
        Assert.Equal(Minus3dB / scale, matrix[0, 2], 5);
        Assert.Equal(0, matrix[0, 3]);
        Assert.Equal(Minus3dB / scale, matrix[0, 4], 5);
        Assert.Equal(0, matrix[0, 5]);
        Assert.Equal(Minus3dB / scale, matrix[1, 5], 5);
    }

    [Fact]
    public void MonoSpreadsToBothFrontsAtMinus3dB()
    {
        var matrix = Matrix(ChannelLayout.Mono, ChannelLayout.Stereo);

        Assert.Equal(Minus3dB, matrix[0, 0], 5);
        Assert.Equal(Minus3dB, matrix[1, 0], 5);
    }

    [Fact]
    public void MatchingSpeakersCopyAtUnity()
    {
        var matrix = Matrix(ChannelLayout.Stereo, ChannelLayout.Surround51);

        Assert.Equal(1f, matrix[0, 0]);
        Assert.Equal(1f, matrix[1, 1]);
        for (var o = 2; o < 6; o++)
        {
            Assert.Equal(0, matrix[o, 0]);
            Assert.Equal(0, matrix[o, 1]);
        }
    }

    [Theory]
    [InlineData(ChannelLayout.SideLeft, ChannelLayout.Surround51Back, ChannelLayout.BackLeft, 1f)]
    [InlineData(ChannelLayout.SideRight, ChannelLayout.Surround51Back, ChannelLayout.BackRight, 1f)]
    [InlineData(ChannelLayout.BackLeft, ChannelLayout.Surround51, ChannelLayout.SideLeft, 1f)]
    [InlineData(ChannelLayout.BackRight, ChannelLayout.Surround51, ChannelLayout.SideRight, 1f)]
    [InlineData(ChannelLayout.FrontLeftOfCenter, ChannelLayout.Stereo, ChannelLayout.FrontLeft, 1f)]
    [InlineData(ChannelLayout.FrontRightOfCenter, ChannelLayout.Stereo, ChannelLayout.FrontRight, 1f)]
    [InlineData(ChannelLayout.TopFrontLeft, ChannelLayout.Stereo, ChannelLayout.FrontLeft, Minus3dB)]
    [InlineData(ChannelLayout.TopFrontRight, ChannelLayout.Stereo, ChannelLayout.FrontRight, Minus3dB)]
    [InlineData(ChannelLayout.TopBackLeft, ChannelLayout.Surround71, ChannelLayout.BackLeft, Minus3dB)]
    [InlineData(ChannelLayout.TopBackRight, ChannelLayout.Surround71, ChannelLayout.BackRight, Minus3dB)]
    [InlineData(ChannelLayout.TopFrontCenter, ChannelLayout.Surround, ChannelLayout.FrontCenter, Minus3dB)]
    [InlineData(ChannelLayout.TopCenter, ChannelLayout.Surround, ChannelLayout.FrontCenter, Minus3dB)]
    public void MissingSpeakersFallBackToTheirNeighbour(ChannelLayout speaker, ChannelLayout output, ChannelLayout expected, float gain)
    {
        var route = Assert.Single(ChannelMixer.Route(speaker, output, 1f));

        Assert.Equal(expected, route.Speaker);
        Assert.Equal(gain, route.Gain, 5);
    }

    [Fact]
    public void RearSpeakersWithNoRearOutputFoldToTheFront()
    {
        var route = Assert.Single(ChannelMixer.Route(ChannelLayout.SideLeft, ChannelLayout.Stereo, 1f));

        Assert.Equal(ChannelLayout.FrontLeft, route.Speaker);
        Assert.Equal(Minus3dB, route.Gain, 5);
        Assert.Equal(ChannelLayout.FrontRight, Assert.Single(ChannelMixer.Route(ChannelLayout.BackRight, ChannelLayout.Stereo, 1f)).Speaker);
    }

    [Fact]
    public void BackCentreSplitsAcrossWhatThereIs()
    {
        var back = ChannelMixer.Route(ChannelLayout.BackCenter, ChannelLayout.Surround71, 1f).ToList();
        var front = ChannelMixer.Route(ChannelLayout.BackCenter, ChannelLayout.Stereo, 1f).ToList();
        var topBack = ChannelMixer.Route(ChannelLayout.TopBackCenter, ChannelLayout.Surround71, 1f).ToList();

        Assert.Equal([ChannelLayout.BackLeft, ChannelLayout.BackRight], back.Select(r => r.Speaker));
        Assert.All(back, r => Assert.Equal(Minus3dB, r.Gain, 5));
        Assert.Equal([ChannelLayout.FrontLeft, ChannelLayout.FrontRight], front.Select(r => r.Speaker));
        Assert.All(front, r => Assert.Equal(0.5f, r.Gain, 5));
        Assert.All(topBack, r => Assert.Equal(0.5f, r.Gain, 5));
    }

    [Fact]
    public void LfeAndUnplaceableSpeakersAreDropped()
    {
        Assert.Empty(ChannelMixer.Route(ChannelLayout.LowFrequency, ChannelLayout.Stereo, 1f));
        Assert.Empty(ChannelMixer.Route(ChannelLayout.FrontLeft, ChannelLayout.Quad & ~ChannelLayout.FrontLeft & ~ChannelLayout.FrontRight, 1f));
        Assert.Equal(ChannelLayout.FrontCenter, Assert.Single(ChannelMixer.Route(ChannelLayout.FrontLeft, ChannelLayout.FrontCenter | ChannelLayout.BackLeft, 1f)).Speaker);
        Assert.Equal(ChannelLayout.FrontCenter, Assert.Single(ChannelMixer.Route(ChannelLayout.SideLeft, ChannelLayout.Mono, 1f)).Speaker);
    }

    [Fact]
    public void ProcessingAppliesTheMatrixAndKeepsTimestamps()
    {
        var mixer = new ChannelMixer(ChannelLayout.Stereo, ChannelLayout.Mono);
        using var input = AudioFrame.Rent(48_000, 2, 3, ChannelLayout.Stereo);
        new[] { 1f, 0.5f, 0f }.CopyTo(input.Channel(0));
        new[] { 0f, 0.5f, 1f }.CopyTo(input.Channel(1));
        input.Pts = MediaTime.FromSeconds(4);
        input.Generation = 2;

        using var output = mixer.Process(input);

        Assert.Equal([0.5f, 0.5f, 0.5f], output.Channel(0).ToArray());
        Assert.Equal(MediaTime.FromSeconds(4), output.Pts);
        Assert.Equal(2, output.Generation);
        Assert.Equal(ChannelLayout.Mono, output.Layout);
        Assert.Equal(0.5f, mixer.Coefficient(0, 1));
        Assert.Equal(ChannelLayout.Stereo, mixer.Input);
        Assert.Equal(ChannelLayout.Mono, mixer.Output);
    }

    [Fact]
    public void BadArgumentsAreRejected()
    {
        var mixer = new ChannelMixer(ChannelLayout.Stereo, ChannelLayout.Mono);
        using var mono = AudioFrame.Rent(48_000, 1, 1);

        Assert.Throws<ArgumentException>(() => new ChannelMixer(ChannelLayout.None, ChannelLayout.Stereo));
        Assert.Throws<ArgumentException>(() => new ChannelMixer(ChannelLayout.Stereo, ChannelLayout.None));
        Assert.Throws<InvalidOperationException>(() => mixer.Process(mono));
        Assert.Throws<ArgumentNullException>(() => mixer.Process(null!));
    }
}
