using System.Runtime.Versioning;
using Rex.Media.AppCore;
using Rex.Media.Codecs.MediaFoundation;
using Rex.Media.IO;
using Rex.Media.Primitives;
using Rex.Media.TestKit;
using Rex.Media.Video;
using Rex.Media.Video.D3D11;

namespace Rex.Media.Windows.Tests;

/// <summary>
/// The Direct3D presenter drawing offscreen in software (WARP, as CI has no graphics card), read
/// back and compared with the CPU's conversion of the same picture.
/// </summary>
[SupportedOSPlatform("windows8.0")]
public sealed class D3D11PresenterTests
{
    /// <summary>The picture of h264-aac.mp4 at 0.24 s, decoded by Windows (NV12).</summary>
    private static VideoFrame DecodedPicture()
    {
        var factory = new MfDecoderFactory();
        using var source = new MemoryByteSource(File.ReadAllBytes(RepoPaths.Combine("tests/fixtures/mp4/h264-aac.mp4")), "h264-aac.mp4");
        using var demuxer = MediaRegistries.Demuxers().Probe(source, CancellationToken.None)!.Open(source, CancellationToken.None);
        if (!factory.CanDecode(demuxer.Info.FirstTrack(MediaKind.Video)!))
        {
            Assert.Skip("This Windows has no Media Foundation H.264 decoder.");
        }

        return Snapshot.Take(demuxer, MediaRegistries.Decoders(factory), MediaTime.FromSeconds(0.24), CancellationToken.None).Picture;
    }

    private static double Psnr(VideoFrame a, VideoFrame b)
    {
        double sum = 0;
        for (var y = 0; y < a.Height; y++)
        {
            var rowA = a.Row(0, y);
            var rowB = b.Row(0, y);
            for (var x = 0; x < a.Width * 4; x++)
            {
                if (x % 4 != 3)
                {
                    var difference = rowA[x] - rowB[x];
                    sum += difference * difference;
                }
            }
        }

        var mse = sum / (a.Width * a.Height * 3);
        return mse == 0 ? double.PositiveInfinity : 10 * Math.Log10(255 * 255 / mse);
    }

    /// <summary>The largest difference in any colour of any pixel.</summary>
    private static int MaxDifference(VideoFrame a, VideoFrame b)
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

    [Fact]
    [Capability("VID-23")]
    public void TheGraphicsCardDrawsWhatTheCpuConversionGives()
    {
        // With chroma from the nearest sample at 1:1 the GPU does exactly the CPU's arithmetic.
        using var picture = DecodedPicture();
        using var expected = ColorConverter.ToBgra(picture);
        using var presenter = D3D11Presenter.Offscreen(picture.Width, picture.Height);
        presenter.SmoothChroma = false;

        presenter.Present(picture);
        using var drawn = presenter.ReadBack();
        presenter.SmoothChroma = true;
        presenter.Present(picture);
        using var smoothed = presenter.ReadBack();

        Assert.Equal(PixelFormat.Nv12, picture.Format);
        Assert.Equal("Direct3D 11 (software)", presenter.Name);
        Assert.Equal((expected.Width, expected.Height), (drawn.Width, drawn.Height));
        Assert.InRange(MaxDifference(drawn, expected), 0, 1);
        Assert.True(Psnr(smoothed, expected) > 20, $"PSNR {Psnr(smoothed, expected):F1} dB");
    }

