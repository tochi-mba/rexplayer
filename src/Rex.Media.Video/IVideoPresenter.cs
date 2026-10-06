using Rex.Media.Primitives;

namespace Rex.Media.Video;

/// <summary>
/// Where pictures go to be seen: a window's swap chain, or a recorder in tests (ADR-008). The engine
/// decides when each picture is due and calls <see cref="Present"/> at that moment; the presenter
/// shows it at once. The engine keeps the picture, so the presenter copies or uploads what it needs
/// before returning.
/// </summary>
public interface IVideoPresenter : IDisposable
{
    string Name { get; }

    /// <summary>
    /// The graphics device the presenter draws with, for decoders that can leave their pictures on it
    /// (opaque to the engine), or null.
    /// </summary>
    object? Gpu => null;

    void Present(VideoFrame frame);
}
