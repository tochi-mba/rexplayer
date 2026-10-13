using Rex.Media.Primitives;
using Rex.Media.Video.D3D11;

namespace Rex.Media.Windows.Tests;

/// <summary>Effects that restyle the whole picture and add to it where it moves.</summary>
public sealed partial class D3D11PresenterTests
{
    [Theory]
    [InlineData(VideoEffect.FlowField)]
    [InlineData(VideoEffect.MotionIsolate)]
    public void MotionStylesHoldSteadyOnAStillPictureAndHighlightMovement(VideoEffect effect)
    {
        // These restyle the whole picture, so a still one changes too, but it must look the same
        // with or without a previous picture (no flicker after a seek), and movement must add to it.
        using var presenter = D3D11Presenter.Offscreen(144, 96);
        presenter.SetEffect(effect, 100);
        var (third, fourth) = VideoStyleOptions.Extras(null, VideoStyleOptions.ForEffect(effect));
        presenter.SetEffectExtras(third, fourth);
        using var initial = MotionPicture(24, 0);
        presenter.Present(initial);
        using var first = presenter.ReadBack();
        Assert.True(MaxDifference(first, initial) > 10, $"{effect} did not restyle the picture");

        using var unchanged = MotionPicture(24, 0.04);
        presenter.Present(unchanged);
        using var still = presenter.ReadBack();
        Assert.Equal(0, MaxDifference(first, still));

        using var shifted = MotionPicture(39, 0.08);
        presenter.Present(shifted);
        using var moving = presenter.ReadBack();
        using var fresh = D3D11Presenter.Offscreen(144, 96);
        fresh.SetEffect(effect, 100);
        fresh.SetEffectExtras(third, fourth);
        fresh.Present(shifted);
        using var withoutMotion = fresh.ReadBack();
        Assert.True(MaxDifference(moving, withoutMotion) > 10, $"{effect} did not highlight the moving bar");

        presenter.SetEffect(effect, 0);
        presenter.Redraw();
        using var disabled = presenter.ReadBack();
        Assert.Equal(0, MaxDifference(disabled, shifted));
    }
}
