using Rex.Media.Vision;

namespace Rex.Media.Tests.Vision;

public sealed class SenseWorkerTests
{
    /// <summary>A perceiver that writes the picture's first blue byte into the matte, so a scene says which picture it came from.</summary>
    private sealed class Marking : IScenePerceiver
    {
        public int Calls { get; private set; }

        public bool? LastMirror { get; private set; }

        public Exception? Throw { get; set; }

        public ManualResetEventSlim? Hold { get; set; }

        public ManualResetEventSlim Entered { get; } = new(false);

        public void Perceive(BgraPicture picture, bool mirror, SceneSense scene, long timestamp)
        {
            Entered.Set();
            Hold?.Wait();
            Calls++;
            LastMirror = mirror;
            if (Throw is { } error)
            {
                throw error;
            }

            scene.Matte[0] = picture.Pixels[0];
            scene.Source = SenseSource.HandWritten;
        }
    }

    private static byte[] Picture(byte marker, int height = 2, int stride = 16)
    {
        var pixels = new byte[stride * height];
        pixels[0] = marker;
        return pixels;
    }

    [Fact]
    public void AnOfferedPictureIsPerceivedAndTakenOnce()
    {
        var perceiver = new Marking();
        using var worker = new SenseWorker(perceiver, 8, 8, start: false);
        var scene = new SceneSense(8, 8);

        Assert.False(worker.TryTake(scene, 0));
        Assert.False(worker.Step());

        worker.Offer(Picture(7), 4, 2, 16, timestamp: 100, mirror: true);
        Assert.True(worker.Step());
        Assert.True(worker.TryTake(scene, 0));
        Assert.Equal(7, scene.Matte[0]);
        Assert.Equal(100, scene.Timestamp);
        Assert.Equal(SenseSource.HandWritten, scene.Source);
        Assert.True(perceiver.LastMirror);

        // Nothing newer than what was taken: nothing to take.
        Assert.False(worker.TryTake(scene, scene.Timestamp));
        Assert.Equal(1, worker.Perceived);
    }

    [Fact]
    public void ANewerPictureReplacesOneNotYetStarted()
    {
        var perceiver = new Marking();
        using var worker = new SenseWorker(perceiver, 8, 8, start: false);
        worker.Offer(Picture(1), 4, 2, 16, 1, false);
        worker.Offer(Picture(2), 4, 2, 16, 2, false);
        worker.Offer(Picture(3), 4, 2, 16, 3, false);

        Assert.True(worker.Step());
        Assert.False(worker.Step());
        var scene = new SceneSense(8, 8);
        Assert.True(worker.TryTake(scene, 0));
        Assert.Equal(3, scene.Matte[0]);
        Assert.Equal(1, perceiver.Calls);
        Assert.Equal(2, worker.Skipped);
    }

    [Fact]
    public void AStrideWiderThanTheRowIsPackedAndASizeChangeIsFollowed()
    {
        var perceiver = new Marking();
        using var worker = new SenseWorker(perceiver, 8, 8, start: false);
        var padded = Picture(9, height: 2, stride: 16);
        worker.Offer(padded, 2, 2, 16, 1, false);
        worker.Step();
        worker.Offer(Picture(5, height: 4, stride: 16), 4, 4, 16, 2, false);
        worker.Step();

        var scene = new SceneSense(8, 8);
        Assert.True(worker.TryTake(scene, 1));
        Assert.Equal(5, scene.Matte[0]);
    }

    [Fact]
    public void AFailureIsKeptAndTheNextPictureIsStillPerceived()
    {
        var perceiver = new Marking { Throw = new InvalidOperationException("driver reset") };
        using var worker = new SenseWorker(perceiver, 8, 8, start: false);
        worker.Offer(Picture(1), 4, 2, 16, 1, false);

        Assert.True(worker.Step());
        Assert.Equal("driver reset", worker.LastError?.Message);
        Assert.Equal(0, worker.Perceived);
        Assert.False(worker.TryTake(new SceneSense(8, 8), 0));

        perceiver.Throw = null;
        worker.Offer(Picture(4), 4, 2, 16, 2, false);
        worker.Step();
        var scene = new SceneSense(8, 8);
        Assert.True(worker.TryTake(scene, 0));
        Assert.Equal(4, scene.Matte[0]);
    }

    [Fact]
    public void RunningOutOfMemoryIsNotSwallowed()
    {
        // InsufficientMemoryException is the out-of-memory a program may raise itself; the filter treats every kind alike.
        var perceiver = new Marking { Throw = new InsufficientMemoryException() };
        using var worker = new SenseWorker(perceiver, 8, 8, start: false);
        worker.Offer(Picture(1), 4, 2, 16, 1, false);

        Assert.Throws<InsufficientMemoryException>(() => worker.Step());
    }

    [Fact]
    public void TheThreadPerceivesOnItsOwnAndStopsWhenDisposed()
    {
        var perceiver = new Marking();
        var worker = new SenseWorker(perceiver, 8, 8);
        var scene = new SceneSense(8, 8);
        worker.Offer(Picture(6), 4, 2, 16, 10, false);
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!worker.TryTake(scene, 0) && DateTime.UtcNow < deadline)
        {
            Thread.Yield();
        }

        Assert.Equal(6, scene.Matte[0]);
        worker.Dispose();
        worker.Dispose();

        // Once stopped, offers are ignored and nothing is perceived.
        worker.Offer(Picture(8), 4, 2, 16, 20, false);
        Assert.False(worker.Step());
        Assert.Equal(1, perceiver.Calls);
    }

    [Fact]
    public async Task DisposingWaitsForThePictureInHand()
    {
        var cancellation = TestContext.Current.CancellationToken;
        using var hold = new ManualResetEventSlim(false);
        var perceiver = new Marking { Hold = hold };
        var worker = new SenseWorker(perceiver, 8, 8);
        worker.Offer(Picture(1), 4, 2, 16, 1, false);
        Assert.True(perceiver.Entered.Wait(TimeSpan.FromSeconds(10), cancellation));
        var disposed = Task.Run(worker.Dispose, cancellation);

        Assert.NotSame(disposed, await Task.WhenAny(disposed, Task.Delay(200, cancellation)));
        hold.Set();
        await disposed.WaitAsync(TimeSpan.FromSeconds(10), cancellation);
    }

    [Fact]
    public void AnUnstartedWorkerDisposesAtOnceAndBadArgumentsAreRefused()
    {
        new SenseWorker(new Marking(), start: false).Dispose();
        Assert.Throws<ArgumentNullException>(() => new SenseWorker(null!));
        using var worker = new SenseWorker(new Marking(), 8, 8, start: false);
        Assert.Throws<ArgumentNullException>(() => worker.TryTake(null!, 0));
        Assert.Throws<ArgumentException>(() => worker.Offer(new byte[4], 4, 2, 16, 1, false));
    }
}
