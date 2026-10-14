using Rex.Media.Engine;
using Rex.Media.Primitives;
using Rex.Media.TestKit;

namespace Rex.Media.Tests.Engine;

public sealed class AudioGapsTests
{
    private static readonly MediaTime Ten = MediaTime.FromSeconds(10);
    private static readonly MediaTime Minute = MediaTime.FromSeconds(60);

    [Fact]
    public void FramesThatFollowOnOrOverlapOrCarryNoTimeAreFollowed()
    {
        Assert.Equal(GapAction.Follow, AudioGaps.Classify(MediaTime.Unknown, Ten, Minute));
        Assert.Equal(GapAction.Follow, AudioGaps.Classify(Ten, MediaTime.Unknown, Minute));
        Assert.Equal(GapAction.Follow, AudioGaps.Classify(Ten, Ten + MediaTime.FromMilliseconds(2), Minute));
        Assert.Equal(GapAction.Follow, AudioGaps.Classify(Ten, MediaTime.FromSeconds(3), Minute));
    }

    [Fact]
    public void AShortHoleIsFilledWithSilenceAndALongOneJumpedOver()
    {
        Assert.Equal(GapAction.Conceal, AudioGaps.Classify(Ten, Ten + MediaTime.FromMilliseconds(3), Minute));
        Assert.Equal(GapAction.Conceal, AudioGaps.Classify(Ten, Ten + AudioGaps.LongestSilence, Minute));
        Assert.Equal(GapAction.Jump, AudioGaps.Classify(Ten, Ten + MediaTime.FromSeconds(12), Minute));
        Assert.Equal(GapAction.Jump, AudioGaps.Classify(Ten, Ten + MediaTime.FromSeconds(12), MediaTime.Unknown));
    }

    [Fact]
    public void ATimePastTheEndIsNotBelieved()
    {
        Assert.Equal(GapAction.Distrust, AudioGaps.Classify(Ten, MediaTime.FromSeconds(3600), Minute));
        Assert.Equal(GapAction.Jump, AudioGaps.Classify(Ten, Minute + AudioGaps.LongestSilence, Minute));
    }
}

public sealed class DemuxRecoveryTests
{
    private static readonly MediaTime Minute = MediaTime.FromSeconds(60);

    [Fact]
    public void TheFirstRefusalsReadAgainThenSeeksGoFurtherEachTime()
    {
        var recovery = new DemuxRecovery();
        Assert.False(recovery.Damaged);
        Assert.True(recovery.Keep(MediaTime.FromSeconds(10), video: false, keyframe: true));
        Assert.Equal(MediaTime.FromSeconds(10), recovery.Reached);

        Assert.Equal(RecoveryStep.ReadAgain, recovery.Failed(Minute, seekable: true));
        Assert.True(recovery.Damaged);
        Assert.Equal(RecoveryStep.ReadAgain, recovery.Failed(Minute, seekable: true));
        Assert.Equal(RecoveryStep.SeekPast, recovery.Failed(Minute, seekable: true));
        Assert.Equal(MediaTime.FromSeconds(11), recovery.Target);
        Assert.Equal(RecoveryStep.SeekPast, recovery.Failed(Minute, seekable: true));
        Assert.Equal(MediaTime.FromSeconds(12), recovery.Target);
        Assert.Equal(RecoveryStep.SeekPast, recovery.Failed(Minute, seekable: true));
        Assert.Equal(MediaTime.FromSeconds(14), recovery.Target);
        for (var i = 0; i < 3; i++)
        {
            Assert.Equal(RecoveryStep.SeekPast, recovery.Failed(Minute, seekable: true));
        }

        Assert.Equal(MediaTime.FromSeconds(42), recovery.Target);
        Assert.Equal(RecoveryStep.GiveUp, recovery.Failed(Minute, seekable: true));
    }

    [Fact]
    public void WithNothingReadYetSkipsAreMeasuredFromTheStart()
    {
        var recovery = new DemuxRecovery();
        recovery.Failed(Minute, seekable: true);
        recovery.Failed(Minute, seekable: true);
        Assert.Equal(RecoveryStep.SeekPast, recovery.Failed(Minute, seekable: true));
        Assert.Equal(MediaTime.FromSeconds(1), recovery.Target);
    }

