using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Rex.Media.TestKit;

namespace Rex.Media.App.Tests;

/// <summary>
/// Screenshot tests: what a window (or one control in it) looks like, compared with a committed
/// baseline. Baselines belong to a lane, as the performance baselines do: CI's Windows runner is
/// "ci-windows" (REXPLAYER_UI_LANE), and any other machine is "local-" and its name, which only
/// records, because fonts, scaling and graphics drivers draw differently on every machine.
/// <para>
/// Every shot is written to artifacts/screenshots/&lt;lane&gt;/actual/&lt;name&gt;.png, which CI keeps as
/// the "screenshots" artefact. A new or changed baseline is made by copying CI's actual picture to
/// tests/screenshot-baselines/ci-windows/&lt;name&gt;.png (or, on that runner, by running with
/// REXPLAYER_WRITE_SCREENSHOTS=1), never from a local capture. A shot with no baseline yet passes
/// and says so. A mismatch fails and leaves the actual picture and a diff (changed pixels in red
/// over a faded copy of the baseline) beside it.
/// </para>
/// </summary>
internal static class Screenshots
{
    /// <summary>A pixel differs when any channel moves by more than this (0 to 255): antialiasing noise stays under it.</summary>
    public const int DefaultPixelThreshold = 40;

    /// <summary>A shot matches while no more than this share of its pixels differ.</summary>
    public const double DefaultMaxDifferingRatio = 0.005;

    public static string Lane => Environment.GetEnvironmentVariable("REXPLAYER_UI_LANE") is { Length: > 0 } lane
        ? lane
        : "local-" + Environment.MachineName.ToLowerInvariant();

    private static bool Recording => Environment.GetEnvironmentVariable("REXPLAYER_WRITE_SCREENSHOTS") == "1";

    private static string Baselines => RepoPaths.Combine("tests", "screenshot-baselines", Lane);

    private static string Output => RepoPaths.Combine("artifacts", "screenshots", Lane);

    /// <summary>
    /// Compares the app's window, or the control <paramref name="automationId"/> within it, with the
    /// baseline called <paramref name="name"/> (letters, digits and hyphens). Wait for the layout to
    /// settle first (a condition on the controls shown), since a shot is taken at once.
    /// </summary>
    public static void Match(AppProcess app, string name, string? automationId = null,
        int pixelThreshold = DefaultPixelThreshold, double maxDifferingRatio = DefaultMaxDifferingRatio)
    {
        var picture = WindowPicture.Take(app.Handle);
        if (automationId is not null)
        {
            picture = Crop(picture, WindowPicture.Bounds(app.Handle), app.Find(automationId).Current.BoundingRectangle);
        }

        Compare(name, picture, pixelThreshold, maxDifferingRatio);
    }

    /// <summary>The comparison itself, for a picture already taken.</summary>
    public static void Compare(string name, (int Width, int Height, byte[] Bgra) picture,
        int pixelThreshold = DefaultPixelThreshold, double maxDifferingRatio = DefaultMaxDifferingRatio)
    {
        Assert.Matches("^[A-Za-z0-9-]+$", name);
        var actual = Path.Combine(Output, "actual", name + ".png");
        Save(actual, picture);
        var baseline = Path.Combine(Baselines, name + ".png");
        if (Recording)
        {
            Save(baseline, picture);
            TestContext.Current.SendDiagnosticMessage($"Screenshot {name}: recorded as the {Lane} baseline.");
            return;
        }

        if (!File.Exists(baseline))
        {
            TestContext.Current.SendDiagnosticMessage(
                $"Screenshot {name}: no {Lane} baseline yet, so it only records. Copy {actual} from CI's screenshots artefact to tests/screenshot-baselines/ci-windows/ to start comparing.");
            return;
        }

        var expected = Load(baseline);
        Assert.True(expected.Width == picture.Width && expected.Height == picture.Height,
            $"Screenshot {name} is {picture.Width}x{picture.Height}, its baseline {expected.Width}x{expected.Height}. The picture is in {actual}.");
        var (differing, diff) = Difference(expected, picture, pixelThreshold);
        var ratio = differing / (double)(picture.Width * picture.Height);
        if (ratio > maxDifferingRatio)
        {
            var diffPath = Path.Combine(Output, "diff", name + ".png");
            Save(diffPath, diff);
            Assert.Fail($"Screenshot {name} differs from its baseline in {ratio:P2} of its pixels (at most {maxDifferingRatio:P2} allowed). See {actual} and {diffPath}.");
        }
    }

