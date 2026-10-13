namespace Rex.Media.Vision;

/// <summary>
/// A segmenter shared between the threads that ask it (a click's worker, a reacquisition worker)
/// and the window that owns it: one call runs at a time, as the model requires, and disposing waits
/// for the call in flight before the model is released, so closing the window never frees a model
/// that is still running. A call after disposal throws <see cref="ObjectDisposedException"/>, which
/// callers treat as the model being unavailable.
/// </summary>
public sealed class SharedSegmenter : IPromptSegmenter
{
    private readonly IPromptSegmenter _model;
    private readonly Lock _gate = new();
    private bool _disposed;

    public SharedSegmenter(IPromptSegmenter model)
    {
        _model = model ?? throw new ArgumentNullException(nameof(model));
    }

    public ExecutionDevice Device => _model.Device;

    public TimeSpan LastEncode
    {
        get
        {
            lock (_gate)
            {
                return _model.LastEncode;
            }
        }
    }

    public TimeSpan LastDecode
    {
        get
        {
            lock (_gate)
            {
                return _model.LastDecode;
            }
        }
    }

    public PictureMask Segment(BgraPicture picture, PictureKey key, SegmentPrompt prompt)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _model.Segment(picture, key, prompt);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (!_disposed)
            {
                _disposed = true;
                _model.Dispose();
            }
        }
    }
}

/// <summary>An inpainter shared the same way as <see cref="SharedSegmenter"/>: one fill at a time, and disposal waits for it.</summary>
public sealed class SharedInpainter : IInpainter
{
    private readonly IInpainter _model;
    private readonly Lock _gate = new();
    private bool _disposed;

    public SharedInpainter(IInpainter model)
    {
        _model = model ?? throw new ArgumentNullException(nameof(model));
    }

    public ExecutionDevice Device => _model.Device;

    public TimeSpan LastFill
    {
        get
        {
            lock (_gate)
            {
                return _model.LastFill;
            }
        }
    }

    public void Fill(BgraPicture picture, PictureMask hole, Span<byte> output)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _model.Fill(picture, hole, output);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (!_disposed)
            {
                _disposed = true;
                _model.Dispose();
            }
        }
    }
}
