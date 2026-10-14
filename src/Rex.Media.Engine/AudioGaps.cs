using Rex.Media.Primitives;

namespace Rex.Media.Engine;

/// <summary>What the audio thread does with a frame that does not start where the last one ended.</summary>
internal enum GapAction
{
    /// <summary>It follows on (within a rounding error, or overlapping the last): played as it comes.</summary>
    Follow,

    /// <summary>A short stretch is missing: silence fills it, so the pictures stay in step with the sound.</summary>
    Conceal,

    /// <summary>A long stretch is missing: the clock moves over it when the frame is heard.</summary>
    Jump,

    /// <summary>The frame claims a time past the end of the media: its timestamp is damage, and it follows on.</summary>
    Distrust,
}

/// <summary>
/// The policy for holes in the sound (the stretches a damaged file loses, or a demuxer skips while
/// it finds its place again). The clock counts the samples written, so a hole left as it is would
/// leave the clock behind the pictures by its length: a short hole is filled with silence, a long
/// one is jumped over, and a time no media can reach is not believed.
/// </summary>
internal static class AudioGaps
{
    /// <summary>How far a frame may start from where the last ended and still be taken to follow it.</summary>
    public static readonly MediaTime Tolerance = MediaTime.FromMilliseconds(2);

    /// <summary>The longest hole filled with silence; longer holes are jumped over.</summary>
    public static readonly MediaTime LongestSilence = MediaTime.FromSeconds(1);

    /// <summary>What to do with a frame starting at <paramref name="actual"/> when the last ended at <paramref name="expected"/>, in media of <paramref name="duration"/>.</summary>
    public static GapAction Classify(MediaTime expected, MediaTime actual, MediaTime duration)
    {
        if (!expected.IsKnown || !actual.IsKnown || actual - expected <= Tolerance)
        {
            return GapAction.Follow;
        }

        if (duration.IsKnown && actual > duration + LongestSilence)
        {
            return GapAction.Distrust;
        }

        return actual - expected <= LongestSilence ? GapAction.Conceal : GapAction.Jump;
    }
}
