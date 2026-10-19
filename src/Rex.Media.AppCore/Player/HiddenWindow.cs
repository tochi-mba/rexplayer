namespace Rex.Media.AppCore.Player;

/// <summary>What the window does when it is restored, to bring its picture up to date.</summary>
public enum RestoreAction
{
    /// <summary>Draw the last picture again at once; while playing, the next picture follows within a frame.</summary>
    Redraw,

    /// <summary>
    /// A picture came while minimised and nothing new is on its way (paused): ask for the picture
    /// at the current position again, so the window does not show an older one.
    /// </summary>
    ShowPositionAgain,
}

/// <summary>
/// Whether the window is out of sight (minimised, or hidden) and so draws nothing. Nobody can see a
/// minimised window, yet the video went on being uploaded, drawn and presented thirty to sixty
/// times a second, and the visualisation and kinetic captions went on drawing with it. While the
/// window is out of sight the pictures are not drawn; decoding, sound and the timeline carry on,
/// so playback continues and the sound stays in step, and the latest picture is drawn the moment
/// the window comes back.
/// </summary>
public sealed class HiddenWindow
{
    private bool _minimised;
    private bool _visible = true;

    /// <summary>Whether the window is out of sight now.</summary>
    public bool Hidden => _minimised || !_visible;

    /// <summary>Whether pictures, the visualisation and kinetic captions are drawn.</summary>
    public bool Draws => !Hidden;

    /// <summary>
    /// The window's presenter state changed: <paramref name="minimised"/> when it is now minimised.
    /// True when that moved the window into or out of sight.
    /// </summary>
    public bool Minimised(bool minimised)
    {
        var was = Hidden;
        _minimised = minimised;
        return was != Hidden;
    }

    /// <summary>
    /// The window was shown or hidden: <paramref name="visible"/> when it is now visible. True when
    /// that moved the window into or out of sight.
    /// </summary>
    public bool Visible(bool visible)
    {
        var was = Hidden;
        _visible = visible;
        return was != Hidden;
    }

    /// <summary>
    /// How to bring the picture up to date as the window comes back: when a picture came while it
    /// was out of sight (<paramref name="missedPicture"/>) and the player is paused, that picture
    /// is asked for again; otherwise the last picture is drawn again at once.
    /// </summary>
    public static RestoreAction Restore(bool playing, bool missedPicture) =>
        missedPicture && !playing ? RestoreAction.ShowPositionAgain : RestoreAction.Redraw;
}
