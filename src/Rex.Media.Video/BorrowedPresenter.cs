using Rex.Media.Primitives;

namespace Rex.Media.Video;

/// <summary>
/// Lends a presenter to a session without giving it away: the session disposes what it is given
/// when it ends, but the window's presenter lives as long as the window, so this passes everything
/// through except <see cref="Dispose"/>. Pictures sent after the session let go are dropped, so a
/// session still closing cannot draw over the next one.
/// </summary>
public sealed class BorrowedPresenter(IVideoPresenter owner) : IVideoPresenter
{
    private readonly IVideoPresenter _owner = owner ?? throw new ArgumentNullException(nameof(owner));
    private volatile bool _returned;

    public string Name => _owner.Name;

    public object? Gpu => _owner.Gpu;

    public void Present(VideoFrame frame)
    {
        if (!_returned)
        {
            _owner.Present(frame);
        }
    }

    /// <summary>Gives the presenter back; the owner keeps it.</summary>
    public void Dispose() => _returned = true;
}
