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
    public void EachSpatialEffectChangesTheImageButCanBeFullyDisabled(VideoEffect effect)
    {
        using var picture = DecodedPicture();
        picture.Pts = MediaTime.FromSeconds(1.5);
        using var presenter = D3D11Presenter.Offscreen(picture.Width, picture.Height);
        presenter.Present(picture);
        using var original = presenter.ReadBack();

        presenter.SetEffect(effect, 100);
        presenter.Redraw();
        using var altered = presenter.ReadBack();
        Assert.True(MaxDifference(original, altered) > 2, $"{effect} did not change picture geometry or detail");

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
