using Rex.Media.Containers;
using Rex.Media.IO;

namespace Rex.Media.Tests.Containers;

public sealed class ScanWindowTests
{
    [Fact]
    public void TheWindowGrowsToItsCeilingAndThenReportsTheEnd()
    {
        var window = new ScanWindow(new ByteCursor(new MemoryByteSource(new byte[100])), 10, capacity: 4, maxCapacity: 8);
        Assert.Equal(10, window.Position);

        Assert.True(window.Ensure(8));
        Assert.Equal(8, window.Available);
        Assert.False(window.Grow());
        Assert.True(window.AtEnd);
        Assert.False(window.Ensure(9));

        window.Consume(3);
        Assert.Equal(13, window.Position);
        Assert.Equal(5, window.Span.Length);
    }

    [Fact]
    public void ResettingForgetsTheBufferAndReadingPastTheSourceEnds()
    {
        var data = Enumerable.Range(0, 10).Select(i => (byte)i).ToArray();
        var window = new ScanWindow(new ByteCursor(new MemoryByteSource(data)), 0, capacity: 4);
        window.Ensure(4);
        window.Consume(2);

        window.Reset(7);

        Assert.False(window.AtEnd);
        Assert.Equal(0, window.Available);
        Assert.False(window.Ensure(4));
        Assert.Equal([7, 8, 9], window.Span.ToArray());
        Assert.True(window.AtEnd);
        Assert.Throws<ArgumentOutOfRangeException>(() => window.Consume(4));
        Assert.Throws<ArgumentOutOfRangeException>(() => window.Consume(-1));
    }
}
