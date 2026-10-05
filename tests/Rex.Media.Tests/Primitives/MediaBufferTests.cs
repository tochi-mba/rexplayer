using Rex.Media.Primitives;

namespace Rex.Media.Tests.Primitives;

public sealed class MediaBufferTests
{
    [Fact]
    public void ARentedBufferHasTheRequestedLength()
    {
        using var buffer = MediaBuffer.Rent(100);

        Assert.Equal(100, buffer.Length);
        Assert.Equal(100, buffer.Span.Length);
        Assert.Equal(100, buffer.Memory.Length);
        Assert.False(buffer.IsDisposed);
    }

    [Fact]
    public void AZeroLengthBufferIsAllowed()
    {
        using var buffer = MediaBuffer.Rent(0);

        Assert.Equal(0, buffer.Span.Length);
    }

    [Fact]
    public void CopyOfHoldsTheBytes()
    {
        using var buffer = MediaBuffer.CopyOf([1, 2, 3]);

        Assert.Equal(new byte[] { 1, 2, 3 }, buffer.Span.ToArray());
    }

    [Fact]
    public void TruncateShortensWithoutLosingTheStart()
    {
        using var buffer = MediaBuffer.CopyOf([1, 2, 3, 4]);

        buffer.Truncate(2);

        Assert.Equal(new byte[] { 1, 2 }, buffer.Span.ToArray());
        Assert.Throws<ArgumentOutOfRangeException>(() => buffer.Truncate(3));
        Assert.Throws<ArgumentOutOfRangeException>(() => buffer.Truncate(-1));
    }

    [Fact]
    public void ADisposedBufferRefusesUseAndDisposingTwiceIsHarmless()
    {
        var buffer = MediaBuffer.Rent(10);
        buffer.Dispose();
        buffer.Dispose();

        Assert.True(buffer.IsDisposed);
        Assert.Throws<ObjectDisposedException>(() => buffer.Span.Length);
        Assert.Throws<ObjectDisposedException>(() => buffer.Truncate(0));
    }

    [Fact]
    public void ARecycledShellStartsClean()
    {
        var buffers = Enumerable.Range(0, 64).Select(_ => MediaBuffer.Rent(32)).ToList();
        foreach (var buffer in buffers)
        {
            buffer.Dispose();
        }

        using var next = MediaBuffer.Rent(5);

        Assert.Equal(5, next.Length);
        Assert.False(next.IsDisposed);
    }

    [Fact]
    public void ANegativeLengthIsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => MediaBuffer.Rent(-1));
    }
}

public sealed class SlotPoolTests
{
    [Fact]
    public void ItemsComeBackOutOnceAndTheEmptyPoolSaysSo()
    {
        var pool = new SlotPool<object>(2);
        var first = new object();
        var second = new object();

        Assert.True(pool.Return(first));
        Assert.True(pool.Return(second));
        var taken = new[] { pool.Take(), pool.Take() };

        Assert.Contains(first, taken);
        Assert.Contains(second, taken);
        Assert.Null(pool.Take());
        Assert.Equal(2, pool.Capacity);
    }

    [Fact]
    public void AFullPoolDropsTheExtraItem()
    {
        var pool = new SlotPool<object>(1);

        Assert.True(pool.Return(new object()));
        Assert.False(pool.Return(new object()));
    }

    [Fact]
    public void BadArgumentsAreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SlotPool<object>(0));
        Assert.Throws<ArgumentNullException>(() => new SlotPool<object>(1).Return(null!));
    }
}
