namespace Rex.Media.Vision;

/// <summary>Something that fills a <see cref="SceneSense"/> from a camera picture: the models, or rexplayer's own algorithms.</summary>
public interface IScenePerceiver
{
    void Perceive(BgraPicture picture, bool mirror, SceneSense scene, long timestamp);
}

/// <summary>
/// Runs perception beside the camera and the renderer, never in their way (ADR-0022). The camera
/// <see cref="Offer"/>s each picture, which replaces any picture not yet started: perception always
/// works on the newest, and a slow computer simply sees fewer pictures. The worker perceives into its
/// own scene and swaps it for the finished one; the renderer <see cref="TryTake"/>s the finished
/// scene when it is newer than what it has. Its own thread, below normal priority, so the sound and
/// the window come first. After the first picture of a size, nothing is allocated.
/// </summary>
public sealed class SenseWorker : IDisposable
{
    private readonly Lock _gate = new();
    private readonly IScenePerceiver _perceiver;
    private readonly Thread _thread;
    private readonly ManualResetEventSlim _wake = new(false);
    private byte[] _waiting = [];
    private byte[] _working = [];
    private (int Width, int Height, long Timestamp, bool Mirror) _waitingFrame;
    private bool _hasWaiting;
    private SceneSense _back;
    private SceneSense _front;
    private long _finished;
    private bool _stopping;

    public SenseWorker(IScenePerceiver perceiver, int width = SceneSense.DefaultWidth, int height = SceneSense.DefaultHeight, bool start = true)
    {
        _perceiver = perceiver ?? throw new ArgumentNullException(nameof(perceiver));
        _back = new SceneSense(width, height);
        _front = new SceneSense(width, height);
        _thread = new Thread(Run) { IsBackground = true, Name = "Camera perception", Priority = ThreadPriority.BelowNormal };
        if (start)
        {
            _thread.Start();
        }
    }

    /// <summary>Pictures perceived so far.</summary>
    public long Perceived { get; private set; }

    /// <summary>Pictures replaced by a newer one before perception reached them.</summary>
    public long Skipped { get; private set; }

    /// <summary>The last failure of the perceiver, for the log; perception goes on with the next picture.</summary>
    public Exception? LastError { get; private set; }

    /// <summary>
    /// Hands the worker the camera's newest BGRA picture (<paramref name="stride"/> bytes a row),
    /// copied, so the camera's buffer can be reused at once. A picture still waiting is replaced.
    /// </summary>
    public void Offer(ReadOnlySpan<byte> bgra, int width, int height, int stride, long timestamp, bool mirror)
    {
        var picture = new BgraPicture(bgra, width, height, stride);
        lock (_gate)
        {
            if (_stopping)
            {
                return;
            }

            var bytes = width * height * 4;
            if (_waiting.Length != bytes)
            {
                _waiting = new byte[bytes];
            }

            for (var y = 0; y < height; y++)
            {
                picture.Row(y).CopyTo(_waiting.AsSpan(y * width * 4));
            }

            if (_hasWaiting)
            {
                Skipped++;
            }

            (_waitingFrame, _hasWaiting) = ((width, height, timestamp, mirror), true);
        }

        _wake.Set();
    }

    /// <summary>
    /// Copies the newest finished scene into <paramref name="into"/> when it is newer than
    /// <paramref name="since"/> (a timestamp the caller last took); false, and nothing copied, otherwise.
    /// </summary>
    public bool TryTake(SceneSense into, long since)
    {
        ArgumentNullException.ThrowIfNull(into);
        lock (_gate)
        {
            if (_finished == 0 || _front.Timestamp <= since)
            {
                return false;
            }

            _front.CopyTo(into);
            return true;
        }
    }

    /// <summary>Perceives the waiting picture now, on the caller's thread; false when none waits. What the worker's thread does each time it wakes.</summary>
    public bool Step()
    {
        (int Width, int Height, long Timestamp, bool Mirror) frame;
        lock (_gate)
        {
            if (!_hasWaiting || _stopping)
            {
                return false;
            }

            // The waiting buffer becomes the working one; the camera fills the other next.
            (_working, _waiting) = (_waiting, _working);
            frame = _waitingFrame;
            _hasWaiting = false;

            // A perceiver that leaves part of the scene alone this picture (a model run every other
            // one) must find the newest scene there, not the one from two pictures ago.
            if (_finished > 0)
            {
                _front.CopyTo(_back);
            }
        }

        try
        {
            _perceiver.Perceive(new BgraPicture(_working, frame.Width, frame.Height, frame.Width * 4), frame.Mirror, _back, frame.Timestamp);
            _back.Timestamp = frame.Timestamp;
            lock (_gate)
            {
                (_front, _back) = (_back, _front);
                _finished++;
                Perceived++;
            }
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            // A model that fails on one picture (a driver reset, a runtime error) must not end
            // perception for good, nor take the app down from a background thread: the failure is
            // kept for the log and the next picture is tried.
            LastError = error;
        }

        return true;
    }

    private void Run()
    {
        while (true)
        {
            _wake.Wait();
            _wake.Reset();
            lock (_gate)
            {
                if (_stopping)
                {
                    return;
                }
            }

            while (Step())
            {
            }
        }
    }

    /// <summary>Stops the worker and waits for the picture in hand, so the perceiver can be released after.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_stopping)
            {
                return;
            }

            _stopping = true;
        }

        _wake.Set();
        if (_thread.IsAlive)
        {
            _thread.Join();
        }

        _wake.Dispose();
    }
}
