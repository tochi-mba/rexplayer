using Rex.Media.AppCore.Library;
using Rex.Media.AppCore.Performance;

namespace Rex.Media.Tests.AppCore;

public sealed class PictureMemoryTests
{
    [Fact]
    public void TheLeastRecentlyShownGoFirstWhenTheBytesPassTheCeiling()
    {
        var memory = new PictureMemory<string>(300);
        memory.Add("a", "A", 100);
        memory.Add("b", "B", 100);
        memory.Add("c", "C", 100);
        Assert.True(memory.TryGet("A", out var a));
        Assert.Equal("A", a);
        memory.Add("d", "D", 100);
        Assert.False(memory.TryGet("b", out _));
        Assert.True(memory.TryGet("a", out _));
        Assert.Equal((3, 300L), (memory.Count, memory.Bytes));
    }

    [Fact]
    public void ReplacingAPictureCountsItOnce()
    {
        var memory = new PictureMemory<string>(1000);
        memory.Add("a", "old", 100);
        memory.Add("a", "new", 250);
        Assert.True(memory.TryGet("a", out var value));
        Assert.Equal(("new", 1, 250L), (value, memory.Count, memory.Bytes));
    }

    [Fact]
    public void APictureLargerThanTheCeilingIsNotKeptAndCostsAtLeastAByte()
    {
        var memory = new PictureMemory<string>(10);
        memory.Add("big", "B", 11);
        memory.Add("free", "F", 0);
        Assert.False(memory.TryGet("big", out var missing));
        Assert.Null(missing);
        Assert.Equal((1, 1L), (memory.Count, memory.Bytes));
    }

    [Fact]
    public void TrimmingKeepsTheMostRecentAndLoweringTheCeilingTrimsAtOnce()
    {
        var memory = new PictureMemory<string>(-5);
        Assert.Equal(0, memory.Ceiling);
        memory.Ceiling = 1000;
        for (var i = 0; i < 10; i++)
        {
            memory.Add("p" + i, "P" + i, 100);
        }

        memory.TrimTo(250);
        Assert.Equal((2, 200L), (memory.Count, memory.Bytes));
        Assert.True(memory.TryGet("p9", out _));
        memory.Ceiling = 100;
        Assert.Equal(1, memory.Count);
        Assert.True(memory.TryGet("p9", out _));
        memory.TrimTo(-1);
        Assert.Equal(0, memory.Count);
        memory.Add("x", "X", 50);
        memory.Clear();
        Assert.Equal((0, 0L), (memory.Count, memory.Bytes));
    }

    [Theory]
    [InlineData(320, 0, 0, 409_600L)]
    [InlineData(320, 100, 50, 20_000L)]
    [InlineData(320, 4000, 3000, 409_600L)]
    [InlineData(0, 0, 0, 4L)]
    public void APictureCostsItsDecodedPixels(int side, int width, int height, long bytes)
    {
        Assert.Equal(bytes, PictureDecodeLimit.DecodedBytes(side, width, height));
    }

    [Fact]
    public void PictureMemoryShrinksWithTheModeAndEnergySaverCapsIt()
    {
        var facts = new DeviceFacts { Processors = 8, MemoryBytes = 32L << 30 };
        var best = DeviceProfile.For(PerformanceTier.BestQuality, facts);
        var balanced = DeviceProfile.For(PerformanceTier.Balanced, facts);
        var fastest = DeviceProfile.For(PerformanceTier.Fastest, facts);
        Assert.True(best.PictureMemoryBytes > balanced.PictureMemoryBytes && balanced.PictureMemoryBytes > fastest.PictureMemoryBytes);
        Assert.True(best.PictureMemoryBytes > best.PictureMemoryIdleBytes && best.PictureMemoryIdleBytes > best.PictureMemoryFloorBytes);
        var saver = DeviceProfile.For(PerformanceTier.BestQuality, facts with { EnergySaver = true });
        Assert.Equal(64L << 20, saver.PictureMemoryBytes);
        Assert.Equal(fastest.PictureMemoryBytes, DeviceProfile.For(PerformanceTier.Fastest, facts with { EnergySaver = true }).PictureMemoryBytes);
    }
}
