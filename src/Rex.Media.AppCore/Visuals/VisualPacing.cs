namespace Rex.Media.AppCore.Visuals;

/// <summary>
/// Whether a visualisation keeps drawing (AU-18). While sound plays, or while the camera feeds the
/// picture, every frame changes and is drawn. Once the music pauses or ends there is nothing new to
/// show: the picture is let die away for <see cref="Settle"/>, so the bars fall and the light fades,
/// and then drawing stops until the music plays again or the visualisation is rebuilt, rather than
/// repainting a still picture thirty to sixty times a second.
/// </summary>
public sealed class VisualPacing
{
    /// <summary>How long a paused visualisation keeps drawing, for what was last heard to die away.</summary>
    public static readonly TimeSpan Settle = TimeSpan.FromSeconds(1.5);

    private TimeSpan? _quietSince;

    /// <summary>The picture must be drawn again (it was rebuilt, resized or its settings changed): a fresh <see cref="Settle"/> begins.</summary>
    public void Wake() => _quietSince = null;

    /// <summary>
    /// Whether to draw the frame at <paramref name="now"/> (the window's rendering time): always
    /// while <paramref name="playing"/> or <paramref name="usesCamera"/>; otherwise only until
    /// <see cref="Settle"/> has passed since the sound stopped or the last <see cref="Wake"/>.
    /// </summary>
    public bool Draws(TimeSpan now, bool playing, bool usesCamera)
    {
        if (playing || usesCamera)
        {
            _quietSince = null;
            return true;
        }

        // A clock that went back (a new rendering time base) starts the settling again.
        if (_quietSince is not { } since || now < since)
        {
            _quietSince = now;
            return true;
        }

        return now - since < Settle;
    }
}
