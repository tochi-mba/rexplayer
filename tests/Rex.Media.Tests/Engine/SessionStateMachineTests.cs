using Rex.Media.Engine;

namespace Rex.Media.Tests.Engine;

public sealed class SessionStateMachineTests
{
    private static readonly (SessionState From, SessionState To)[] Legal =
    [
        (SessionState.Idle, SessionState.Opening),
        (SessionState.Opening, SessionState.Ready), (SessionState.Opening, SessionState.Playing), (SessionState.Opening, SessionState.Stopping), (SessionState.Opening, SessionState.Faulted),
        (SessionState.Ready, SessionState.Playing), (SessionState.Ready, SessionState.Paused), (SessionState.Ready, SessionState.Buffering), (SessionState.Ready, SessionState.Seeking), (SessionState.Ready, SessionState.Stopping), (SessionState.Ready, SessionState.Faulted),
        (SessionState.Playing, SessionState.Paused), (SessionState.Playing, SessionState.Buffering), (SessionState.Playing, SessionState.Seeking), (SessionState.Playing, SessionState.Ended), (SessionState.Playing, SessionState.Stopping), (SessionState.Playing, SessionState.Faulted),
        (SessionState.Paused, SessionState.Playing), (SessionState.Paused, SessionState.Seeking), (SessionState.Paused, SessionState.Ended), (SessionState.Paused, SessionState.Stopping), (SessionState.Paused, SessionState.Faulted),
        (SessionState.Buffering, SessionState.Playing), (SessionState.Buffering, SessionState.Paused), (SessionState.Buffering, SessionState.Seeking), (SessionState.Buffering, SessionState.Stopping), (SessionState.Buffering, SessionState.Faulted),
        (SessionState.Seeking, SessionState.Playing), (SessionState.Seeking, SessionState.Paused), (SessionState.Seeking, SessionState.Buffering), (SessionState.Seeking, SessionState.Seeking), (SessionState.Seeking, SessionState.Ended), (SessionState.Seeking, SessionState.Stopping), (SessionState.Seeking, SessionState.Faulted),
        (SessionState.Ended, SessionState.Playing), (SessionState.Ended, SessionState.Seeking), (SessionState.Ended, SessionState.Stopping), (SessionState.Ended, SessionState.Faulted),
        (SessionState.Stopping, SessionState.Idle),
        (SessionState.Faulted, SessionState.Stopping),
    ];

    [Fact]
    public void EveryPairOfStatesIsLegalExactlyWhenTheTableSaysSo()
    {
        foreach (var from in Enum.GetValues<SessionState>())
        {
            foreach (var to in Enum.GetValues<SessionState>())
            {
                var legal = Legal.Contains((from, to));
                Assert.True(legal == SessionStateMachine.CanMove(from, to), $"{from} -> {to} should be {(legal ? "legal" : "illegal")}.");
                if (legal)
                {
                    SessionStateMachine.Ensure(from, to);
                }
                else
                {
                    var error = Assert.Throws<InvalidOperationException>(() => SessionStateMachine.Ensure(from, to));
                    Assert.Equal($"A media session cannot go from {from} to {to}.", error.Message);
                }
            }
        }
    }
}