    [Fact]
    public void EachPictureLookTransformsPixelsAndOriginalRestoresThem()
    {
        using var picture = DecodedPicture();
        using var presenter = D3D11Presenter.Offscreen(picture.Width, picture.Height);
        presenter.SmoothChroma = false;
        presenter.Present(picture);
        using var original = presenter.ReadBack();

        foreach (var look in Enum.GetValues<VideoLook>().Where(look => look != VideoLook.Original))
        {
            presenter.SetLook(look);
            presenter.Redraw();
            using var styled = presenter.ReadBack();
            Assert.True(MaxDifference(original, styled) > 2, $"{look} did not change the displayed frame");

            if (look == VideoLook.Monochrome)
            {
                var center = styled.Row(0, styled.Height / 2);
                for (var x = 0; x < styled.Width; x++)
                {
                    Assert.InRange(Math.Abs(center[x * 4] - center[(x * 4) + 1]), 0, 1);
                    Assert.InRange(Math.Abs(center[(x * 4) + 1] - center[(x * 4) + 2]), 0, 1);
                }
            }
        }

        presenter.SetLook(VideoLook.Original);
        presenter.Redraw();
        using var restored = presenter.ReadBack();
        Assert.Equal(0, MaxDifference(original, restored));

        // Invalid saved styles may never leave playback with an unpredictable shader state.
        presenter.SetLook((VideoLook)999);
        presenter.Redraw();
        using var reset = presenter.ReadBack();
        Assert.Equal(0, MaxDifference(original, reset));
    }

    [Theory]
    [InlineData(VideoEffect.PrismFlow)]
    [InlineData(VideoEffect.NeonEdges)]
    [InlineData(VideoEffect.PixelDrift)]
    [InlineData(VideoEffect.Kaleidoscope)]
    [InlineData(VideoEffect.InkTrace)]
    [InlineData(VideoEffect.TopographicContours)]
    [InlineData(VideoEffect.ChromaticContours)]
    [InlineData(VideoEffect.LiquidGlass)]
    [InlineData(VideoEffect.SliceShift)]
    [InlineData(VideoEffect.Vortex)]
    [InlineData(VideoEffect.CursorLens)]
    [InlineData(VideoEffect.EdgeGravity)]
    [InlineData(VideoEffect.Ghostwire)]
    [InlineData(VideoEffect.GhostwireMask)]
    [InlineData(VideoEffect.ColourSpotlight)]
    [InlineData(VideoEffect.ReliefEtch)]
    public void EachSpatialEffectChangesTheImageButCanBeFullyDisabled(VideoEffect effect)
    {
        using var picture = DecodedPicture();
        picture.Pts = MediaTime.FromSeconds(1.5);
        using var presenter = D3D11Presenter.Offscreen(picture.Width, picture.Height);
        presenter.Present(picture);
        using var original = presenter.ReadBack();

        if (VideoEffects.UsesPointer(effect))
        {
            presenter.SetPointer(0.45f, 0.52f, true);
        }

        presenter.SetEffect(effect, 100);
        presenter.Redraw();
        using var altered = presenter.ReadBack();
        Assert.True(MaxDifference(original, altered) > 2, $"{effect} did not change picture geometry or detail");

        presenter.SetEffect(effect, 100, 175);
        presenter.Redraw();
        using var detailed = presenter.ReadBack();
        Assert.True(MaxDifference(altered, detailed) > 0, $"{effect}'s detail control did not change its pixels");

        presenter.SetEffect(effect, 0);
        presenter.Redraw();
        using var zeroIntensity = presenter.ReadBack();
        Assert.Equal(0, MaxDifference(original, zeroIntensity));

        presenter.SetEffect(VideoEffect.Off, 100);
        presenter.Redraw();
        using var off = presenter.ReadBack();
        Assert.Equal(0, MaxDifference(original, off));

        presenter.SetEffect((VideoEffect)999, 100);
        presenter.Redraw();
        using var invalid = presenter.ReadBack();
        Assert.Equal(0, MaxDifference(original, invalid));
    }