    /// <summary>How many pixels differ by more than the threshold, and a picture showing where.</summary>
    internal static (int Differing, (int Width, int Height, byte[] Bgra) Diff) Difference(
        (int Width, int Height, byte[] Bgra) expected, (int Width, int Height, byte[] Bgra) actual, int pixelThreshold)
    {
        var diff = new byte[actual.Bgra.Length];
        var differing = 0;
        for (var i = 0; i < diff.Length; i += 4)
        {
            var moved = Math.Max(Math.Abs(expected.Bgra[i] - actual.Bgra[i]),
                Math.Max(Math.Abs(expected.Bgra[i + 1] - actual.Bgra[i + 1]), Math.Abs(expected.Bgra[i + 2] - actual.Bgra[i + 2])));
            if (moved > pixelThreshold)
            {
                differing++;
                (diff[i], diff[i + 1], diff[i + 2]) = (0, 0, 255);
            }
            else
            {
                var grey = (byte)(192 + ((expected.Bgra[i] + expected.Bgra[i + 1] + expected.Bgra[i + 2]) / 12));
                (diff[i], diff[i + 1], diff[i + 2]) = (grey, grey, grey);
            }

            diff[i + 3] = 255;
        }

        return (differing, (actual.Width, actual.Height, diff));
    }

    /// <summary>The part of a window's picture under <paramref name="area"/>, both in screen pixels.</summary>
    internal static (int Width, int Height, byte[] Bgra) Crop((int Width, int Height, byte[] Bgra) picture, System.Windows.Rect window, System.Windows.Rect area)
    {
        var left = Math.Clamp((int)Math.Round(area.Left - window.Left), 0, picture.Width);
        var top = Math.Clamp((int)Math.Round(area.Top - window.Top), 0, picture.Height);
        var width = Math.Clamp((int)Math.Round(area.Width), 0, picture.Width - left);
        var height = Math.Clamp((int)Math.Round(area.Height), 0, picture.Height - top);
        Assert.True(width > 0 && height > 0, "The control is not inside the window's picture.");
        var pixels = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            Buffer.BlockCopy(picture.Bgra, (((top + y) * picture.Width) + left) * 4, pixels, y * width * 4, width * 4);
        }

        return (width, height, pixels);
    }

    private static void Save(string path, (int Width, int Height, byte[] Bgra) picture)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(picture.Width, picture.Height, 96, 96, PixelFormats.Bgra32, null, Opaque(picture.Bgra), picture.Width * 4)));
        using var file = File.Create(path);
        encoder.Save(file);
    }

    private static (int Width, int Height, byte[] Bgra) Load(string path)
    {
        using var file = File.OpenRead(path);
        var frame = new FormatConvertedBitmap(BitmapDecoder.Create(file, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0], PixelFormats.Bgra32, null, 0);
        var pixels = new byte[frame.PixelWidth * frame.PixelHeight * 4];
        frame.CopyPixels(pixels, frame.PixelWidth * 4, 0);
        return (frame.PixelWidth, frame.PixelHeight, pixels);
    }

    /// <summary>PrintWindow leaves alpha undefined; a saved shot is always opaque.</summary>
    private static byte[] Opaque(byte[] bgra)
    {
        var copy = (byte[])bgra.Clone();
        for (var i = 3; i < copy.Length; i += 4)
        {
            copy[i] = 255;
        }

        return copy;
    }
}
