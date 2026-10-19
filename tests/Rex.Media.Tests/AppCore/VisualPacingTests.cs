using Rex.Media.AppCore.Visuals;

namespace Rex.Media.Tests.AppCore;

/// <summary>
/// A paused visualisation settles and then stops drawing (AU-18): the window's rendering callback
/// asks <see cref="VisualPacing"/> on every display refresh, and is taken off once it says no.
/// </summary>
public sealed class VisualPacingTests
{
    private static readonly TimeSpan Refresh = TimeSpan.FromSeconds(1.0 / 60);

    /// <summary>
    /// The display's refreshes for <paramref name="seconds"/> from <paramref name="start"/>, as the
    /// window sees them: how many were drawn before the pacing said stop (after which the window is
    /// no longer called at all).
    /// </summary>
    private static int FramesDrawn(VisualPacing pacing, double seconds, bool playing, bool camera = false, TimeSpan start = default)
    {
        var frames = 0;
        for (var at = start; at < start + TimeSpan.FromSeconds(seconds); at += Refresh)
        {
            if (!pacing.Draws(at, playing, camera))
            {
                break;
            }

            frames++;
        }

        return frames;
    }

    [Fact]
    public void Playing_music_draws_every_refresh()
    {
        Assert.InRange(FramesDrawn(new VisualPacing(), 60, playing: true), 3599, 3601);
    }

    [Fact]
    public void A_paused_minute_draws_only_while_the_picture_settles()
    {
        // Before: a paused visualisation was asked to draw on all 3600 refreshes of a minute.
        var frames = FramesDrawn(new VisualPacing(), 60, playing: false);

        Assert.InRange(frames, 89, 91);
        Assert.True(frames * Refresh <= VisualPacing.Settle + Refresh);
    }

    [Fact]
    public void The_camera_keeps_a_paused_visualisation_drawing()
    {
        Assert.InRange(FramesDrawn(new VisualPacing(), 60, playing: false, camera: true), 3599, 3601);
    }

    [Fact]
    public void Playing_again_or_a_rebuild_draws_again_and_settles_again()
    {
        var pacing = new VisualPacing();
        Assert.True(pacing.Draws(TimeSpan.Zero, playing: false, usesCamera: false));
        Assert.False(pacing.Draws(TimeSpan.FromSeconds(2), playing: false, usesCamera: false));

        // Played again: drawn at once, and a later pause settles afresh.
        Assert.True(pacing.Draws(TimeSpan.FromSeconds(10), playing: true, usesCamera: false));
        Assert.True(pacing.Draws(TimeSpan.FromSeconds(11), playing: false, usesCamera: false));
        Assert.True(pacing.Draws(TimeSpan.FromSeconds(12), playing: false, usesCamera: false));
        Assert.False(pacing.Draws(TimeSpan.FromSeconds(12.5), playing: false, usesCamera: false));

        // Rebuilt while still (resized, another visualisation, a setting): drawn for another settle.
        pacing.Wake();
        Assert.InRange(FramesDrawn(pacing, 60, playing: false, start: TimeSpan.FromSeconds(20)), 89, 91);
    }

    [Fact]
    public void A_rendering_clock_that_goes_back_starts_settling_again()
    {
        var pacing = new VisualPacing();
        Assert.True(pacing.Draws(TimeSpan.FromSeconds(30), playing: false, usesCamera: false));
        Assert.True(pacing.Draws(TimeSpan.FromSeconds(1), playing: false, usesCamera: false));
        Assert.True(pacing.Draws(TimeSpan.FromSeconds(2), playing: false, usesCamera: false));
        Assert.False(pacing.Draws(TimeSpan.FromSeconds(3), playing: false, usesCamera: false));
    }
}
