using Rex.Media.AppCore.Metadata;

namespace Rex.Media.Tests.AppCore;

/// <summary>
/// The fetch queue: jobs run in order and only once, never while playback could stutter, a failure
/// backs the whole queue off with a doubling delay capped at an hour, and a success clears the run.
/// </summary>
public sealed class FetchPlanTests
{
    private static readonly DateTimeOffset Noon = new(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);

    private static readonly MetadataJob Show = new(MetadataWork.TvShow, "The Show");
    private static readonly MetadataJob Album = new(MetadataWork.Music, @"D:\Music\Album");

    [Fact]
    public void JobsRunInOrderAndOnlyOnce()
    {
        var queue = new FetchPlan();
        Assert.Null(queue.Next(Noon));
        Assert.True(queue.Add(Show));
        Assert.True(queue.Add(Album));
        Assert.False(queue.Add(Show));
        Assert.Equal(2, queue.Count);

        Assert.Equal(Show, queue.Next(Noon));
        Assert.False(queue.Add(Show));
        queue.Done(Show);
        Assert.Equal(1, queue.Count);
        Assert.True(queue.Add(Show));

        Assert.Equal(Album, queue.Next(Noon));
        Assert.Equal(Show, queue.Next(Noon));
        Assert.Null(queue.Next(Noon));
    }

    [Fact]
    public void NothingIsHandedOutWhilePlaying()
    {
        var queue = new FetchPlan();
        queue.Add(Show);

        Assert.Null(queue.Next(Noon, playing: true));
        Assert.Equal(Show, queue.Next(Noon, playing: false));
    }

    [Fact]
    public void AFailureBacksTheWholeQueueOffDoubling()
    {
        var queue = new FetchPlan();
        queue.Add(Show);
        queue.Add(Album);

        Assert.Equal(Show, queue.Next(Noon));
        queue.Failed(Show, Noon);
        Assert.Equal(2, queue.Count);
        Assert.Null(queue.Next(Noon));
        Assert.Equal(Noon + FetchPlan.FirstDelay, queue.HeldUntil(Noon));
        Assert.Null(queue.HeldUntil(Noon + TimeSpan.FromMinutes(2)));

        // The failed job went to the back; the next failure doubles the wait.
        var later = Noon + TimeSpan.FromMinutes(2);
        Assert.Equal(Album, queue.Next(later));
        queue.Failed(Album, later);
        Assert.Equal(later + 2 * FetchPlan.FirstDelay, queue.HeldUntil(later));
    }

    [Fact]
    public void TheBackoffCapsAtAnHourAndASuccessClearsIt()
    {
        var queue = new FetchPlan();
        queue.Add(Show);
        var now = Noon;
        for (var i = 0; i < 12; i++)
        {
            now += TimeSpan.FromHours(2);
            Assert.Equal(Show, queue.Next(now));
            queue.Failed(Show, now);
        }

        Assert.Equal(now + FetchPlan.LongestDelay, queue.HeldUntil(now));

        now += TimeSpan.FromHours(2);
        Assert.Equal(Show, queue.Next(now));
        queue.Done(Show);
        queue.Add(Album);
        Assert.Equal(Album, queue.Next(now));
        queue.Failed(Album, now);
        Assert.Equal(now + FetchPlan.FirstDelay, queue.HeldUntil(now));
    }

    [Fact]
    public void ADroppedJobIsForgottenWhereverItIs()
    {
        var queue = new FetchPlan();
        queue.Add(Show);
        queue.Add(Album);

        queue.Drop(Show);
        Assert.Equal(1, queue.Count);
        Assert.Equal(Album, queue.Next(Noon));
        Assert.True(queue.Add(Show));

        // Dropping a job already handed out, and one never queued, changes nothing else.
        var handed = queue.Next(Noon);
        Assert.Equal(Show, handed);
        queue.Drop(Show);
        queue.Drop(new MetadataJob(MetadataWork.Movie, "never queued"));
        Assert.Equal(1, queue.Count);
        Assert.Null(queue.Next(Noon));
        queue.Done(Album);
        Assert.Equal(0, queue.Count);
    }
}
