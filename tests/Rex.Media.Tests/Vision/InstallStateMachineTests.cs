using Rex.Media.Vision;

namespace Rex.Media.Tests.Vision;

public sealed class InstallStateMachineTests
{
    [Fact]
    public void ADownloadMovesThroughVerifyingToInstalled()
    {
        var machine = new InstallStateMachine();
        var seen = new List<InstallStatus>();
        machine.Changed += seen.Add;

        machine.Begin(100);
        machine.Report(40);
        Assert.Equal(0.4, machine.Status.Fraction, 6);
        machine.Report(500);
        Assert.Equal(100, machine.Status.Done);
        machine.Report(-5);
        Assert.Equal(0, machine.Status.Done);
        machine.Verify();
        Assert.Equal((InstallPhase.Verifying, 100L), (machine.Status.Phase, machine.Status.Done));
        Assert.True(machine.Status.Busy);
        machine.Complete();

        Assert.Equal(InstallPhase.Installed, machine.Status.Phase);
        Assert.False(machine.Status.Busy);
        Assert.Equal(6, seen.Count);
        Assert.Equal(0, new InstallStatus(InstallPhase.NotInstalled).Fraction);
    }

    [Theory]
    [InlineData(InstallPhase.Downloading)]
    [InlineData(InstallPhase.Verifying)]
    [InlineData(InstallPhase.Failed)]
    public void OnlyAPhaseFoundOnDiskCanStartTheMachine(InstallPhase phase)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new InstallStateMachine(phase));
        Assert.Throws<ArgumentOutOfRangeException>(() => new InstallStateMachine().Refresh(phase));
    }

    [Fact]
    public void MovesFromTheWrongPhaseAreRefused()
    {
        var machine = new InstallStateMachine(InstallPhase.Installed);

        Assert.Throws<InvalidOperationException>(() => machine.Begin(10));
        Assert.Throws<InvalidOperationException>(() => machine.Report(1));
        Assert.Throws<InvalidOperationException>(() => machine.Verify());
        Assert.Throws<InvalidOperationException>(() => machine.Complete());
        Assert.Throws<InvalidOperationException>(() => machine.Fail("no"));
        Assert.Throws<InvalidOperationException>(() => machine.Cancel());
        Assert.Throws<ArgumentOutOfRangeException>(() => new InstallStateMachine().Begin(0));
        Assert.Throws<ArgumentException>(() => new InstallStateMachine().Fail(" "));

        var busy = new InstallStateMachine();
        busy.Begin(10);
        Assert.Throws<InvalidOperationException>(() => busy.Begin(10));
        Assert.Throws<InvalidOperationException>(() => busy.Removed());
        Assert.Throws<InvalidOperationException>(() => busy.Refresh(InstallPhase.Installed));
        busy.Verify();
        Assert.Throws<InvalidOperationException>(() => busy.Report(5));
    }

    [Fact]
    public void AFailureCanBeRetriedAndACancelGoesBackToWhereItStarted()
    {
        var machine = new InstallStateMachine(InstallPhase.UpdateAvailable);

        machine.Begin(10);
        machine.Fail("The connection failed.");
        Assert.Equal((InstallPhase.Failed, "The connection failed."), (machine.Status.Phase, machine.Status.Reason));
        machine.Begin(10);
        machine.Verify();
        machine.Cancel();

        Assert.Equal(InstallPhase.UpdateAvailable, machine.Status.Phase);
    }

    [Fact]
    public void RemovingAndRefreshingSetTheRestingPhase()
    {
        var machine = new InstallStateMachine(InstallPhase.Installed);

        machine.Removed();
        Assert.Equal(InstallPhase.NotInstalled, machine.Status.Phase);
        machine.Refresh(InstallPhase.Installed);
        Assert.Equal(InstallPhase.Installed, machine.Status.Phase);
        machine.Refresh(InstallPhase.UpdateAvailable);
        machine.Begin(5);
        machine.Cancel();
        Assert.Equal(InstallPhase.UpdateAvailable, machine.Status.Phase);
    }
}
