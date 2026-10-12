using System.Runtime.Versioning;
using Rex.Media.Primitives;
using Rex.Media.Video.D3D11;

namespace Rex.Media.Windows.Tests;

/// <summary>Pictures made for the effect tests: exact, so each test knows what is in them.</summary>
internal static class EffectPictures
{
    /// <summary>A BGRA picture whose pixels <paramref name="paint"/> chooses as (red, green, blue).</summary>
    public static VideoFrame Bgra(int width, int height, Func<int, int, (int R, int G, int B)> paint, double seconds = 1)
    {
        var picture = VideoFrame.Rent(PixelFormat.Bgra32, width, height);
        picture.Pts = MediaTime.FromSeconds(seconds);
        for (var y = 0; y < height; y++)
        {
            var row = picture.Row(0, y);
            for (var x = 0; x < width; x++)
            {
                var (r, g, b) = paint(x, y);
                row[x * 4] = (byte)Math.Clamp(b, 0, 255);
                row[(x * 4) + 1] = (byte)Math.Clamp(g, 0, 255);
                row[(x * 4) + 2] = (byte)Math.Clamp(r, 0, 255);
                row[(x * 4) + 3] = 255;
            }
        }

        return picture;
    }

    /// <summary>A planar (NV12) picture with a luma ramp and a little colour, for the YUV shader.</summary>
    public static VideoFrame Nv12Gradient(int width, int height)
    {
        var picture = VideoFrame.Rent(PixelFormat.Nv12, width, height);
        picture.Color = new ColorInfo(ColorMatrix.Bt709, ColorTransfer.Bt709, ColorPrimaries.Bt709, false);
        for (var y = 0; y < height; y++)
        {
            var row = picture.Row(0, y);
            for (var x = 0; x < width; x++)
            {
                row[x] = (byte)(16 + ((x * 7 + y * 3) % 219));
            }
        }

        for (var y = 0; y < (height + 1) / 2; y++)
        {
            var row = picture.Row(1, y);
            for (var x = 0; x < (width + 1) / 2; x++)
            {
                row[x * 2] = (byte)(96 + (x * 3 % 64));
                row[(x * 2) + 1] = (byte)(160 - (y * 5 % 64));
            }
        }

        return picture;
    }

    /// <summary>Repeatable noise, as grain or compression noise would add.</summary>
    public static int Noise(int x, int y, int amplitude)
    {
        var h = unchecked((uint)(x * 73856093) ^ (uint)(y * 19349663));
        h ^= h >> 16;
        h = unchecked(h * 0x7feb352d);
        h ^= h >> 15;
        return (int)(h % (uint)((2 * amplitude) + 1)) - amplitude;
    }

    public static int MaxDifference(VideoFrame a, VideoFrame b)
    {
        var largest = 0;
        for (var y = 0; y < a.Height; y++)
        {
            var rowA = a.Row(0, y);
            var rowB = b.Row(0, y);
            for (var x = 0; x < a.Width * 4; x++)
            {
                largest = Math.Max(largest, Math.Abs(rowA[x] - rowB[x]));
            }
        }

        return largest;
    }

    /// <summary>The (blue, green, red) of one drawn pixel.</summary>
    public static (int B, int G, int R) Pixel(VideoFrame frame, int x, int y)
    {
        var row = frame.Row(0, y);
        return (row[x * 4], row[(x * 4) + 1], row[(x * 4) + 2]);
    }

    public static int Brightest(VideoFrame frame, int x, int y) => Math.Max(Math.Max(Pixel(frame, x, y).B, Pixel(frame, x, y).G), Pixel(frame, x, y).R);
}

/// <summary>
/// What the picture effects promise, drawn offscreen in software (WARP) and read back: contours
/// that find faint outlines but not noise, nothing drawn for black bars recorded into the picture,
/// no smeared borders, options that do what they say, and every effect at zero leaving the
/// picture alone.
/// </summary>
[SupportedOSPlatform("windows8.0")]
public sealed class D3D11EffectTests
{
    private static readonly VideoEffect[] Contours =
    [
        VideoEffect.NeonEdges, VideoEffect.InkTrace, VideoEffect.ChromaticContours,
        VideoEffect.Ghostwire, VideoEffect.GhostwireMask, VideoEffect.GhostwireMotion,
    ];

