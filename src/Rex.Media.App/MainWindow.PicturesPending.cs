namespace Rex.Media.App;

/// <summary>
/// How many picture requests (a card's, a banner's, a playlist line's) are queued or in flight. The
/// automation pipe's <c>status</c> reports it as <c>picturesPending</c>, so a script or a screenshot
/// test can wait until the pictures on screen have settled instead of racing them.
/// </summary>
public sealed partial class MainWindow
{
    private int _picturesPending;

    /// <summary>Counts one picture request until the returned handle is disposed.</summary>
    private PendingPicture PictureStarted()
    {
        Interlocked.Increment(ref _picturesPending);
        return new PendingPicture(this);
    }

    /// <summary>The picture requests not yet finished, cancelled or failed.</summary>
    private int PicturesPending => Volatile.Read(ref _picturesPending);

    private readonly struct PendingPicture(MainWindow window) : IDisposable
    {
        public void Dispose() => Interlocked.Decrement(ref window._picturesPending);
    }
}
