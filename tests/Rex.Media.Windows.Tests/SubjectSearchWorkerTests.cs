using System.Runtime.Versioning;
using Rex.Media.Primitives;
using Rex.Media.Video.D3D11;

namespace Rex.Media.Windows.Tests;

[SupportedOSPlatform("windows8.0")]
public sealed class SubjectSearchWorkerTests
{
    private sealed class ControlledScheduler : TaskScheduler
    {
        private readonly Queue<Task> _pending = new();
        public int Pending => _pending.Count;
        protected override IEnumerable<Task> GetScheduledTasks() => _pending.ToArray();
        protected override void QueueTask(Task task) => _pending.Enqueue(task);
        protected override bool TryExecuteTaskInline(Task task, bool taskWasPreviouslyQueued) => false;
        public void RunNext() => Assert.True(TryExecuteTask(_pending.Dequeue()));
    }

    private static VideoFrame Picture(double seconds, int x = -50)
    {
        var frame = VideoFrame.Rent(PixelFormat.Bgra32, 128, 80);
        frame.Pts = MediaTime.FromSeconds(seconds);
        for (var y = 0; y < frame.Height; y++)
        {
            var row = frame.Row(0, y);
            for (var col = 0; col < frame.Width; col++)
            {
                var target = col >= x && col < x + 16 && y >= 20 && y < 36;
                row[col * 4] = (byte)(target ? 130 + (col - x) % 3 * 20 : 25);
                row[col * 4 + 1] = (byte)(target ? 30 + (y - 20) % 4 * 20 : 40);
                row[col * 4 + 2] = (byte)(target ? 210 : 60);
                row[col * 4 + 3] = 255;
            }
        }
        return frame;
    }

    private static SubjectEditSession Lost(ControlledScheduler scheduler)
    {
        var session = new SubjectEditSession(true, scheduler);
        session.Select(30 / 128f, 20 / 80f, 16 / 128f, 16 / 80f);
        using var first = Picture(1, 30);
        session.Process(first);
        using var missing = Picture(1.04);
        session.Process(missing);
        Assert.False(session.Tracking);
        return session;
    }

    [Fact]
    public void ABlockedSearchNeverBlocksPresentationOrQueuesMorePictures()
    {
        var scheduler = new ControlledScheduler();
        using var session = Lost(scheduler);
        for (var i = 0; i < 60; i++)
        {
            using var missing = Picture(1.08 + i * 0.04);
            session.Process(missing);
            Assert.InRange(scheduler.Pending, 0, 1);
            Assert.False(session.Tracking);
        }
        Assert.Equal(1, scheduler.Pending);
        // The caller has already disposed every input frame. The worker owns its copy.
        scheduler.RunNext();
        session.Reset();
        Assert.False(session.Locked);
    }

    [Fact]
    public void ResetAndReselectionCannotPublishAnOldSearchResult()
    {
        var scheduler = new ControlledScheduler();
        using var session = Lost(scheduler);
        using var returned = Picture(1.08, 70);
        session.Process(returned);
        returned.Pts = MediaTime.FromSeconds(1.12);
        session.Process(returned);
        Assert.Equal(1, scheduler.Pending);
        session.Select(10 / 128f, 20 / 80f, 16 / 128f, 16 / 80f);
        scheduler.RunNext();
        using var chosen = Picture(1.16, 10);
        session.Process(chosen);
        Assert.True(session.Tracking);
        Assert.Equal(10 / 128f, session.Region.Left);
        Assert.False(session.Erase);
    }

    [Fact]
    public void AsyncRecoveryNeedsTwoSightingsAndVerifiesTheCurrentPicture()
    {
        var scheduler = new ControlledScheduler();
        using var session = Lost(scheduler);
        for (var i = 0; i < 8 && !session.Tracking; i++)
        {
            using var returned = Picture(1.08 + i * 0.04, 70);
            session.Process(returned);
            if (scheduler.Pending > 0) scheduler.RunNext();
            if (i < 3) Assert.False(session.Tracking);
        }
        Assert.True(session.Tracking);
        Assert.InRange(session.Region.Left * 128, 68, 72);
        Assert.False(session.Erase);
        session.Reset();
    }

