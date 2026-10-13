using System.Runtime.Versioning;
using Rex.Media.Primitives;
using Rex.Media.Video.D3D11;

namespace Rex.Media.Windows.Tests;

/// <summary>
/// The contour family drawn offscreen in software and read back: every member steady while only
/// grain changes from one picture to the next, each with a look of its own, and the mosaics'
/// tiles holding their colour through grain.
/// </summary>
[SupportedOSPlatform("windows8.0")]
public sealed class D3D11ContourTests
{
    private static readonly VideoEffect[] Family =
    [
        VideoEffect.NeonEdges, VideoEffect.InkTrace, VideoEffect.TopographicContours, VideoEffect.ChromaticContours,
        VideoEffect.Ghostwire, VideoEffect.GhostwireMask, VideoEffect.GhostwireMotion, VideoEffect.LumenWeave,
    ];

    private static VideoFrame Draw(VideoFrame picture, VideoEffect effect)
    {
        using var presenter = D3D11Presenter.Offscreen(picture.Width, picture.Height);
        var controls = VideoStyleOptions.Controls(effect, null, VideoStyleOptions.SharedStrengthDefault);
        presenter.SetEffect(effect, controls.Intensity, controls.Detail);
        presenter.SetEffectExtras(controls.Third, controls.Fourth);
        presenter.Present(picture);
        return presenter.ReadBack();
    }

    /// <summary>
    /// A small scene: a bright disc, a dim rectangle a few levels above its surround, a soft
    /// gradient and thin diagonal lines, with fresh grain of up to <paramref name="grain"/>
    /// levels from <paramref name="seed"/>.
    /// </summary>
    private static VideoFrame Scene(int seed, int grain, double seconds)
    {
        const int Width = 256, Height = 144;
        return EffectPictures.Bgra(Width, Height, (x, y) =>
        {
            var grey = 70 + (x / 8);
            var dx = x - 70.0;
            var dy = y - 72.0;
            if ((dx * dx) + (dy * dy) < 30 * 30)
            {
                grey = 200;
            }
            else if (x is >= 140 and < 220 && y is >= 30 and < 110)
            {
                grey += 10;
            }

            if (x > 150 && (x + y) % 23 == 0)
            {
                grey -= 40;
            }

            grey += EffectPictures.Noise(x + (seed * 997), y + (seed * 131), grain);
            return (grey, grey - 6, grey + 8);
        }, seconds);
    }

    private static double MeanDifference(VideoFrame a, VideoFrame b)
    {
        long sum = 0;
        for (var y = 0; y < a.Height; y++)
        {
            var rowA = a.Row(0, y);
            var rowB = b.Row(0, y);
            for (var x = 0; x < a.Width * 4; x++)
            {
                sum += x % 4 == 3 ? 0 : Math.Abs(rowA[x] - rowB[x]);
            }
        }

        return sum / (double)(a.Width * a.Height * 3);
    }

    [Fact]
    public void EveryContourEffectHoldsStillWhenOnlyTheGrainChanges()
    {
        using var first = Scene(seed: 1, grain: 3, seconds: 1);
        using var second = Scene(seed: 2, grain: 3, seconds: 1);
        var grainAlone = MeanDifference(first, second);
        foreach (var effect in Family)
        {
            using var a = Draw(first, effect);
            using var b = Draw(second, effect);
            var changed = MeanDifference(a, b);

            // Lines that shimmer with grain change far more than the grain itself does.
            Assert.True(changed < grainAlone * 2.5, $"{effect} changed by {changed:F2} levels where the grain changed by {grainAlone:F2}");
        }
    }

    [Fact]
    public void EveryContourEffectLooksUnlikeEveryOther()
    {
        using var scene = Scene(seed: 1, grain: 2, seconds: 1);
        var drawn = Family.Select(effect => (effect, picture: Draw(scene, effect))).ToList();
        try
        {
            foreach (var (effect, picture) in drawn)
            {
                foreach (var (other, otherPicture) in drawn.Where(pair => pair.effect > effect))
                {
                    var apart = MeanDifference(picture, otherPicture);
                    Assert.True(apart > 18, $"{effect} and {other} differ by only {apart:F1} levels on average");
                }
            }
        }
        finally
        {
            drawn.ForEach(pair => pair.picture.Dispose());
        }
    }
}
