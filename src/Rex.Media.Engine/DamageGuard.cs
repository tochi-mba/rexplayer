using Rex.Media.Primitives;

namespace Rex.Media.Engine;

/// <summary>
/// Where playback meets damaged data: a failure of a reader or decoder on the bytes it was given is
/// damage, which playback works around (it passes over the packet, the stretch or the track), while
/// the session closing and the disk failing are not, and still end what they end.
/// </summary>
internal static class DamageGuard
{
    /// <summary>
    /// Whether a failure is damage in the media rather than the session closing or the disk failing.
    /// A reader's own fault on hostile bytes counts as damage. (A <see cref="MediaFormatException"/>
    /// is an <see cref="IOException"/>, but of the bytes, not the disk.)
    /// </summary>
    public static bool Recoverable(Exception ex) => ex is MediaFormatException || ex is not (OperationCanceledException or IOException or UnauthorizedAccessException);

    /// <summary>Runs <paramref name="work"/>; damage is handed to <paramref name="damaged"/> and false returned instead of thrown.</summary>
    public static bool Try(Action work, Action<Exception> damaged)
    {
        try
        {
            work();
            return true;
        }
        catch (Exception ex) when (Recoverable(ex))
        {
            damaged(ex);
            return false;
        }
    }
}