    private static D3D11Presenter Drawing(VideoFrame picture, VideoEffect effect, int? strength = null, int? detail = null, int? third = null, int? fourth = null)
    {
        var presenter = D3D11Presenter.Offscreen(picture.Width, picture.Height);
        var controls = VideoStyleOptions.Controls(effect, null, VideoStyleOptions.SharedStrengthDefault);
        presenter.SetEffect(effect, strength ?? controls.Intensity, detail ?? controls.Detail);
        presenter.SetEffectExtras(third ?? controls.Third, fourth ?? controls.Fourth);
        presenter.Present(picture);
        return presenter;
    }

    /// <summary>
    /// How much a contour effect drew at a pixel: the largest change from what the same effect
    /// draws on a perfectly flat picture of the same grey.
    /// </summary>
    private static int Drawn(VideoFrame result, VideoFrame flat, int x, int y)
    {
        var (b, g, r) = EffectPictures.Pixel(result, x, y);
        var (fb, fg, fr) = EffectPictures.Pixel(flat, x, y);
        return Math.Max(Math.Max(Math.Abs(b - fb), Math.Abs(g - fg)), Math.Abs(r - fr));
    }

    public static TheoryData<VideoEffect, int, int> FaintOutlines()
    {
        var data = new TheoryData<VideoEffect, int, int>();
        foreach (var effect in Contours)
        {
            data.Add(effect, 12, 100); // a 5 % step at the default sensitivity
            data.Add(effect, 6, 160);  // a 2.5 % step with the sensitivity raised
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(FaintOutlines))]
    public void ContoursFindAFaintOutlineInGrainButDrawNoSpecks(VideoEffect effect, int step, int sensitivity)
    {
        // Grey 80 with grain of up to 3 levels; a rectangle only a few levels brighter.
        const int Width = 192, Height = 128;
        using var flat = EffectPictures.Bgra(Width, Height, (_, _) => (80, 80, 80));
        using var picture = EffectPictures.Bgra(Width, Height, (x, y) =>
        {
            var grey = 80 + (x >= 64 && x < 128 && y >= 32 && y < 96 ? step : 0) + EffectPictures.Noise(x, y, 3);
            return (grey, grey, grey);
        });
        using var flatPresenter = Drawing(flat, effect, detail: sensitivity);
        using var flatDrawn = flatPresenter.ReadBack();
        using var presenter = Drawing(picture, effect, detail: sensitivity);
        using var drawn = presenter.ReadBack();

        // Along the rectangle's left side the outline shows on most rows (Neon contours keeps to
        // clear outlines by design, so only its freedom from specks is checked).
        var found = Enumerable.Range(40, 48).Count(y => Enumerable.Range(61, 6).Max(x => Drawn(drawn, flatDrawn, x, y)) > 40);
        Assert.True(effect == VideoEffect.NeonEdges || found > 36, $"{effect} drew the faint outline on only {found} of 48 rows");

        // Away from it, grain alone draws almost nothing.
        var specks = 0;
        for (var y = 8; y < Height - 8; y++)
        {
            for (var x = 8; x < 52; x++)
            {
                specks += Drawn(drawn, flatDrawn, x, y) > 40 ? 1 : 0;
            }
        }

        Assert.True(specks < 20, $"{effect} drew {specks} specks of grain");
    }

