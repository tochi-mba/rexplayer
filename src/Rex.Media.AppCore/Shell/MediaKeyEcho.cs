using Rex.Media.AppCore.Player;

namespace Rex.Media.AppCore.Shell;

/// <summary>
/// One press of a media key while rexplayer's window is in front can arrive twice: once as the key,
/// which the window handles at once, and again a moment later from Windows' media controls, which
/// hear the same key. Each media key the window handled is remembered for <see cref="Window"/>, and
/// the first matching press from Windows within it is the echo, taken once and dropped. Pressing
/// twice runs twice: each echo matches one press. Windows may send the play key as Play or as Pause,
/// whichever it thinks is due, so the two count as one.
/// </summary>
public sealed class MediaKeyEcho
{
    /// <summary>How long after a media key Windows' copy of it may still arrive.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromMilliseconds(500);

    private readonly (TransportRequest? Request, TimeSpan At)[] _pressed = new (TransportRequest?, TimeSpan)[8];
    private int _next;

    /// <summary>The media key for <paramref name="virtualKey"/>, or null for any other key; by its virtual-key code.</summary>
    public static TransportRequest? RequestOfKey(int virtualKey) => virtualKey switch
    {
        0xB0 => TransportRequest.Next,
        0xB1 => TransportRequest.Previous,
        0xB2 => TransportRequest.Stop,
        0xB3 => TransportRequest.PlayPause,
        _ => null,
    };

    /// <summary>The window handled a media key asking for <paramref name="request"/> at <paramref name="now"/>.</summary>
    public void Handled(TransportRequest request, TimeSpan now)
    {
        _pressed[_next] = (Family(request), now);
        _next = (_next + 1) % _pressed.Length;
    }

    /// <summary>Whether <paramref name="request"/> from Windows at <paramref name="now"/> is a key the window already handled.</summary>
    public bool IsEcho(TransportRequest request, TimeSpan now)
    {
        var family = Family(request);
        for (var i = 0; i < _pressed.Length; i++)
        {
            var (pressed, at) = _pressed[i];
            if (pressed == family && now >= at && now - at <= Window)
            {
                _pressed[i] = default;
                return true;
            }
        }

        return false;
    }

    private static TransportRequest Family(TransportRequest request) =>
        request is TransportRequest.Play or TransportRequest.Pause ? TransportRequest.PlayPause : request;
}
