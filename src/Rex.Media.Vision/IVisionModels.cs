namespace Rex.Media.Vision;

/// <summary>
/// Click-to-select with a model: a point or a box on a picture to the outline of the object there.
/// Implementations cache the expensive image encoding per <see cref="PictureKey"/>, so further clicks
/// on the same picture cost only the prompt decoder. Calls come from one thread at a time.
/// </summary>
public interface IPromptSegmenter : IDisposable
{
    /// <summary>Where the model actually runs (a failed graphics load falls back to the processor).</summary>
    ExecutionDevice Device { get; }

    /// <summary>The last call's time in the image encoder; zero when the cached encoding was reused.</summary>
    TimeSpan LastEncode { get; }

    /// <summary>The last call's time in the prompt decoder.</summary>
    TimeSpan LastDecode { get; }

    /// <summary>The mask of the object under <paramref name="prompt"/>, at the picture's size; empty when nothing was found.</summary>
    PictureMask Segment(BgraPicture picture, PictureKey key, SegmentPrompt prompt);
}

/// <summary>Removal with a model: fills the masked pixels of a picture from what surrounds them.</summary>
public interface IInpainter : IDisposable
{
    ExecutionDevice Device { get; }

    /// <summary>The last fill's time.</summary>
    TimeSpan LastFill { get; }

    /// <summary>
    /// Writes the picture into <paramref name="output"/> (BGRA, the picture's size and stride) with
    /// every pixel inside <paramref name="hole"/> replaced; pixels outside it are copied unchanged.
    /// </summary>
    void Fill(BgraPicture picture, PictureMask hole, Span<byte> output);
}

/// <summary>
/// Following a selected object from picture to picture by its outline, remembering the last good
/// mask so a lost object can be confirmed when the hand-written search proposes where it returned.
/// </summary>
public interface IMaskTracker
{
    /// <summary>Begins following the object <paramref name="mask"/> outlines.</summary>
    void Start(PictureMask mask);

    /// <summary>The object in the next picture, searched near where it last was.</summary>
    TrackResult Track(BgraPicture picture, PictureKey key);

    /// <summary>Whether the object is inside <paramref name="candidate"/>, a box proposed after it was lost.</summary>
    TrackResult Reacquire(BgraPicture picture, PictureKey key, PixelBox candidate);

    /// <summary>Forgets the object.</summary>
    void Reset();
}
