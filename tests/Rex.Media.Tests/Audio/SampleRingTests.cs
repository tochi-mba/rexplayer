using Rex.Media.Audio;

namespace Rex.Media.Tests.Audio;

/// <summary>The hand-over to a device that pulls its own samples: exact order, honest counts, silence when starved.</summary>
public sealed class SampleRingTests
{
    private static float[] Ramp(int start, int count) => [.. Enumerable.Range(start, count).Select(i => (float)i)];

    [Fact]
    public void SamplesComeOutInTheOrderTheyWentIn()
    {
        var ring = new SampleRing(frames: 4, channels: 2);

        Assert.Equal(6, ring.Write(Ramp(0, 6)));
        var out1 = new float[4];
        Assert.Equal(2, ring.Read(out1));

        Assert.Equal(Ramp(0, 4), out1);
        Assert.Equal((1, 3, 2L), (ring.QueuedFrames, ring.FreeFrames, ring.FramesRead));
    }

    [Fact]
    public void WritesAndReadsWrapAroundTheEndWithoutLosingASample()
    {
        var ring = new SampleRing(frames: 3, channels: 2);
        ring.Write(Ramp(0, 4));
        ring.Read(new float[4]);

        Assert.Equal(6, ring.Write(Ramp(4, 6)));
        var all = new float[6];
        Assert.Equal(3, ring.Read(all));

        Assert.Equal(Ramp(4, 6), all);
        Assert.Equal(5L, ring.FramesRead);
    }

    [Fact]
    public void AFullRingTakesOnlyWhatFitsAndOnlyWholeFrames()
    {
        var ring = new SampleRing(frames: 2, channels: 2);

        Assert.Equal(4, ring.Write(Ramp(0, 10)));
        Assert.Equal(0, ring.Write(Ramp(10, 2)));
        Assert.Equal(0, ring.FreeFrames);

        var odd = new SampleRing(frames: 4, channels: 2);
        Assert.Equal(2, odd.Write(Ramp(0, 3)));
    }

    [Fact]
    public void AStarvedDeviceGetsSilenceThatIsNotCountedAsPlayed()
    {
        var ring = new SampleRing(frames: 8, channels: 1);
        ring.Write(Ramp(1, 2));
        var buffer = Enumerable.Repeat(9f, 5).ToArray();

        Assert.Equal(2, ring.Read(buffer));

        Assert.Equal([1f, 2f, 0f, 0f, 0f], buffer);
        Assert.Equal(2L, ring.FramesRead);
        Assert.Equal(0, ring.Read(buffer));
        Assert.All(buffer, sample => Assert.Equal(0f, sample));
    }

    [Fact]
    public void ClearingForASeekForgetsTheQueueAndRestartsTheCount()
    {
        var ring = new SampleRing(frames: 4, channels: 2);
        ring.Write(Ramp(0, 8));
        ring.Read(new float[2]);

        ring.Clear();

        Assert.Equal((0, 4, 0L), (ring.QueuedFrames, ring.FreeFrames, ring.FramesRead));
        Assert.Equal(2, ring.Write(Ramp(100, 2)));
        var after = new float[2];
        ring.Read(after);
        Assert.Equal([100f, 101f], after);
    }

    [Fact]
    public void ARingNeedsRoomAndChannels()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SampleRing(0, 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SampleRing(4, 0));
        var ring = new SampleRing(4, 3);
        Assert.Equal((3, 4), (ring.Channels, ring.CapacityFrames));
    }

    [Fact]
    public void OneWriterAndOneReaderOnTheirOwnThreadsAgreeOnEverySample()
    {
        const int Total = 200_000;
        var ring = new SampleRing(frames: 512, channels: 1);
        var received = new List<float>(Total);
        var reader = Task.Run(() =>
        {
            var chunk = new float[97];
            while (received.Count < Total)
            {
                var got = ring.Read(chunk);
                received.AddRange(chunk.AsSpan(0, got).ToArray());
            }
        }, TestContext.Current.CancellationToken);

        var source = Ramp(0, Total);
        var offset = 0;
        while (offset < Total)
        {
            offset += ring.Write(source.AsSpan(offset, Math.Min(131, Total - offset)));
        }

        Assert.True(reader.Wait(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken));
        Assert.Equal(source, received);
        Assert.Equal(Total, ring.FramesRead);
    }
}
