using Rex.Media.Audio;

namespace Rex.Media.Tests.Audio;

public sealed class CaptureFeedTests
{
    private static (float[] Left, float[] Right) Newest(AudioScope scope, int count)
    {
        var (left, right) = (new float[count], new float[count]);
        Assert.True(scope.Read(left, right));
        return (left, right);
    }

    [Fact]
    public void FloatStereoIsKeptAsItIs()
    {
        var scope = new AudioScope();
        using var feed = new CaptureFeed(scope);
        float[] samples = [0.5f, -0.25f, 0.75f, 0.125f];
        feed.Write(System.Runtime.InteropServices.MemoryMarshal.AsBytes(samples.AsSpan()), 2, new CaptureFormat(48_000, 2, 32, true));

        var (left, right) = Newest(scope, 2);
        Assert.Equal([0.5f, 0.75f], left);
        Assert.Equal([-0.25f, 0.125f], right);
        Assert.Equal(48_000, scope.SampleRate);
        Assert.Same(scope, feed.Scope);
    }

    [Fact]
    public void SixteenBitIsScaledAndMonoFeedsBothSides()
    {
        var scope = new AudioScope();
        using var feed = new CaptureFeed(scope);
        short[] mono = [16384, -32768];
        feed.Write(System.Runtime.InteropServices.MemoryMarshal.AsBytes(mono.AsSpan()), 2, new CaptureFormat(44_100, 1, 16, false));

        var (left, right) = Newest(scope, 2);
        Assert.Equal([0.5f, -1f], left);
        Assert.Equal(left, right);
    }

    [Fact]
    public void MoreThanTwoChannelsGiveTheFrontLeftAndRight()
    {
        var scope = new AudioScope();
        using var feed = new CaptureFeed(scope);
        float[] surround = [0.1f, 0.2f, 0.9f, 0.9f, 0.9f, 0.9f];
        feed.Write(System.Runtime.InteropServices.MemoryMarshal.AsBytes(surround.AsSpan()), 1, new CaptureFormat(48_000, 6, 32, true));

        var (left, right) = Newest(scope, 1);
        Assert.Equal((0.1f, 0.2f), (left[0], right[0]));
        Assert.Equal(24, new CaptureFormat(48_000, 6, 32, true).BlockAlign);
    }

    [Fact]
    public void ASilentPacketIsSilenceAndTheFrameGrowsAndFollowsTheRate()
    {
        var scope = new AudioScope();
        using var feed = new CaptureFeed(scope);
        float[] loud = [1, 1];
        feed.Write(System.Runtime.InteropServices.MemoryMarshal.AsBytes(loud.AsSpan()), 1, new CaptureFormat(48_000, 2, 32, true));
        feed.Write([], 4096, new CaptureFormat(44_100, 2, 32, true), silent: true);

        var (left, _) = Newest(scope, 4);
        Assert.All(left, sample => Assert.Equal(0, sample));
        Assert.Equal(44_100, scope.SampleRate);
        feed.Write([], 0, new CaptureFormat(44_100, 2, 32, true));
    }

    [Fact]
    public void UnreadableFormatsAndShortDataAreRefused()
    {
        using var feed = new CaptureFeed(new AudioScope());
        Assert.Throws<NotSupportedException>(() => feed.Write(new byte[24], 1, new CaptureFormat(48_000, 2, 24, false)));
        Assert.Throws<NotSupportedException>(() => feed.Write(new byte[24], 1, new CaptureFormat(48_000, 2, 16, true)));
        Assert.Throws<NotSupportedException>(() => feed.Write(new byte[24], 1, new CaptureFormat(48_000, 0, 32, true)));
        Assert.Throws<NotSupportedException>(() => feed.Write(new byte[24], 1, new CaptureFormat(0, 2, 32, true)));
        Assert.Throws<ArgumentException>(() => feed.Write(new byte[4], 1, new CaptureFormat(48_000, 2, 32, true)));
        Assert.Throws<ArgumentNullException>(() => new CaptureFeed(null!));
        feed.Dispose();
        feed.Dispose();
    }
}
