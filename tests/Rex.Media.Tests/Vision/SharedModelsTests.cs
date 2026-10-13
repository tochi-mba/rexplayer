using Rex.Media.Vision;

namespace Rex.Media.Tests.Vision;

/// <summary>Models shared between workers and the window: one call at a time, and disposal waits for it.</summary>
public sealed class SharedModelsTests
{
    private static readonly byte[] Pixels = new byte[8 * 6 * 4];

    private static BgraPicture Picture => new(Pixels, 8, 6, 32);

    /// <summary>A model whose calls block until released, counting how many run at once.</summary>
    private sealed class SlowModel : IPromptSegmenter, IInpainter
    {
        private int _running;

        public ManualResetEventSlim Started { get; } = new();

        public ManualResetEventSlim Release { get; } = new();

        public int MostAtOnce { get; private set; }

        public bool Disposed { get; private set; }

        public ExecutionDevice Device => ExecutionDevice.Gpu;

        public TimeSpan LastEncode => TimeSpan.FromSeconds(4);

        public TimeSpan LastDecode => TimeSpan.FromSeconds(1);

        public TimeSpan LastFill => TimeSpan.FromSeconds(3);

        public PictureMask Segment(BgraPicture picture, PictureKey key, SegmentPrompt prompt)
        {
            Run();
            return new PictureMask(new byte[picture.Width * picture.Height], picture.Width, picture.Height, 1);
        }

        public void Fill(BgraPicture picture, PictureMask hole, Span<byte> output) => Run();

        private void Run()
        {
            Assert.False(Disposed);
            MostAtOnce = Math.Max(MostAtOnce, Interlocked.Increment(ref _running));
            Started.Set();
            Release.Wait(TimeSpan.FromSeconds(10));
            Interlocked.Decrement(ref _running);
        }

        public void Dispose() => Disposed = true;
    }

    [Fact]
    public void ASharedSegmenterRunsOneCallAtATimeAndDisposalWaitsForIt()
    {
        Assert.Throws<ArgumentNullException>(() => new SharedSegmenter(null!));
        var model = new SlowModel();
        var shared = new SharedSegmenter(model);
        Assert.Equal(ExecutionDevice.Gpu, shared.Device);
        Assert.Equal(TimeSpan.FromSeconds(4), shared.LastEncode);
        Assert.Equal(TimeSpan.FromSeconds(1), shared.LastDecode);

        var calls = Enumerable.Range(0, 3).Select(_ => Task.Run(() => shared.Segment(Picture, new PictureKey(1, 1), SegmentPrompt.At(1, 1)))).ToArray();
        Assert.True(model.Started.Wait(TimeSpan.FromSeconds(10)));
        var disposal = Task.Run(shared.Dispose);
        Assert.False(disposal.Wait(TimeSpan.FromMilliseconds(100)));
        Assert.False(model.Disposed);
        model.Release.Set();
        Assert.True(disposal.Wait(TimeSpan.FromSeconds(10)));
        Assert.True(model.Disposed);
        Assert.Equal(1, model.MostAtOnce);

        // Calls that had not started when it was disposed find it gone; disposing again does nothing.
        var refused = calls.Count(call => call.Exception?.InnerException is ObjectDisposedException);
        Assert.True(refused <= 2);
        Assert.Throws<ObjectDisposedException>(() => shared.Segment(Picture, new PictureKey(1, 1), SegmentPrompt.At(1, 1)));
        shared.Dispose();
    }

    [Fact]
    public void ASharedInpainterRunsOneFillAtATimeAndDisposalWaitsForIt()
    {
        Assert.Throws<ArgumentNullException>(() => new SharedInpainter(null!));
        var model = new SlowModel();
        var shared = new SharedInpainter(model);
        Assert.Equal(ExecutionDevice.Gpu, shared.Device);
        Assert.Equal(TimeSpan.FromSeconds(3), shared.LastFill);
        var hole = new PictureMask(new byte[8 * 6], 8, 6, 1);

        var fill = Task.Run(() => shared.Fill(Picture, hole, new byte[Pixels.Length]));
        Assert.True(model.Started.Wait(TimeSpan.FromSeconds(10)));
        var disposal = Task.Run(shared.Dispose);
        Assert.False(disposal.Wait(TimeSpan.FromMilliseconds(100)));
        model.Release.Set();
        Assert.True(disposal.Wait(TimeSpan.FromSeconds(10)));
        Assert.True(fill.Wait(TimeSpan.FromSeconds(10)));
        Assert.True(model.Disposed);
        Assert.Throws<ObjectDisposedException>(() => shared.Fill(Picture, hole, new byte[Pixels.Length]));
        shared.Dispose();
    }
}