    [Fact]
    public void ASeekInvalidatesAnOutstandingSearchBeforeItCanComplete()
    {
        var scheduler = new ControlledScheduler();
        using var session = Lost(scheduler);
        using var frame = Picture(1.08, 70);
        session.Process(frame);
        frame.Pts = MediaTime.FromSeconds(1.12);
        session.Process(frame);
        Assert.Equal(1, scheduler.Pending);
        frame.Generation = 1;
        session.Process(frame);
        Assert.False(session.Locked);
        scheduler.RunNext();
        session.Process(frame);
        Assert.False(session.Tracking);
    }
    [Fact]
    public void PendingSearchCanSkipReadbackButNeverAcrossASeekOrResize()
    {
        var scheduler = new ControlledScheduler();
        using var session = Lost(scheduler);
        using var frame = Picture(1.08, 70);
        session.Process(frame);
        frame.Pts = MediaTime.FromSeconds(1.12);
        session.Process(frame);
        Assert.Equal(1, scheduler.Pending);
        frame.Pts = MediaTime.FromSeconds(1.16);
        Assert.True(session.CanPresentWithoutAnalysis(frame));
        frame.Pts = MediaTime.FromSeconds(1.10);
        Assert.False(session.CanPresentWithoutAnalysis(frame));
        frame.Pts = MediaTime.FromSeconds(4);
        Assert.False(session.CanPresentWithoutAnalysis(frame));
        frame.Pts = MediaTime.FromSeconds(1.20);
        frame.Generation = 1;
        Assert.False(session.CanPresentWithoutAnalysis(frame));
        using var resized = VideoFrame.Rent(PixelFormat.Bgra32, 64, 40);
        resized.Pts = frame.Pts;
        Assert.False(session.CanPresentWithoutAnalysis(resized));
        session.Dispose();
        scheduler.RunNext();
        Assert.False(session.Locked);
    }

    [Fact]
    public void AStaleSightingCannotCountTowardRecovery()
    {
        var scheduler = new ControlledScheduler();
        using var session = Lost(scheduler);
        using var frame = Picture(1.08, 70);
        session.Process(frame);
        frame.Pts = MediaTime.FromSeconds(1.12);
        session.Process(frame);
        scheduler.RunNext();
        frame.Pts = MediaTime.FromSeconds(1.8);
        session.Process(frame);
        frame.Pts = MediaTime.FromSeconds(1.84);
        session.Process(frame);
        Assert.False(session.Tracking);
        Assert.Equal(1, scheduler.Pending);
        scheduler.RunNext();
        for (var i = 0; i < 2; i++)
        {
            frame.Pts = MediaTime.FromSeconds(1.88 + i * 0.04);
            session.Process(frame);
            Assert.False(session.Tracking);
        }
        Assert.Equal(1, scheduler.Pending);
        session.Reset();
        scheduler.RunNext();
    }

    [Fact]
    public void ARecoveredSnapshotCannotLockAPersonWhoHasAlreadyLeft()
    {
        var scheduler = new ControlledScheduler();
        using var session = Lost(scheduler);
        for (var i = 0; i < 4; i++)
        {
            using var returned = Picture(1.08 + i * 0.04, 70);
            session.Process(returned);
            if (scheduler.Pending > 0) scheduler.RunNext();
        }
        for (var i = 0; i < 2; i++)
        {
            using var missing = Picture(1.24 + i * 0.04);
            session.Process(missing);
            Assert.False(session.Tracking);
            Assert.False(session.Erase);
        }
        session.Reset();
        if (scheduler.Pending > 0) scheduler.RunNext();
    }
}
