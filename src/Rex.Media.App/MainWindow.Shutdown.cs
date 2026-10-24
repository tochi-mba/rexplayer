namespace Rex.Media.App;

public sealed partial class MainWindow
{
    private int _lateExceptions;

    /// <summary>Records unexpected exceptions raised after window teardown begins.</summary>
    private void LogLateException(object? sender, System.Runtime.ExceptionServices.FirstChanceExceptionEventArgs e)
    {
        // Closing cancels the automation pipe after this window's teardown handler.
        // Its normal first-chance cancellation must not race process shutdown by logging.
        if (e.Exception is OperationCanceledException)
        {
            return;
        }

        if (Interlocked.Increment(ref _lateExceptions) <= 10)
        {
            App.Log.Critical(LogSource, $"While closing: {e.Exception.GetType().Name}: {e.Exception.Message} (0x{e.Exception.HResult:X8}) at{Environment.NewLine}{new System.Diagnostics.StackTrace(1, false)}");
        }
    }
}
