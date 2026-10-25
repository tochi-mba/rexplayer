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
        // A malformed file can expose an unexpected parser or decoder exception. Allowing it
        // to escape the pipeline thread terminates the whole process. The session still reports
        // the underlying exception through its normal fault path; it must not silently vanish.
#pragma warning disable CA1031 // This is the outermost thread safety boundary.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            onFailure(ex);
        }
    }
}
