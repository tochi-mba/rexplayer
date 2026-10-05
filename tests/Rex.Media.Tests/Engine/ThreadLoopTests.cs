using System.Runtime.InteropServices;
using Rex.Media.Engine;
using Rex.Media.TestKit;

namespace Rex.Media.Tests.Engine;

public sealed class ThreadLoopTests
{
    [Fact]
    public void ACompletedBodyReportsNothing()
    {
        var failures = new List<Exception>();

        ThreadLoop.Run(() => { }, failures.Add, CancellationToken.None);

        Assert.Empty(failures);
    }

    [Fact]
    public void CancellationOfTheSessionEndsQuietly()
    {
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        var failures = new List<Exception>();

        ThreadLoop.Run(() => cancel.Token.ThrowIfCancellationRequested(), failures.Add, cancel.Token);

        Assert.Empty(failures);
    }

    [Fact]
    public void ACancellationThatIsNotTheSessionsIsNotSwallowed()
    {
        using var other = new CancellationTokenSource();
        other.Cancel();

        Assert.Throws<OperationCanceledException>(() => ThreadLoop.Run(() => other.Token.ThrowIfCancellationRequested(), _ => { }, CancellationToken.None));
    }

    [Theory]
    [InlineData(typeof(IOException))]
    [InlineData(typeof(UnauthorizedAccessException))]
    [InlineData(typeof(InvalidOperationException))]
    [InlineData(typeof(COMException))]
    public void OutsideWorldFailuresAreReported(Type type)
    {
        var failure = (Exception)Activator.CreateInstance(type)!;
        var failures = new List<Exception>();

        ThreadLoop.Run(() => throw failure, failures.Add, CancellationToken.None);

        Assert.Same(failure, Assert.Single(failures));
    }

    [Fact]
    public void ProgrammingErrorsAreNotHidden()
    {
        Assert.Throws<ArgumentNullException>(() => ThreadLoop.Run(() => throw new ArgumentNullException("x"), _ => { }, CancellationToken.None));
    }
}

public sealed class SessionShutdownTests
{
    [Fact]
    public async Task StoppingWhileTheQueueIsFullEndsBothThreads()
    {
        using var proceed = new ManualResetEventSlim(false);
        var sink = new RecordingAudioSink(channels: 1);
        sink.Writing += _ => proceed.Wait(TestContext.Current.CancellationToken);
        using var harness = new SessionHarness(sink: sink);
        await harness.Session.OpenAsync(SessionHarness.Source(Pcm.RampWav(8000, 8000 * 30)));
        await Task.Delay(300, TestContext.Current.CancellationToken);

        var stop = harness.Session.StopAsync();
        proceed.Set();
        await stop;

        Assert.Equal(Rex.Media.Engine.SessionState.Idle, harness.Session.State);
    }

    [Fact]
    public async Task AnAudioOutputThatFailsFaultsTheSession()
    {
        var sink = new RecordingAudioSink(channels: 1);
        sink.Writing += _ => throw new IOException("the device was unplugged");
        using var harness = new SessionHarness(sink: sink);

        await harness.Session.OpenAsync(SessionHarness.Source(Pcm.RampWav(8000, 8000)));
        await harness.FinishAsync();

        Assert.Equal(Rex.Media.Engine.SessionState.Faulted, harness.Session.State);
        Assert.Equal("The audio output failed: IOException: the device was unplugged", harness.WaitFor<ErrorEvent>().Message);
    }

    [Fact]
    public async Task AnEndReportedAfterANewerSeekIsIgnored()
    {
        var sink = new GatedDrainSink();
        using var harness = new SessionHarness(sink: sink);
        await harness.Session.OpenAsync(SessionHarness.Source(Pcm.RampWav(8000, 800)));
        Assert.True(sink.Draining.Wait(5000, TestContext.Current.CancellationToken));

        await harness.Session.SeekAsync(Rex.Media.Primitives.MediaTime.FromMilliseconds(50));
        sink.Release.Set();
        harness.WaitFor<SeekCompletedEvent>();
        harness.WaitFor<EndedEvent>();

        Assert.Single(harness.Events.OfType<EndedEvent>());
    }
}
