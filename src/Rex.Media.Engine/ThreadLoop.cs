namespace Rex.Media.Engine;

/// <summary>
/// The body of a pipeline thread. Cancellation of the session ends it quietly; a failure of the
/// outside world (a disk, a network, a device) is reported instead of tearing the process down,
/// because an exception escaping a thread would end the whole app.
/// </summary>
internal static class ThreadLoop
{
    public static void Run(Action body, Action<Exception> onFailure, CancellationToken token)
    {
        try
        {
            body();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // The session is closing; there is nothing to report.
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.Runtime.InteropServices.ExternalException)
        {
            onFailure(ex);
        }
    }
}
