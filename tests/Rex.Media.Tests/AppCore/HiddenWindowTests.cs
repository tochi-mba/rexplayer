using Rex.Media.AppCore.Player;

namespace Rex.Media.Tests.AppCore;

/// <summary>
/// A minimised window draws nothing (<see cref="HiddenWindow"/>): the presenter, the visualisation
/// and kinetic captions ask it before drawing, and the window brings its picture up to date as it
/// comes back.
/// </summary>
public sealed class HiddenWindowTests
{
    /// <summary>
    /// A minute of 30-frame-a-second video as the presenter sees it: how many pictures it draws,
    /// with the window minimised from <paramref name="minimisedFrom"/> seconds on.
    /// </summary>
    private static int PicturesDrawnInAMinute(double minimisedFrom)
    {
        var window = new HiddenWindow();
        var drawn = 0;
        for (var frame = 0; frame < 30 * 60; frame++)
        {
            if (frame == (int)(minimisedFrom * 30))
            {
                window.Minimised(true);
            }

            if (window.Draws)
            {
                drawn++;
            }
        }

        return drawn;
    }

    [Fact]
    public void A_window_in_sight_draws_every_picture()
    {
        var window = new HiddenWindow();

        Assert.True(window.Draws);
        Assert.False(window.Hidden);
        Assert.Equal(1800, PicturesDrawnInAMinute(minimisedFrom: 60));
    }

    [Fact]
    public void A_minimised_minute_draws_no_pictures()
    {
        // Before: all 1800 pictures of the minute were uploaded, drawn and presented to nobody.
        Assert.Equal(0, PicturesDrawnInAMinute(minimisedFrom: 0));
        Assert.Equal(900, PicturesDrawnInAMinute(minimisedFrom: 30));
    }

    [Fact]
    public void Minimising_and_restoring_say_when_the_window_moves_out_of_or_into_sight()
    {
        var window = new HiddenWindow();

        Assert.False(window.Minimised(false));
        Assert.True(window.Minimised(true));
        Assert.True(window.Hidden);
        Assert.False(window.Draws);
        Assert.False(window.Minimised(true));
        Assert.True(window.Minimised(false));
        Assert.True(window.Draws);
    }

    [Fact]
    public void A_hidden_window_draws_nothing_until_it_is_both_visible_and_restored()
    {
        var window = new HiddenWindow();

        Assert.False(window.Visible(true));
        Assert.True(window.Visible(false));
        Assert.False(window.Draws);

        // Minimised while hidden: still out of sight, so nothing changes.
        Assert.False(window.Minimised(true));
        Assert.False(window.Visible(true));
        Assert.False(window.Draws);
        Assert.True(window.Minimised(false));
        Assert.True(window.Draws);
    }

    [Theory]
    [InlineData(true, false, RestoreAction.Redraw)]
    [InlineData(false, false, RestoreAction.Redraw)]
    [InlineData(true, true, RestoreAction.Redraw)]
    [InlineData(false, true, RestoreAction.ShowPositionAgain)]
    public void Coming_back_shows_the_latest_picture(bool playing, bool missedPicture, RestoreAction expected)
    {
        // Paused after a picture went undrawn (a seek from the media keys while minimised), the
        // last picture drawn is out of date and nothing new is coming, so it is asked for again.
        Assert.Equal(expected, HiddenWindow.Restore(playing, missedPicture));
    }
}