    [Theory]
    [InlineData(VideoEffect.NeonEdges)]
    [InlineData(VideoEffect.ChromaticContours)]
    [InlineData(VideoEffect.Ghostwire)]
    [InlineData(VideoEffect.GhostwireMask)]
    public void BlackBarsRecordedIntoThePictureDrawNoOutline(VideoEffect effect)
    {
        // Pillarboxed: black columns either side, a picture between with a real vertical edge.
        const int Width = 192, Height = 108;
        using var picture = EffectPictures.Bgra(Width, Height, (x, _) =>
            x < 24 || x >= 168 ? (0, 0, 0) : x < 96 ? (120, 110, 100) : (200, 190, 180));
        using var presenter = Drawing(picture, effect);
        using var drawn = presenter.ReadBack();

        // A drawn line is brighter than what lies either side of it.
        int Line(int from, int inside, int outside) =>
            Enumerable.Range(from, 8).Max(x => EffectPictures.Brightest(drawn, x, 54))
            - Math.Max(EffectPictures.Brightest(drawn, inside, 54), EffectPictures.Brightest(drawn, outside, 54));
        Assert.True(Line(92, 60, 130) > 40, $"{effect} lost the picture's own edge");
        Assert.True(Line(20, 10, 50) <= 12, $"{effect} drew the left bar's edge");
        Assert.True(Line(164, 180, 140) <= 12, $"{effect} drew the right bar's edge");
    }

    [Theory]
    [InlineData(VideoEffect.MotionSmear, 60, 40)]
    [InlineData(VideoEffect.ChromaticRift, 0, 100)]
    public void PreviousPictureEffectsNeverBorrowAPictureFromBeforeASeek(VideoEffect effect, int third, int fourth)
    {
        using var before = EffectPictures.Bgra(96, 64, (x, _) => x < 48 ? (250, 250, 250) : (10, 10, 10), seconds: 1);
        using var after = EffectPictures.Bgra(96, 64, (x, _) => x < 48 ? (10, 10, 10) : (250, 250, 250), seconds: 1.04);
        before.Generation = 1;
        after.Generation = 2;
        using var presenter = Drawing(before, effect, strength: 100, third: third, fourth: fourth);
        presenter.Present(after);
        using var seeked = presenter.ReadBack();
        using var fresh = Drawing(after, effect, strength: 100, third: third, fourth: fourth);
        using var expected = fresh.ReadBack();
        Assert.Equal(0, EffectPictures.MaxDifference(expected, seeked));

        // Within one playback the previous picture does count.
        after.Generation = 1;
        using var continuing = Drawing(before, effect, strength: 100, third: third, fourth: fourth);
        continuing.Present(after);
        using var moved = continuing.ReadBack();
        Assert.True(EffectPictures.MaxDifference(expected, moved) > 20, $"{effect} ignored the previous picture");
    }

    [Fact]
    public void NeonTracesClearOutlinesWhileChromaticContoursTraceEveryLine()
    {
        // A strong silhouette (left) and a faint fold (right) on clean grey.
        using var picture = EffectPictures.Bgra(192, 96, (x, _) => x < 48 ? (20, 20, 20) : x is >= 140 and < 143 ? (128, 128, 128) : (118, 118, 118));
        int Peak(VideoFrame frame, int from) => Enumerable.Range(from, 8).Max(x => EffectPictures.Brightest(frame, x, 48));

        using var neonPresenter = Drawing(picture, VideoEffect.NeonEdges);
        using var neon = neonPresenter.ReadBack();
        var neonFlat = EffectPictures.Brightest(neon, 100, 48);
        Assert.True(Peak(neon, 44) > neonFlat + 100, "Neon must light the silhouette");
        Assert.True(Peak(neon, 137) < neonFlat + 30, "Neon must leave faint folds dark");

        using var chromaticPresenter = Drawing(picture, VideoEffect.ChromaticContours);
        using var chromatic = chromaticPresenter.ReadBack();
        var chromaticFlat = EffectPictures.Brightest(chromatic, 100, 48);
        Assert.True(Peak(chromatic, 137) > chromaticFlat + 40, "Chromatic contours must draw the faint fold");
    }

