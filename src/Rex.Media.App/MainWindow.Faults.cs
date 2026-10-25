using Rex.Media.AppCore.Automation;

namespace Rex.Media.App;

/// <summary>
/// With REXPLAYER_FAKE_FAULTS=1 the window tests can make the window's own code fail where Windows
/// calls it back (the <c>fault</c> request): work queued from another thread ("queued"), a
/// clock's tick ("clock"), a task nobody waits for ("task") and an event handler that has awaited
/// ("handler"). Each proves a failure there is written down with its stack while the player
/// carries on. Without the variable the request is refused like any unknown one.
/// </summary>
public sealed partial class MainWindow
{
    private static readonly bool FakeFaults = Environment.GetEnvironmentVariable("REXPLAYER_FAKE_FAULTS") == "1";

    /// <summary>Makes the failure a test asks for; null for every other request, or without the variable.</summary>
    private Task<AutomationReply>? AnswerFault(AutomationRequest request)
    {
        if (!FakeFaults || request.Command != "fault")
        {
            return null;
        }

        var failure = new InvalidOperationException($"A test's {request.Id} failure.");
        switch (request.Id)
        {
            case "queued":
                OnWindowThread(() => throw failure);
                break;
            case "clock":
                OnWindowThread(() =>
                {
                    var timer = CreateTimer();
                    timer.Interval = TimeSpan.FromMilliseconds(10);
                    timer.IsRepeating = false;
                    OnTick(timer, () => throw failure);
                    timer.Start();
                });
                break;
            case "task":
                Forget(FailLaterAsync(failure));
                break;
            case "handler":
                OnWindowThread(() => FailingHandler(failure));
                break;
            default:
                return Task.FromResult(AutomationReply.Failed($"There is no fault \"{request.Id}\"."));
        }

        return Task.FromResult(new AutomationReply { Ok = true });
    }

    private static async Task FailLaterAsync(Exception failure)
    {
        await Task.Yield();
        throw failure;
    }

    /// <summary>An event handler that throws once it has awaited, as a click that opens a picker could.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "VSTHRD100", Justification = "It stands for the event handlers that must be async void.")]
    private static async void FailingHandler(Exception failure)
    {
        await Task.Yield();
        throw failure;
    }
}