    [Fact]
    public void AFileThatCannotSeekOrHasNoLengthGivesUpAfterReadingAgain()
    {
        foreach (var (duration, seekable) in new[] { (Minute, false), (MediaTime.Unknown, true) })
        {
            var recovery = new DemuxRecovery();
            recovery.Failed(duration, seekable);
            recovery.Failed(duration, seekable);
            Assert.Equal(RecoveryStep.GiveUp, recovery.Failed(duration, seekable));
        }
    }

    [Fact]
    public void AfterASkipNothingEarlierPlaysAndPicturesWaitForAKeyframe()
    {
        var recovery = new DemuxRecovery();
        recovery.Keep(MediaTime.FromSeconds(5), video: false, keyframe: true);
        recovery.Failed(Minute, seekable: true);
        recovery.Failed(Minute, seekable: true);
        recovery.Failed(Minute, seekable: true);
        Assert.Equal(MediaTime.FromSeconds(6), recovery.Target);

        Assert.False(recovery.Keep(MediaTime.FromSeconds(4), video: false, keyframe: true));
        Assert.False(recovery.Keep(MediaTime.FromSeconds(6.5), video: true, keyframe: false));
        Assert.True(recovery.Keep(MediaTime.FromSeconds(6.5), video: false, keyframe: true));
        Assert.True(recovery.Keep(MediaTime.FromSeconds(7), video: true, keyframe: true));
        Assert.True(recovery.Keep(MediaTime.FromSeconds(7.04), video: true, keyframe: false));
        Assert.True(recovery.Keep(MediaTime.Unknown, video: false, keyframe: true));
        Assert.Equal(MediaTime.FromSeconds(7.04), recovery.Reached);

        // A packet that plays ends the run of refusals: the next one reads again first.
        Assert.Equal(RecoveryStep.ReadAgain, recovery.Failed(Minute, seekable: true));
    }

    [Fact]
    public void ASeekForgetsTheSkip()
    {
        var recovery = new DemuxRecovery();
        for (var i = 0; i < 3; i++)
        {
            recovery.Failed(Minute, seekable: true);
        }

        recovery.Restart();
        Assert.Equal(MediaTime.Unknown, recovery.Reached);
        Assert.True(recovery.Keep(MediaTime.Zero, video: true, keyframe: false));
        Assert.Equal(RecoveryStep.ReadAgain, recovery.Failed(Minute, seekable: true));
    }
}

public sealed class AudioClockJumpTests
{
    [Fact]
    public void AJumpMovesTheClockWhenItsSampleIsHeardWithoutAnnouncingAnItem()
    {
        var sink = new ManualClockSink();
        var item = new object();
        var clock = new AudioClock(sink, item);
        clock.Rebase(MediaTime.Zero, 1000);
        clock.Wrote(1000);
        clock.Jump(MediaTime.FromSeconds(10));
        clock.Wrote(1000);

        sink.PlayedSamples = 500;
        Assert.Equal(MediaTime.FromMilliseconds(500), clock.Now);
        sink.PlayedSamples = 1500;
        Assert.Equal(MediaTime.FromMilliseconds(10_500), clock.Now);
        Assert.Equal(MediaTime.FromMilliseconds(10_500), clock.NowFor(item));
        Assert.Same(item, clock.Heard);
        Assert.Empty(clock.TakeStarted());
    }

    [Fact]
    public void AJumpAfterTheNextItemIsQueuedBelongsToThatItem()
    {
        var sink = new ManualClockSink();
        var first = new object();
        var second = new object();
        var clock = new AudioClock(sink, first);
        clock.Rebase(MediaTime.Zero, 1000);
        clock.Wrote(1000);
        clock.Continue(MediaTime.Zero, second);
        clock.Wrote(1000);
        clock.Jump(MediaTime.FromSeconds(5));

        sink.PlayedSamples = 2500;
        Assert.Same(second, clock.Heard);
        Assert.Equal(MediaTime.FromMilliseconds(5_500), clock.NowFor(second));
        Assert.Equal([second], clock.TakeStarted());
    }
}
