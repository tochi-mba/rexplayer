using Rex.Media.Primitives;

namespace Rex.Media.Engine;

/// <summary>
/// When the first picture after an open or a seek goes on screen, so the window answers at once
/// instead of waiting for the sound to start or for the decoder to reach the exact moment asked
/// for. Only pictures into a real-time output are moved earlier: a capture keeps every picture
/// exactly where it belongs.
/// </summary>
public static class FirstPicture
{
    /// <summary>How long before it is due the first picture may be shown: about the time the audio device takes to start.</summary>
    public static readonly MediaTime Lead = MediaTime.FromMilliseconds(500);

    /// <summary>
    /// How far short of a precise seek's target a decoded picture must end for it to be shown on
    /// the way there; closer than this the decoder reaches the target before anyone would notice.
    /// </summary>
    public static readonly MediaTime PreviewDistance = MediaTime.FromMilliseconds(250);

    /// <summary>
    /// Whether a picture due at <paramref name="due"/> is shown without waiting for the clock: the
    /// first of its generation, when the clock is held (paused, so it would wait for a key press) or
    /// when it is due within <see cref="Lead"/>.
    /// </summary>
    public static bool ShowAtOnce(bool shownBefore, bool paused, MediaTime due, MediaTime now) =>
        !shownBefore && (paused || due - now <= Lead);

    /// <summary>
    /// Whether a picture that ends at <paramref name="end"/>, before the target of a precise seek,
    /// is shown while the decoder carries on to the target: the first of its generation (the
    /// keyframe the seek landed on), when the target is at least <see cref="PreviewDistance"/> away.
    /// </summary>
    public static bool ShowOnTheWay(bool shownBefore, MediaTime end, MediaTime target) =>
        !shownBefore && target - end >= PreviewDistance;
}