    [Fact]
    public void ImageAwareEffectsAreDrivenByFrameStructureAndLookControlsAreIndependent()
    {
        using var picture = VideoFrame.Rent(PixelFormat.Bgra32, 80, 64);
        for (var y = 0; y < picture.Height; y++)
        {
            var row = picture.Row(0, y);
            for (var x = 0; x < picture.Width; x++)
            {
                byte c = (byte)(x < 40 ? 12 : 235);
                row[x * 4] = c;
                row[x * 4 + 1] = c;
                row[x * 4 + 2] = c;
                row[x * 4 + 3] = 255;
            }
        }

        using var presenter = D3D11Presenter.Offscreen(80, 64);
        picture.Pts = MediaTime.FromSeconds(1);
        presenter.Present(picture);
        using var baseline = presenter.ReadBack();

        presenter.SetEffect(VideoEffect.EdgeGravity, 100, 175);
        presenter.Redraw();
        using var edges = presenter.ReadBack();
        Assert.True(MaxDifference(baseline, edges) > 2, "The subject boundary should refract");

        presenter.SetEffect(VideoEffect.Off, 100);
        presenter.SetLook(VideoLook.Cinema, 150, 135);
        presenter.Redraw();
        using var cinematic = presenter.ReadBack();
        Assert.True(MaxDifference(baseline, cinematic) > 2);

        presenter.SetLook(VideoLook.Cinema, 100, 135);
        presenter.Redraw();
        using var normalIntensity = presenter.ReadBack();
        Assert.True(MaxDifference(cinematic, normalIntensity) > 0, "Intensity above 100 must not be silently clipped");

        presenter.SetLook(VideoLook.Cinema, 0, 100);
        presenter.Redraw();
        using var intensityZero = presenter.ReadBack();
        Assert.Equal(0, MaxDifference(baseline, intensityZero));

        presenter.SetLook(VideoLook.Original);
        presenter.SetEffect(VideoEffect.CursorLens, 100);
        presenter.SetPointer(float.NaN, float.PositiveInfinity, false);
        presenter.Redraw();
        using var notHovering = presenter.ReadBack();
        Assert.Equal(0, MaxDifference(baseline, notHovering));

        presenter.SetPointer(0.5f, 0.5f, true);
        presenter.Redraw();
        using var hovered = presenter.ReadBack();
        Assert.True(MaxDifference(baseline, hovered) > 2);
    }

    [Fact]
    public void GhostwireHighlightTheBoundaryNotTheUniformSurface()
    {
        using var picture = VideoFrame.Rent(PixelFormat.Bgra32, 96, 64);
        for (var y = 0; y < picture.Height; y++)
        {
            var row = picture.Row(0, y);
            for (var x = 0; x < picture.Width; x++)
            {
                byte value = (byte)(x < 48 ? 8 : 240);
                row[x * 4] = value;
                row[x * 4 + 1] = value;
                row[x * 4 + 2] = value;
                row[x * 4 + 3] = 255;
            }
        }

        using var presenter = D3D11Presenter.Offscreen(96, 64);
        presenter.Present(picture);
        presenter.SetEffect(VideoEffect.Ghostwire, 100);
        presenter.Redraw();
        using var contours = presenter.ReadBack();

        var flat = contours.Row(0, 32)[8 * 4 + 1];
        var boundary = contours.Row(0, 32)[47 * 4 + 1];
        Assert.True(boundary > flat + 40, $"Expected a distinct boundary: {boundary} versus {flat}");

        presenter.SetEffect(VideoEffect.Off, 100);
        presenter.Redraw();
        using var original = presenter.ReadBack();
        Assert.Equal(0, MaxDifference(original, picture));
    }

    [Fact]
    public void GhostwireMaskUsesOnlyVisibleEdgesAndFullyRestoresTheRecordedFrame()
    {
        using var picture = VideoFrame.Rent(PixelFormat.Bgra32, 96, 64);
        for (var y = 0; y < picture.Height; y++)
        {
            var row = picture.Row(0, y);
            for (var x = 0; x < picture.Width; x++)
            {
                byte sample = (byte)(x < 48 ? 8 : 240);
                row[x * 4] = sample;
                row[x * 4 + 1] = sample;
                row[x * 4 + 2] = sample;
                row[x * 4 + 3] = 255;
            }
        }

        using var presenter = D3D11Presenter.Offscreen(96, 64);
        presenter.Present(picture);
        using var original = presenter.ReadBack();

        presenter.SetEffect(VideoEffect.GhostwireMask, 100);
        presenter.Redraw();
        using var mask = presenter.ReadBack();
        var flat = mask.Row(0, 32)[8 * 4 + 1];
        var line = mask.Row(0, 32)[47 * 4 + 1];
        Assert.True(line > flat + 30, $"Visible contours must remain distinct: {line} and {flat}");
        Assert.True(MaxDifference(mask, original) > 10);

        presenter.SetEffect(VideoEffect.Off, 100);
        presenter.Redraw();
        using var restored = presenter.ReadBack();
        Assert.Equal(0, MaxDifference(restored, original));
    }

