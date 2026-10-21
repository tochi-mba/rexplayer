namespace Rex.Media.AppCore.Player;

/// <summary>
/// Closes the window's parts in order, once. A failure in one part is reported and the rest still
/// close: the window closes inside a Windows event, where an exception that escapes ends the
/// process with a stowed exception instead of reaching the app's own handler.
/// </summary>
public sealed class WindowTeardown
{
    private readonly Action<string, Exception> _failed;

    /// <param name="failed">Hears the name of a part that failed to close, and why.</param>
    public WindowTeardown(Action<string, Exception> failed) => _failed = failed;

    /// <summary>True once <see cref="Run"/> has begun; work that arrives later must not touch the window.</summary>
    public bool Started { get; private set; }

    /// <summary>
    /// Runs each step in order the first time it is called and returns how many failed; a second
    /// call (Windows can raise Closed again) does nothing and returns -1.
    /// </summary>
    public int Run(IEnumerable<(string Name, Action Close)> steps)
    {
        if (Started)
        {
            return -1;
        }

        Started = true;
        var failures = 0;
        foreach (var (name, close) in steps)
        {
            try
            {
                close();
            }
            catch (Exception ex)
            {
                failures++;
                _failed(name, ex);
            }
        }

        return failures;
    }
}
