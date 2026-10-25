namespace Rex.Media.Vision;

/// <summary>What became of a model's outline offered for a click.</summary>
public enum MaskVerdict
{
    /// <summary>Confident, and the size of an object: it becomes the selection and the removal's shape.</summary>
    Accepted,

    /// <summary>The model found nothing at the click.</summary>
    Empty,

    /// <summary>The model's own quality estimate was below <see cref="SubjectModelPolicy.MinimumScore"/>.</summary>
    NotConfident,

    /// <summary>Too few pixels to follow or to be worth removing: a fragment, not an object.</summary>
    TooSmall,

    /// <summary>Most of the picture: the background or the whole scene, not an object in it.</summary>
    TooLarge,
}

/// <summary>
/// When Subject Lock asks the optional models (ADR-0019) and when it keeps its own methods. The
/// models are slow on most computers, seconds for one picture on integrated graphics, so they are
/// used only for a picture that stays on screen (a paused video) and only while their measured time
/// (<see cref="FrameBudget"/>) is a wait a person will sit through with a progress message. A
/// following second opinion runs while the video plays, so it is held to the interactive budget.
/// Everything here is a decision; the session that follows it runs the models off the presenting
/// thread and falls back to its own methods whenever the answer is no.
/// </summary>
public static class SubjectModelPolicy
{
    /// <summary>The model's own quality estimate below which an outline is not used.</summary>
    public const double MinimumScore = PromptedMaskTracker.MinimumScore;

    /// <summary>The fewest pixels an accepted outline has, whatever the picture's size.</summary>
    public const int SmallestArea = 400;

    /// <summary>The smallest share of the picture an accepted outline covers.</summary>
    public const double SmallestShare = 0.0002;

    /// <summary>The narrowest an accepted outline's bounds are, in pixels, either way.</summary>
    public const int NarrowestSide = 12;

    /// <summary>The largest share of the picture an accepted outline covers.</summary>
    public const double LargestShare = 0.5;

    /// <summary>The longest wait for a model on a paused picture, with its progress shown and any action cancelling it.</summary>
    public static readonly TimeSpan PausedWait = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Whether a model may work on a picture: it is installed and opened, the picture stays on
    /// screen while it works, and its measured time on this computer is a wait worth making.
    /// An unmeasured model is tried once; its first call shows what it costs here.
    /// </summary>
    public static bool UseOnPicture(bool available, bool paused, FrameBudget budget)
    {
        ArgumentNullException.ThrowIfNull(budget);
        return available && paused && (budget.Slow is not { } slow || slow <= PausedWait.TotalMilliseconds);
    }

    /// <summary>
    /// Whether the model may confirm a return the hand-written search proposed while the video
    /// plays: only when its slow calls answer within <see cref="FrameBudget.Interactive"/>, so the
    /// confirmation arrives while the proposal still describes the picture on screen.
    /// </summary>
    public static bool SecondOpinion(bool available, FrameBudget budget, double framesPerSecond)
    {
        ArgumentNullException.ThrowIfNull(budget);
        return available && budget.Slow is not null && budget.Decide(framesPerSecond) != ModelUse.Unsuitable;
    }

    /// <summary>Whether an outline the model drew for a click is an object worth selecting, and why not.</summary>
    public static MaskVerdict Judge(PictureMask mask)
    {
        ArgumentNullException.ThrowIfNull(mask);
        var picture = (double)mask.Width * mask.Height;
        if (mask.Area == 0)
        {
            return MaskVerdict.Empty;
        }

        if (mask.Score < MinimumScore)
        {
            return MaskVerdict.NotConfident;
        }

        if (mask.Area < Math.Max(SmallestArea, SmallestShare * picture)
            || mask.Bounds.Width < NarrowestSide || mask.Bounds.Height < NarrowestSide)
        {
            return MaskVerdict.TooSmall;
        }

        return mask.Area > LargestShare * picture ? MaskVerdict.TooLarge : MaskVerdict.Accepted;
    }
}