    [Theory]
    [InlineData(VideoEffect.NeonEdges)]
    [InlineData(VideoEffect.InkTrace)]
    [InlineData(VideoEffect.ChromaticContours)]
    [InlineData(VideoEffect.Ghostwire)]
    [InlineData(VideoEffect.GhostwireMask)]
    public void ContoursKeepSubtleLinesInsideAGreyObject(VideoEffect effect)
    {
        using var picture = VideoFrame.Rent(PixelFormat.Bgra32, 160, 96);
        for (var y = 0; y < picture.Height; y++)
        {
            var row = picture.Row(0, y);
            for (var x = 0; x < picture.Width; x++)
            {
                byte grey = (byte)(x < 30 || x >= 135 ? 35 : x >= 65 && x < 69 ? 126 : 110);
                var pixel = x * 4;
                row[pixel] = grey;
                row[pixel + 1] = grey;
                row[pixel + 2] = grey;
                row[pixel + 3] = 255;
            }
        }

        using var presenter = D3D11Presenter.Offscreen(160, 96);
        presenter.Present(picture);
        presenter.SetEffect(effect, 100, 175);
        presenter.Redraw();
        using var detailed = presenter.ReadBack();
        var flat = detailed.Row(0, 48)[54 * 4 + 1];
        var line = Enumerable.Range(63, 8).Select(x => (int)detailed.Row(0, 48)[x * 4 + 1]).ToArray();
        Assert.True(line.Any(g => Math.Abs(g - flat) >= 6),
            $"{effect} must respond to grey-on-grey internal edges: flat={flat}; lines={string.Join(",", line)}");

        presenter.SetEffect(VideoEffect.Off, 100);
        presenter.Redraw();
        using var restored = presenter.ReadBack();
        Assert.Equal(0, MaxDifference(picture, restored));
    }

    [Theory]
    [InlineData(VideoEffect.Ghostwire)]
    [InlineData(VideoEffect.GhostwireMask)]
    public void ContourSensitivityExposesWeakerInternalLines(VideoEffect effect)
    {
        using var picture = VideoFrame.Rent(PixelFormat.Bgra32, 160, 96);
        for (var y = 0; y < picture.Height; y++)
        {
            var row = picture.Row(0, y);
            for (var x = 0; x < picture.Width; x++)
            {
                var grey = (byte)(x >= 70 && x < 74 ? 119 : 108);
                row[x * 4] = grey;
                row[x * 4 + 1] = grey;
                row[x * 4 + 2] = grey;
                row[x * 4 + 3] = 255;
            }
        }

        using var presenter = D3D11Presenter.Offscreen(160, 96);
        presenter.Present(picture);
        presenter.SetEffect(effect, 100, 25);
        presenter.Redraw();
        using var low = presenter.ReadBack();
        presenter.SetEffect(effect, 100, 175);
        presenter.Redraw();
        using var high = presenter.ReadBack();
        var lowFlat = low.Row(0, 48)[40 * 4 + 1];
        var highFlat = high.Row(0, 48)[40 * 4 + 1];
        var lowContrast = Enumerable.Range(68, 8).Max(x => Math.Abs(low.Row(0, 48)[x * 4 + 1] - lowFlat));
        var highContrast = Enumerable.Range(68, 8).Max(x => Math.Abs(high.Row(0, 48)[x * 4 + 1] - highFlat));
        Assert.True(highContrast > lowContrast + 6,
            $"{effect}: raising contour detail must reveal weak lines ({lowContrast} vs {highContrast})");
    }

