using Rex.Media.Codecs;

namespace Rex.Media.Tests.Codecs;

/// <summary>Pictures handed over in decode order come out in presentation order, with no more delay than the stream's reordering needs.</summary>
public sealed class PresentationOrderTests
{
    private static PresentationOrder<long> Order(int depth) => new(depth, time => time);

    [Fact]
    public void DecodeOrderWithBFramesComesOutInPresentationOrder()
    {
        // I0 P3 B1 B2 P6 B4 B5: how a decoder emits a stream with two B-frames between references.
        var order = Order(depth: 2);
        var output = new List<long>();

        foreach (var time in new long[] { 0, 3, 1, 2, 6, 4, 5 })
        {
            order.Add(time);
            order.Release(output);
        }

        Assert.Equal([0L, 1, 2, 3, 4], output);
        order.Drain(output);
        Assert.Equal([0L, 1, 2, 3, 4, 5, 6], output);
        Assert.Equal(0, order.Count);
    }

    [Fact]
    public void PicturesAlreadyInOrderPassWithOnlyTheDepthAsDelay()
    {
        var order = Order(depth: 1);
        var output = new List<long>();

        order.Add(10);
        order.Release(output);
        Assert.Empty(output);
        order.Add(20);
        order.Release(output);

        Assert.Equal([10L], output);
        Assert.Equal(1, order.Count);
    }

    [Fact]
    public void EqualTimesKeepTheOrderTheyArrivedIn()
    {
        var order = new PresentationOrder<(long Time, string Name)>(0, picture => picture.Time);
        var output = new List<(long Time, string Name)>();

        order.Add((5, "first"));
        order.Add((5, "second"));
        order.Add((1, "earlier"));
        order.Drain(output);

        Assert.Equal(["earlier", "first", "second"], output.Select(picture => picture.Name));
    }

    [Fact]
    public void ASeekDiscardsEveryHeldPicture()
    {
        var order = Order(depth: 4);
        order.Add(1);
        order.Add(2);
        var discarded = new List<long>();

        order.Clear(discarded.Add);

        Assert.Equal([1L, 2], discarded);
        Assert.Equal(0, order.Count);
        Assert.Equal(4, order.Depth);
    }

    [Fact]
    public void ArgumentsAreChecked()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Order(-1));
        Assert.Throws<ArgumentNullException>(() => new PresentationOrder<long>(1, null!));
        Assert.Throws<ArgumentNullException>(() => Order(1).Release(null!));
        Assert.Throws<ArgumentNullException>(() => Order(1).Drain(null!));
        Assert.Throws<ArgumentNullException>(() => Order(1).Clear(null!));
    }
}
