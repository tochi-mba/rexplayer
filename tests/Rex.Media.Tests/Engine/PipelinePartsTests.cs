using Rex.Media.Engine;
using Rex.Media.Primitives;
using Rex.Media.TestKit;

namespace Rex.Media.Tests.Engine;

public sealed class PipelinePartsTests
{
    private sealed class Item : IDisposable
    {
        public bool Disposed { get; private set; }

        public void Dispose() => Disposed = true;
    }

    [Fact]
    public void TheQueueDeliversInOrderAndEndsAfterClosing()
    {
        using var queue = new BoundedQueue<Item>(4);
        var first = new Item();
        queue.Add(new QueueItem<Item>(first, false, 0), CancellationToken.None);
        queue.Add(new QueueItem<Item>(null, true, 0), CancellationToken.None);
        queue.Close();

        Assert.Equal(2, queue.Count);
        Assert.True(queue.TryTake(out var taken, CancellationToken.None));
        Assert.Same(first, taken.Payload);
        Assert.True(queue.TryTake(out var end, CancellationToken.None));
        Assert.True(end.EndOfStream);
        Assert.False(queue.TryTake(out _, CancellationToken.None));
        var refused = new Item();
        Assert.False(queue.Add(new QueueItem<Item>(refused, false, 0), CancellationToken.None));
        Assert.True(refused.Disposed);
        Assert.False(queue.Add(new QueueItem<Item>(null, true, 0), CancellationToken.None));
    }

    [Fact]
    public void FlushingDisposesQueuedItemsAndRejectsOlderGenerations()
    {
        using var queue = new BoundedQueue<Item>(4);
        var stale = new Item();
        queue.Add(new QueueItem<Item>(stale, false, 0), CancellationToken.None);

        queue.Flush(3);
        var late = new Item();
        Assert.True(queue.Add(new QueueItem<Item>(late, false, 2), CancellationToken.None));
        queue.Flush(1);

        Assert.True(stale.Disposed);
        Assert.True(late.Disposed);
        Assert.Equal(0, queue.Count);
        Assert.Equal(3, queue.Generation);
    }