    [Theory]
    [InlineData(VideoEffect.Ghostwire)]
    [InlineData(VideoEffect.GhostwireMask)]
    public void GhostwireSeparatesDifferentColoursAtNearlyEqualBrightness(VideoEffect effect)
    {
        using var picture = VideoFrame.Rent(PixelFormat.Bgra32, 160, 96);
        for (var y = 0; y < picture.Height; y++)
        {
            var row = picture.Row(0, y);
            for (var x = 0; x < picture.Width; x++)
            {
                var tint = x >= 74 && x < 88;
                row[x * 4] = tint ? (byte)225 : (byte)110;
                row[x * 4 + 1] = tint ? (byte)81 : (byte)110;
                row[x * 4 + 2] = tint ? (byte)145 : (byte)110;
                row[x * 4 + 3] = 255;
            }
        }

        using var presenter = D3D11Presenter.Offscreen(160, 96);
        presenter.Present(picture);
        presenter.SetEffect(effect, 100, 175);
        presenter.Redraw();
        using var result = presenter.ReadBack();
        var flat = result.Row(0, 48)[42 * 4 + 1];
        var edge = Enumerable.Range(72, 5).Max(x => (int)result.Row(0, 48)[x * 4 + 1]);
        Assert.True(edge > flat + 10,
            $"{effect} must see colour-only contours: edge={edge}, uniform={flat}");
    }

    [Fact]
    public void ColourSpotlightOnlyRespondsToTheChosenPixelAndClearsWithThePointer()
    {
        using var picture = VideoFrame.Rent(PixelFormat.Bgra32, 96, 64);
        for (var y = 0; y < picture.Height; y++)
        {
            var row = picture.Row(0, y);
            for (var x = 0; x < picture.Width; x++)
            {
                row[x * 4] = (byte)(x < 48 ? 10 : 210);
                row[x * 4 + 1] = 20;
                row[x * 4 + 2] = (byte)(x < 48 ? 220 : 10);
                row[x * 4 + 3] = 255;
            }
        }

        using var presenter = D3D11Presenter.Offscreen(96, 64);
        presenter.Present(picture);
        using var baseline = presenter.ReadBack();

        presenter.SetEffect(VideoEffect.ColourSpotlight, 100);
        presenter.Redraw();
        using var noPointer = presenter.ReadBack();
        Assert.Equal(0, MaxDifference(baseline, noPointer));

        presenter.SetPointer(0.25f, 0.5f, true);
        presenter.Redraw();
        using var selected = presenter.ReadBack();
        Assert.True(MaxDifference(baseline, selected) > 2);
        Assert.True(selected.Row(0, 32)[12 * 4 + 2] > selected.Row(0, 32)[80 * 4 + 2]);

        presenter.SetPointer(float.NaN, float.NaN, false);
        presenter.Redraw();
        using var released = presenter.ReadBack();
        Assert.Equal(0, MaxDifference(baseline, released));
    }

    [Fact]
    public void SpatialEffectsAlsoWorkOnBgraAndRespondToPlaybackTime()
    {
        using var picture = VideoFrame.Rent(PixelFormat.Bgra32, 64, 64);
        for (var y = 0; y < picture.Height; y++)
        {
            var row = picture.Row(0, y);
            for (var x = 0; x < picture.Width; x++)
            {
                row[(x * 4)] = (byte)(x * 4);
                row[(x * 4) + 1] = (byte)(y * 4);
                row[(x * 4) + 2] = (byte)(Math.Abs(x - y) * 4);
                row[(x * 4) + 3] = 255;
            }
        }

        using var presenter = D3D11Presenter.Offscreen(64, 64);
        presenter.SetEffect(VideoEffect.PrismFlow, 100);
        picture.Pts = MediaTime.FromSeconds(1);
        presenter.Present(picture);
        using var first = presenter.ReadBack();
        picture.Pts = MediaTime.FromSeconds(2.5);
        presenter.Present(picture);
        using var second = presenter.ReadBack();
        Assert.True(MaxDifference(first, second) > 2, "Prism flow should follow video time");

        presenter.SetEffect(VideoEffect.Off, 65);
        presenter.Redraw();
        using var restored = presenter.ReadBack();
        Assert.Equal(0, MaxDifference(restored, picture));
    }