    [Fact]
    public void AFlatClippedPictureStaysFlatUnderEveryEffect()
    {
        // Pure white has no slope in any direction; nothing may divide by it.
        using var white = EffectPictures.Bgra(96, 64, (_, _) => (255, 255, 255));
        foreach (var effect in Enum.GetValues<VideoEffect>().Where(effect => !VideoEffects.UsesPointer(effect)
            && effect is not (VideoEffect.Halftone or VideoEffect.GlyphMosaic or VideoEffect.TapeWear or VideoEffect.InkTrace or VideoEffect.TopographicContours or VideoEffect.Kaleidoscope or VideoEffect.Vortex)))
        {
            using var presenter = Drawing(white, effect, strength: 100);
            using var drawn = presenter.ReadBack();
            var first = EffectPictures.Pixel(drawn, 10, 10);
            for (var y = 4; y < 60; y += 5)
            {
                for (var x = 4; x < 92; x += 5)
                {
                    Assert.Equal(first, EffectPictures.Pixel(drawn, x, y));
                }
            }
        }
    }

    [Fact]
    public void EveryEffectAtZeroStrengthShowsTheOriginal()
    {
        using var picture = EffectPictures.Bgra(96, 64, (x, y) => (x * 2, y * 3, (x + y) % 255));
        using var plain = Drawing(picture, VideoEffect.Off);
        using var original = plain.ReadBack();
        foreach (var effect in Enum.GetValues<VideoEffect>())
        {
            using var presenter = Drawing(picture, effect, strength: 0);
            presenter.SetPointer(0.5f, 0.5f, true);
            presenter.Redraw();
            using var drawn = presenter.ReadBack();
            Assert.True(EffectPictures.MaxDifference(original, drawn) == 0, $"{effect} changed the picture at zero strength");
        }
    }

    [Fact]
    public void WarpsMirrorThePictureInsteadOfSmearingItsBorder()
    {
        // A red last column: a clamped warp drags it across the corners as streaks.
        using var picture = EffectPictures.Bgra(160, 90, (x, y) => x == 159 ? (255, 0, 0) : (60 + (y / 3), 90, 110 + (x / 4)));
        foreach (var effect in new[] { VideoEffect.Kaleidoscope, VideoEffect.Vortex, VideoEffect.LiquidGlass, VideoEffect.PrismFlow })
        {
            using var presenter = Drawing(picture, effect, strength: 100);
            using var drawn = presenter.ReadBack();
            var red = 0;
            for (var y = 0; y < 90; y++)
            {
                for (var x = 0; x < 160; x++)
                {
                    var (b, g, r) = EffectPictures.Pixel(drawn, x, y);
                    red += r > 200 && g < 80 && b < 80 ? 1 : 0;
                }
            }

            Assert.True(red < 300, $"{effect} smeared the border over {red} pixels");
        }
    }

    [Fact]
    public void KaleidoscopeFoldsAndZoomChangeThePicture()
    {
        using var picture = EffectPictures.Bgra(160, 90, (x, y) => (x * 3 % 256, y * 5 % 256, (x * y) % 256));
        using var three = Drawing(picture, VideoEffect.Kaleidoscope, strength: 100, third: 3, fourth: 0);
        using var threeFolds = three.ReadBack();
        three.SetEffectExtras(8, 0);
        three.Redraw();
        using var eightFolds = three.ReadBack();
        Assert.True(EffectPictures.MaxDifference(threeFolds, eightFolds) > 20, "the fold count did nothing");

        three.SetEffect(VideoEffect.Kaleidoscope, 100, 160);
        three.Redraw();
        using var zoomed = three.ReadBack();
        Assert.True(EffectPictures.MaxDifference(eightFolds, zoomed) > 20, "the mirror zoom did nothing");
    }

    [Fact]
    public void SwitchingEffectsStartsTheNewOneFromItsOwnDefaults()
    {
        // Pixel sort's extras must not carry over into Halftone when only the effect changes.
        using var picture = EffectPictures.Bgra(96, 64, (x, y) => (x * 2, y * 3, 128));
        using var presenter = Drawing(picture, VideoEffect.PixelSort, strength: 90, third: 100, fourth: 3);
        presenter.SetEffect(VideoEffect.Halftone, 90);
        presenter.Redraw();
        using var switched = presenter.ReadBack();
        using var fresh = Drawing(picture, VideoEffect.Halftone, strength: 90, detail: 100);
        using var expected = fresh.ReadBack();
        Assert.Equal(0, EffectPictures.MaxDifference(expected, switched));
    }