    [Fact]
    public async Task AFullQueueBlocksTheProducerUntilAFlushReleasesIt()
    {
        using var queue = new BoundedQueue<Item>(1);
        queue.Add(new QueueItem<Item>(new Item(), false, 0), CancellationToken.None);
        var blocked = new Item();
        var producer = Task.Run(() => queue.Add(new QueueItem<Item>(blocked, false, 0), CancellationToken.None), TestContext.Current.CancellationToken);

        await Task.Delay(100, TestContext.Current.CancellationToken);
        Assert.False(producer.IsCompleted);
        queue.Flush(1);

        Assert.True(await producer.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        Assert.True(blocked.Disposed);
    }

    [Fact]
    public async Task AWaitingTakerReceivesTheNextItem()
    {
        using var queue = new BoundedQueue<Item>(4);
        var arriving = new Item();
        var consumer = Task.Run(
            () =>
            {
                Assert.True(queue.TryTake(out var item, CancellationToken.None));
                return item.Payload;
            },
            TestContext.Current.CancellationToken);

        await Task.Delay(50, TestContext.Current.CancellationToken);
        Assert.False(consumer.IsCompleted);
        queue.Add(new QueueItem<Item>(arriving, false, 0), CancellationToken.None);

        Assert.Same(arriving, await consumer.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task WaitsCanBeCancelled()
    {
        using var full = new BoundedQueue<Item>(1);
        using var empty = new BoundedQueue<Item>(1);
        using var cancel = new CancellationTokenSource();
        full.Add(new QueueItem<Item>(new Item(), false, 0), CancellationToken.None);
        var adder = Task.Run(() => full.Add(new QueueItem<Item>(new Item(), false, 0), cancel.Token), CancellationToken.None);
        var taker = Task.Run(() => empty.TryTake(out _, cancel.Token), CancellationToken.None);
        await Task.Delay(50, TestContext.Current.CancellationToken);
        Assert.False(adder.IsCompleted);

        await cancel.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => adder);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => taker);
        Assert.Throws<OperationCanceledException>(() => full.Add(new QueueItem<Item>(new Item(), false, 0), cancel.Token));
        Assert.Throws<ArgumentOutOfRangeException>(() => new BoundedQueue<Item>(0));
    }

    [Fact]
    public void DisposingTheQueueDisposesWhatItHolds()
    {
        var queue = new BoundedQueue<Item>(2);
        var held = new Item();
        queue.Add(new QueueItem<Item>(held, false, 0), CancellationToken.None);

        queue.Dispose();

        Assert.True(held.Disposed);
    }

    [Fact]
    public void TheEventPumpDeliversInOrderAndCoalescesPositions()
    {
        var seen = new List<SessionEvent>();
        using var release = new ManualResetEventSlim(false);
        using var busy = new ManualResetEventSlim(false);
        var pump = new EventPump(sessionEvent =>
        {
            busy.Set();
            release.Wait(TestContext.Current.CancellationToken);
            lock (seen)
            {
                seen.Add(sessionEvent);
            }
        });

        pump.Post(new StateChangedEvent(SessionState.Idle, SessionState.Opening));
        Assert.True(busy.Wait(5000, TestContext.Current.CancellationToken));
        pump.Post(new PositionEvent(MediaTime.FromSeconds(1), MediaTime.Unknown));
        pump.Post(new PositionEvent(MediaTime.FromSeconds(2), MediaTime.Unknown));
        pump.Post(new StatsEvent(new SessionStats()));
        pump.Post(new StatsEvent(new SessionStats { PacketsRead = 3 }));
        pump.Post(new EndedEvent());
        release.Set();
        pump.Dispose();

        Assert.IsType<StateChangedEvent>(seen[0]);
        Assert.IsType<EndedEvent>(seen[1]);
        Assert.Equal(MediaTime.FromSeconds(2), Assert.IsType<PositionEvent>(seen[2]).Position);
        Assert.Equal(3, Assert.IsType<StatsEvent>(seen[3]).Stats.PacketsRead);
        Assert.Equal(4, seen.Count);
        Assert.Throws<ArgumentNullException>(() => new EventPump(null!));
    }

    [Fact]
    public void ThePumpRefusesANullEvent()
    {
        using var pump = new EventPump(_ => { });

        Assert.Throws<ArgumentNullException>(() => pump.Post(null!));
    }

    [Fact]
    public void TheAudioClockIsTheAnchorPlusWhatTheSinkHasPlayed()
    {
        var sink = new RecordingAudioSink(channels: 1);
        sink.Open(new AudioFormat(48_000, 1, SampleFormat.F32));
        var clock = new AudioClock(sink);
        clock.Rebase(MediaTime.FromSeconds(10), 48_000);
        using var frame = AudioFrame.Rent(48_000, 1, 24_000);

        sink.Write(frame, CancellationToken.None);

        Assert.Equal(MediaTime.FromSeconds(10.5), clock.Now);
        Assert.Throws<ArgumentOutOfRangeException>(() => clock.Rebase(MediaTime.Zero, 0));
        Assert.Throws<ArgumentNullException>(() => new AudioClock(null!));
    }

    [Fact]
    public void TheSystemClockRunsStopsAndScales()
    {
        var time = new ManualTimeProvider();
        var clock = new SystemClock(time);

        time.Advance(TimeSpan.FromSeconds(5));
        Assert.Equal(MediaTime.Zero, clock.Now);
        Assert.False(clock.IsRunning);

        clock.Start();
        clock.Start();
        time.Advance(TimeSpan.FromSeconds(2));
        Assert.Equal(MediaTime.FromSeconds(2), clock.Now);
        Assert.True(clock.IsRunning);

        clock.Rate = 2;
        time.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(MediaTime.FromSeconds(4), clock.Now);
        Assert.Equal(2, clock.Rate);

        clock.Stop();
        time.Advance(TimeSpan.FromSeconds(10));
        Assert.Equal(MediaTime.FromSeconds(4), clock.Now);

        clock.Rebase(MediaTime.FromSeconds(30));
        Assert.Equal(MediaTime.FromSeconds(30), clock.Now);
        Assert.Throws<ArgumentOutOfRangeException>(() => clock.Rate = 0);
        Assert.True(new SystemClock().Now >= MediaTime.Zero);
    }
}