    [Fact]
    public void TenBitP010EffectsUseTheSameShaderAsEightBitVideo()
    {
        using var picture = VideoFrame.Rent(PixelFormat.P010, 64, 32);
        picture.Color = new ColorInfo(ColorMatrix.Bt709, ColorTransfer.Bt709, ColorPrimaries.Bt709, true);
        for (var y = 0; y < picture.Height; y++)
        {
            var pixels = picture.Row(0, y);
            for (var x = 0; x < picture.Width; x++)
            {
                var brightness = (ushort)(64 + (x * 13 % 880));
                var packed = (ushort)(brightness << 6);
                pixels[x * 2] = (byte)packed;
                pixels[x * 2 + 1] = (byte)(packed >> 8);
            }
        }

        for (var y = 0; y < picture.Height / 2; y++)
        {
            var chroma = picture.Row(1, y);
            for (var i = 0; i < chroma.Length; i += 2)
            {
                chroma[i] = 0;
                chroma[i + 1] = 0x80;
            }
        }

        using var presenter = D3D11Presenter.Offscreen(64, 32);
        presenter.Present(picture);
        using var original = presenter.ReadBack();
        presenter.SetEffect(VideoEffect.NeonEdges, 100);
        presenter.Redraw();
        using var outlines = presenter.ReadBack();
        Assert.True(MaxDifference(original, outlines) > 2);

        presenter.SetEffect(VideoEffect.Off, 100);
        presenter.Redraw();
        using var restored = presenter.ReadBack();
        Assert.Equal(0, MaxDifference(original, restored));
    }

    [Fact]
    public void APictureIsLetterboxedToItsDisplayAspect()
    {
        using var picture = DecodedPicture();
        using var presenter = D3D11Presenter.Offscreen(256, 256);

        presenter.Present(picture);
        using var drawn = presenter.ReadBack();

        // 128x72 in a square: 256x144 in the middle, black bars of 56 rows above and below.
        Assert.All(drawn.Row(0, 10).ToArray().Where((_, i) => i % 4 != 3), value => Assert.Equal(0, value));
        Assert.All(drawn.Row(0, 245).ToArray().Where((_, i) => i % 4 != 3), value => Assert.Equal(0, value));
        Assert.Contains(drawn.Row(0, 128).ToArray(), value => value > 32);
    }

    [Fact]
    public void ClearingForSoundForgetsTheLastPictureAndDrawsBlack()
    {
        using var picture = DecodedPicture();
        using var presenter = D3D11Presenter.Offscreen(picture.Width, picture.Height);

        presenter.Present(picture);
        presenter.Clear();
        using var cleared = presenter.ReadBack();

        Assert.All(cleared.Plane(0).ToArray().Where((_, i) => i % 4 != 3), value => Assert.Equal(0, value));
    }

    [Theory]
    [InlineData(PixelFormat.Bgra32)]
    [InlineData(PixelFormat.I420)]
    [InlineData(PixelFormat.P010)]
    public void OtherFormatsAreDrawnToo(PixelFormat format)
    {
        // A mid-grey picture in each format: BGRA as it is, I420 through the CPU, P010 on the card.
        using var picture = VideoFrame.Rent(format, 16, 8);
        picture.Color = new ColorInfo(ColorMatrix.Bt709, ColorTransfer.Bt709, ColorPrimaries.Bt709, true);
        for (var plane = 0; plane < format.PlaneCount(); plane++)
        {
            var bytes = picture.Plane(plane);
            if (format == PixelFormat.P010)
            {
                for (var i = 0; i + 1 < bytes.Length; i += 2)
                {
                    (bytes[i], bytes[i + 1]) = (0x00, 0x80);
                }
            }
            else
            {
                bytes.Fill(128);
            }
        }

        using var presenter = D3D11Presenter.Offscreen(16, 8);
        presenter.Present(picture);
        using var drawn = presenter.ReadBack();

        Assert.All(drawn.Row(0, 4).ToArray().Where((_, i) => i % 4 != 3), value => Assert.InRange(value, 126, 130));
    }
}
