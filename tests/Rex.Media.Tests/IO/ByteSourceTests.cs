using Rex.Media.IO;

namespace Rex.Media.Tests.IO;

public sealed class ByteSourceTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), "rexplayer-io-" + Guid.NewGuid().ToString("N") + ".bin");

    public void Dispose() => File.Delete(_path);

    [Fact]
    public void AFileSourceReadsAtAnyPositionAndReportsItsLength()
    {
        File.WriteAllBytes(_path, [1, 2, 3, 4, 5]);
        using var source = new FileByteSource(_path);
        var buffer = new byte[3];

        Assert.Equal(3, source.Read(2, buffer, TestContext.Current.CancellationToken));
        Assert.Equal(new byte[] { 3, 4, 5 }, buffer);
        Assert.Equal(1, source.Read(4, buffer, TestContext.Current.CancellationToken));
        Assert.Equal(0, source.Read(5, buffer, TestContext.Current.CancellationToken));
        Assert.Equal(5, source.Length);
        Assert.True(source.CanSeek);
        Assert.Equal(Path.GetFileName(_path), source.Name);
        Assert.Equal(Path.GetFullPath(_path), source.Path);
    }

    [Fact]
    public void AFileSourceSeesAFileGrow()
    {
        File.WriteAllBytes(_path, [1]);
        using var source = new FileByteSource(_path);
        using (var writer = new FileStream(_path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
        {
            writer.Write([2, 3]);
        }

        Assert.Equal(3, source.Length);
    }

    [Fact]
    public void AFileSourceRejectsBadArgumentsAndCancellation()
    {
        File.WriteAllBytes(_path, [1]);
        using var source = new FileByteSource(_path);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        Assert.Throws<ArgumentOutOfRangeException>(() => source.Read(-1, new byte[1], CancellationToken.None));
        Assert.Throws<OperationCanceledException>(() => source.Read(0, new byte[1], cancelled.Token));
        Assert.Throws<ArgumentException>(() => new FileByteSource(" "));
        Assert.Throws<FileNotFoundException>(() => new FileByteSource(_path + ".missing"));
    }

    [Fact]
    public void AMemorySourceReadsLikeAFile()
    {
        using var source = new MemoryByteSource(new byte[] { 9, 8, 7 }, "clip.wav", canSeek: false);
        var buffer = new byte[5];

        Assert.Equal(2, source.Read(1, buffer, TestContext.Current.CancellationToken));
        Assert.Equal(new byte[] { 8, 7, 0, 0, 0 }, buffer);
        Assert.Equal(0, source.Read(3, buffer, TestContext.Current.CancellationToken));
        Assert.Equal(3, source.Length);
        Assert.False(source.CanSeek);
        Assert.Equal("clip.wav", source.Name);
        Assert.Throws<ArgumentOutOfRangeException>(() => source.Read(-1, buffer, CancellationToken.None));
    }

    [Fact]
    public void AMemorySourceHonoursCancellation()
    {
        using var source = new MemoryByteSource(new byte[] { 1 });
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        Assert.Throws<OperationCanceledException>(() => source.Read(0, new byte[1], cancelled.Token));
        Assert.Equal("memory", source.Name);
    }

    [Fact]
    public void ASliceShowsOnlyItsWindow()
    {
        using var inner = new MemoryByteSource(new byte[] { 0, 1, 2, 3, 4, 5 }, "disc.iso");
        using var slice = new SliceByteSource(inner, 2, 3);
        var buffer = new byte[5];

        Assert.Equal(3, slice.Read(0, buffer, TestContext.Current.CancellationToken));
        Assert.Equal(new byte[] { 2, 3, 4, 0, 0 }, buffer);
        Assert.Equal(1, slice.Read(2, buffer, TestContext.Current.CancellationToken));
        Assert.Equal(0, slice.Read(3, buffer, TestContext.Current.CancellationToken));
        Assert.Equal(3, slice.Length);
        Assert.True(slice.CanSeek);
        Assert.Equal("disc.iso", slice.Name);
        Assert.Equal("track", new SliceByteSource(inner, 0, 1, "track").Name);
    }

    [Fact]
    public void ASliceRejectsBadArguments()
    {
        using var inner = new MemoryByteSource(new byte[] { 1 });

        Assert.Throws<ArgumentNullException>(() => new SliceByteSource(null!, 0, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SliceByteSource(inner, -1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SliceByteSource(inner, 0, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SliceByteSource(inner, 0, 1).Read(-1, new byte[1], CancellationToken.None));
    }
}