    [Fact]
    public void HalftoneLeavesWhitePaperCleanAndClosesBlackUp()
    {
        using var picture = EffectPictures.Bgra(120, 60, (x, _) => x < 60 ? (255, 255, 255) : (0, 0, 0));
        using var presenter = Drawing(picture, VideoEffect.Halftone, strength: 100);
        using var drawn = presenter.ReadBack();
        for (var y = 4; y < 56; y++)
        {
            for (var x = 4; x < 52; x++)
            {
                Assert.True(EffectPictures.Pixel(drawn, x, y) == (255, 255, 255), $"ink on white paper at {x},{y}");
            }
        }

        var dark = Enumerable.Range(70, 40).Average(x => EffectPictures.Brightest(drawn, x, 30));
        Assert.True(dark < 40, $"full coverage left paper showing ({dark:F0})");
    }

    [Fact]
    public void GlyphsAreDensestWhereTheScreenIsBrightAndWherePaperIsDark()
    {
        using var picture = EffectPictures.Bgra(240, 120, (x, _) => x < 120 ? (230, 230, 230) : (20, 20, 20));
        int Lit(VideoFrame frame, int from) => Enumerable.Range(10, 100).Sum(y => Enumerable.Range(from, 100).Count(x => EffectPictures.Brightest(frame, x, y) > 120));

        using var phosphor = Drawing(picture, VideoEffect.GlyphMosaic, strength: 100, fourth: 0);
        using var screen = phosphor.ReadBack();
        Assert.True(Lit(screen, 10) > Lit(screen, 130) + 500, "bright parts of the picture must light more of the phosphor screen");

        using var paperPresenter = Drawing(picture, VideoEffect.GlyphMosaic, strength: 100, fourth: 2);
        using var paper = paperPresenter.ReadBack();
        int Inked(VideoFrame frame, int from) => Enumerable.Range(10, 100).Sum(y => Enumerable.Range(from, 100).Count(x => EffectPictures.Brightest(frame, x, y) < 120));
        Assert.True(Inked(paper, 130) > Inked(paper, 10) + 500, "dark parts of the picture must carry more ink on paper");
    }

    [Fact]
    public void ThermalRainbowRunsFromColdToHotWithoutWrappingRound()
    {
        using var picture = EffectPictures.Bgra(64, 32, (x, _) => x < 32 ? (0, 0, 0) : (255, 255, 255));
        using var presenter = Drawing(picture, VideoEffect.ThermalMap, strength: 100, detail: 25, fourth: 1);
        using var drawn = presenter.ReadBack();
        var cold = EffectPictures.Pixel(drawn, 8, 16);
        var hot = EffectPictures.Pixel(drawn, 56, 16);
        Assert.True(cold.B > cold.R + 60, $"black must be cold blue: {cold}");
        Assert.True(hot.R > hot.B + 60, $"white must be hot red: {hot}");
    }

    [Theory]
    [InlineData(0, 0, 1)]
    [InlineData(1, 0, -1)]
    [InlineData(2, 1, 0)]
    [InlineData(3, -1, 0)]
    public void PixelSortStreaksRunTheNamedWay(int mode, int dx, int dy)
    {
        // One bright square in the middle of a dark picture.
        using var picture = EffectPictures.Bgra(128, 128, (x, y) => x is >= 60 and < 68 && y is >= 60 and < 68 ? (250, 250, 250) : (20, 20, 20));
        using var presenter = Drawing(picture, VideoEffect.PixelSort, strength: 100, detail: 100, third: 100, fourth: mode);
        using var drawn = presenter.ReadBack();
        var ahead = EffectPictures.Brightest(drawn, 64 + (dx * 12), 64 + (dy * 12));
        var behind = EffectPictures.Brightest(drawn, 64 - (dx * 12), 64 - (dy * 12));
        Assert.True(ahead > behind + 80, $"{VideoStyleOptions.ModeNames(VideoEffect.PixelSort)[mode]}: ahead {ahead}, behind {behind}");
    }
}
