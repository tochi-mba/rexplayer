namespace Rex.Media.Vision;

/// <summary>
/// An <see cref="IMaskTracker"/> built on a promptable segmenter: each picture is prompted with a box
/// around where the object last was and the size it had, and the answer is accepted only when the
/// model is confident and the outline has not changed size implausibly. The memory is the last
/// accepted mask. This is the on-demand tracker; a video model with a longer memory (ADR-0019) can
/// replace it behind the same interface once one fits a frame budget.
/// </summary>
public sealed class PromptedMaskTracker : IMaskTracker
{
    /// <summary>How far around the last box the next picture is searched, as a share of its size.</summary>
    public const double SearchMargin = 0.25;

    /// <summary>The model's own quality estimate below which an outline is not trusted.</summary>
    public const double MinimumScore = 0.6;

    /// <summary>The largest change of area between consecutive pictures.</summary>
    public const double FollowGrowth = 1.6;

    /// <summary>The largest change of area between the last good outline and a returning object.</summary>
    public const double ReturnGrowth = 3.0;

    private readonly IPromptSegmenter _segmenter;

    public PromptedMaskTracker(IPromptSegmenter segmenter)
    {
        _segmenter = segmenter ?? throw new ArgumentNullException(nameof(segmenter));
    }

    /// <summary>The last accepted mask, or null when nothing is followed.</summary>
    public PictureMask? Memory { get; private set; }

    public void Start(PictureMask mask)
    {
        ArgumentNullException.ThrowIfNull(mask);
        if (mask.Area == 0)
        {
            throw new ArgumentException("An empty mask has nothing to follow.", nameof(mask));
        }

        Memory = mask;
    }

    public void Reset() => Memory = null;

    public TrackResult Track(BgraPicture picture, PictureKey key) =>
        Follow(picture, key, Memory?.Bounds.Inflate(SearchMargin, picture.Width, picture.Height), FollowGrowth);

    public TrackResult Reacquire(BgraPicture picture, PictureKey key, PixelBox candidate) =>
        Follow(picture, key, candidate.Inflate(SearchMargin / 2, picture.Width, picture.Height), ReturnGrowth);

    private TrackResult Follow(BgraPicture picture, PictureKey key, PixelBox? box, double growth)
    {
        if (Memory is not { } memory)
        {
            return TrackResult.Lost("Nothing is being followed.");
        }

        if (memory.Width != picture.Width || memory.Height != picture.Height)
        {
            return TrackResult.Lost("The picture size changed.");
        }

        if (box is not { IsEmpty: false } search)
        {
            return TrackResult.Lost("The search area is outside the picture.");
        }

        var found = _segmenter.Segment(picture, key, SegmentPrompt.Following(search, memory.Area));
        if (found.Area == 0)
        {
            return TrackResult.Lost("The model found no object there.");
        }

        if (found.Score < MinimumScore)
        {
            return TrackResult.Lost("The model was not confident about the outline.");
        }

        var ratio = (double)found.Area / memory.Area;
        if (ratio > growth || ratio < 1 / growth)
        {
            return TrackResult.Lost("The outline changed size too suddenly to be the same object.");
        }

        Memory = found;
        return TrackResult.Found(found);
    }
}
