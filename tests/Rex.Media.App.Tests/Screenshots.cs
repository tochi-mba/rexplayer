using System.IO;
using System.Text.Json;
using System.Windows.Automation;
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

    private static readonly JsonSerializerOptions MetadataJsonOptions = new() { WriteIndented = true };

    private static bool Recording => Environment.GetEnvironmentVariable("REXPLAYER_WRITE_SCREENSHOTS") == "1";

    private static string Baselines => RepoPaths.Combine("tests", "screenshot-baselines", Lane);

    private static string Output => RepoPaths.Combine("artifacts", "screenshots", Lane);

    /// <summary>
    /// Compares the app's window, or the control <paramref name="automationId"/> within it, with the
    /// baseline called <paramref name="name"/> (letters, digits and hyphens). Wait for the layout to
    /// settle first (a condition on the controls shown), since a shot is taken at once.
    /// </summary>
    public static void Match(AppProcess app, string name, string? automationId = null,
        int pixelThreshold = DefaultPixelThreshold, double maxDifferingRatio = DefaultMaxDifferingRatio, int maxViewportHeightChange = 0)
    {
        var scene = ScreenshotScenes.Find(name);
        // WinUI exposes UIA elements before they have been painted. Require the
        // scene's semantic contract first, then independently inspect pixels.
        var ready = Wait.Within(AppProcess.Patience, () =>
            scene.ExpectedAutomationIds.All(app.IsShown) &&
            scene.ExpectedVisibleData.All(item =>
                app.IsShown(item.AutomationId) && app.Text(item.AutomationId) == item.Name));
        if (!ready)
        {
            // A missing UIA witness is still a useful screenshot failure, not an
            // excuse to omit the screenshot and the application's diagnostic log.
            try
            {
                var failedFrame = scene.CaptureMode == "desktop"
                    ? WindowPicture.TakeDesktop(app.Handle)
                    : WindowPicture.Take(app.Handle);
                WriteCaptureMetadata(scene, app, failedFrame, automationId, pixelThreshold,
                    maxDifferingRatio, false, false, false);
                Save(Path.Combine(Output, "actual", name + ".png"), failedFrame);
            }
            catch (Exception error)
            {
                Directory.CreateDirectory(Path.Combine(Output, "actual"));
                File.WriteAllText(Path.Combine(Output, "actual", name + ".capture-error.txt"), error.ToString());
                File.WriteAllText(Path.Combine(Output, "actual", name + ".log"), app.LogText);
            }

            Assert.Fail($"Screenshot {name} never reached its declared UI Automation readiness state. See its captured evidence.");
        }

        // A text label, playback button or list pane can be visually uniform
        // while the surrounding scene genuinely renders. Only the actual
        // compositor-backed dialogs and menus need a varied control crop.
        var needsPixelWitness = scene.CaptureMode == "desktop" &&
            (scene.Route.StartsWith("settings/", StringComparison.Ordinal) ||
                scene.Route.StartsWith("dialog/", StringComparison.Ordinal) ||
                scene.Route.StartsWith("menu/", StringComparison.Ordinal));
        (int Width, int Height, byte[] Bgra) picture = default;
        (int Width, int Height, byte[] Bgra) witness = default;
        var rendered = Wait.Within(TimeSpan.FromSeconds(5), () =>
        {
            var captured = scene.CaptureMode == "desktop"
                ? WindowPicture.TakeDesktop(app.Handle)
                : WindowPicture.Take(app.Handle);
            picture = captured;
            if (needsPixelWitness && !TryCropControl(app, captured, scene.ReadinessId, out witness))
            {
                return false;
            }

            if (automationId is not null && !TryCropControl(app, captured, automationId, out picture))
            {
                picture = captured;
                return false;
            }

            return HasVisibleVariation(picture) &&
                (!needsPixelWitness || HasVisibleVariation(witness));
        });

        // Two independent compositor reads of the same stable scene must match.
        // Record the second frame and a diff even when the repeatability gate fails.
        // A bounded retry permits initial WinUI layout animations to finish without
        // silently treating permanently moving or stale content as deterministic.
        (int Width, int Height, byte[] Bgra) repeat = default;
        var repeatDifference = 1.0;
        var repeatable = rendered && Wait.Within(TimeSpan.FromSeconds(4), () =>
        {
            var captured = scene.CaptureMode == "desktop"
                ? WindowPicture.TakeDesktop(app.Handle)
                : WindowPicture.Take(app.Handle);
            repeat = captured;
            if (automationId is not null && !TryCropControl(app, captured, automationId, out repeat))
            {
                repeat = captured;
                return false;
            }

            if (repeat.Width != picture.Width || repeat.Height != picture.Height)
            {
                return false;
            }

            var (different, _) = Difference(picture, repeat, pixelThreshold);
            repeatDifference = different / (double)(picture.Width * picture.Height);
            return repeatDifference <= maxDifferingRatio;
        });

        // Persist artifacts *before* any screenshot assertion, including failed
        // readiness, missing popups, occlusion and mismatched committed baselines.
        WriteCaptureMetadata(scene, app, picture, automationId, pixelThreshold,
            maxDifferingRatio, true, rendered, needsPixelWitness && HasVisibleVariation(witness),
            repeatable, repeatDifference);
        Save(Path.Combine(Output, "actual", name + ".png"), picture);
        if (repeat.Bgra is { Length: > 0 })
        {
            Save(Path.Combine(Output, "repeat", name + ".png"), repeat);
            if (!repeatable && repeat.Width == picture.Width && repeat.Height == picture.Height)
            {
                var (_, repeatDiff) = Difference(picture, repeat, pixelThreshold);
                Save(Path.Combine(Output, "repeat-diff", name + ".png"), repeatDiff);
            }
        }
        if (automationId is not null)
        {
            Save(Path.Combine(Output, "crop", name + ".png"), picture);
        }

        Assert.True(WindowPicture.IsFullyOnDesktop(app.Handle),
            $"Screenshot {name} extends beyond the visible desktop. Black offscreen pixels must never become evidence.");
        Assert.True(HasVisibleVariation(picture),
            $"Screenshot {name} appears blank or uniform; wait for the expected UI before capturing it.");
        Assert.True(!needsPixelWitness || HasVisibleVariation(witness),
            $"Screenshot {name} does not visibly render the expected popup control {scene.ReadinessId}, despite UI Automation reporting it. See its actual capture.");
        Assert.True(rendered, $"Screenshot {name} did not render the declared image and pixel witness before the deadline.");
        Assert.True(repeatable,
            $"Screenshot {name} did not stabilize on a second desktop capture ({repeatDifference:P2} differing pixels). Inspect repeat and repeat-diff artifacts.");

        Compare(name, picture, pixelThreshold, maxDifferingRatio, maxViewportHeightChange);
    }

    private static void WriteCaptureMetadata(ScreenshotScenes.ScreenshotScene scene, AppProcess app,
        (int Width, int Height, byte[] Bgra) picture, string? automationId,
        int pixelThreshold, double maxDifferingRatio, bool ready, bool rendered, bool witnessVaried,
        bool repeatable = false, double repeatDifference = 1.0)
    {
        var dir = Path.Combine(Output, "actual");
        Directory.CreateDirectory(dir);
        var desktop = WindowPicture.DesktopBounds;
        var window = WindowPicture.Bounds(app.Handle);
        var details = new
        {
            sceneId = scene.Id,
            fixture = scene.Fixture,
            route = scene.Route,
            state = scene.State,
            theme = scene.Theme,
            captureMode = scene.CaptureMode,
            declaredDimensions = scene.Dimensions,
            actualDimensions = new { width = picture.Width, height = picture.Height },
            desktopBounds = new { desktop.Left, desktop.Top, desktop.Width, desktop.Height },
            windowBounds = new { window.Left, window.Top, window.Width, window.Height },
            fullyOnDesktop = WindowPicture.IsFullyOnDesktop(app.Handle),
            measuredDpi = WindowPicture.Dpi(app.Handle),
            actualHighContrast = WindowPicture.HighContrast,
            foregroundProcessId = WindowPicture.ForegroundProcessId,
            foregroundBelongsToApp = WindowPicture.ForegroundProcessId == (uint)app.ProcessId,
            cropAutomationId = automationId,
            readiness = scene.ReadinessSignal,
            readinessAchieved = ready,
            pixelReadinessAchieved = rendered,
            witnessVisiblyVaried = witnessVaried,
            repeatabilityAchieved = repeatable,
            repeatedDifferingRatio = repeatDifference,
            expectedAutomationIds = scene.ExpectedAutomationIds,
            observedAutomationIds = scene.ExpectedAutomationIds.Select(id => ObserveControl(app, id)).ToArray(),
            expectedVisibleData = scene.ExpectedVisibleData,
            owner = scene.Owner,
            baselineReviewState = scene.Baseline,
            baselineExists = File.Exists(Path.Combine(Baselines, scene.Id + ".png")),
            lane = Lane,
            commit = Environment.GetEnvironmentVariable("GITHUB_SHA"),
            pixelThreshold,
            maxDifferingRatio,
        };
        File.WriteAllText(Path.Combine(dir, scene.Id + ".json"),
            JsonSerializer.Serialize(details, MetadataJsonOptions));
        File.WriteAllText(Path.Combine(dir, scene.Id + ".log"), app.LogText);
    }

    /// <summary>
    /// Probe a UIA control without a blocking Find: vanished flyouts and invalid
    /// rectangles must time out with a retained whole-window image, not throw
    /// before diagnostics are persisted.
    /// </summary>
    private static bool TryCropControl(AppProcess app, (int Width, int Height, byte[] Bgra) frame,
        string automationId, out (int Width, int Height, byte[] Bgra) crop)
    {
        crop = default;
        try
        {
            var control = app.Window.FindFirst(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.AutomationIdProperty, automationId));
            if (control is null || control.Current.IsOffscreen)
            {
                return false;
            }

            var bounds = WindowPicture.Bounds(app.Handle);
            var area = control.Current.BoundingRectangle;
            if (area.Width <= 0 || area.Height <= 0 || area.Left < bounds.Left || area.Top < bounds.Top
                || area.Right > bounds.Right || area.Bottom > bounds.Bottom)
            {
                return false;
            }

            crop = Crop(frame, bounds, area);
            return true;
        }
        catch (ElementNotAvailableException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>Capture a one-shot UIA observation without a waiting lookup that could mask a failing scene.</summary>
    private static object ObserveControl(AppProcess app, string automationId)
    {
        try
        {
            var element = app.Window.FindFirst(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.AutomationIdProperty, automationId));
            if (element is not null)
            {
                var current = element.Current;
                return new
                {
                    automationId,
                    shown = !current.IsOffscreen,
                    current.Name,
                    current.IsEnabled,
                    bounds = current.BoundingRectangle.ToString(),
                };
            }
        }
        catch (ElementNotAvailableException)
        {
            // A transient control was replaced as the failing scene was recorded.
        }
        catch (InvalidOperationException)
        {
            // The UIA provider is gone; its absence is itself diagnostic evidence.
        }

        return new { automationId, shown = false, name = (string?)null, isEnabled = false, bounds = (string?)null };
    }

    /// <summary>
    /// A genuinely rendered window varies across its pixels. A failed PrintWindow capture, a
    /// hidden frame or an unmapped target is usually a uniform sheet of black or background.
    /// Reject that before accepting a new baseline (which would otherwise pass by default).
    /// </summary>
    internal static bool HasVisibleVariation((int Width, int Height, byte[] Bgra) picture)
    {
        // Check dimensions before integer multiplication so corrupt captures fail clearly.
        if (picture.Width <= 0 || picture.Height <= 0
            || (long)picture.Width * picture.Height > picture.Bgra.Length / 4)
        {
            return false;
        }

        var pixels = picture.Bgra;
        var total = picture.Width * picture.Height;
        var step = Math.Max(1, total / 4000);
        // A stride sharing a divisor with the image size can miss an alternating
        // pattern entirely (the original UI test failed on every-other-pixel stripes).
        // A coprime stride visits both sides of such patterns across the whole frame.
        static int Gcd(int left, int right)
        {
            while (right != 0)
            {
                var remainder = left % right;
                left = right;
                right = remainder;
            }

            return left;
        }

        while (Gcd(step, total) != 1)
        {
            step++;
        }

        var bins = new Dictionary<int, int>();
        var sampled = 0;
        var biggestBin = 0;
        var minB = 255;
        var minG = 255;
        var minR = 255;
        var maxB = 0;
        var maxG = 0;
        var maxR = 0;
        for (var p = 0; p < total; p += step)
        {
            var i = p * 4;
            var blue = pixels[i];
            var green = pixels[i + 1];
            var red = pixels[i + 2];
            minB = Math.Min(minB, blue);
            minG = Math.Min(minG, green);
            minR = Math.Min(minR, red);
            maxB = Math.Max(maxB, blue);
            maxG = Math.Max(maxG, green);
            maxR = Math.Max(maxR, red);

            // Bucket by visible colour rather than treating one arbitrary corner pixel as
            // the reference. A single bright cursor pixel must not make a blank window pass.
            var key = ((red >> 5) << 6) | ((green >> 5) << 3) | (blue >> 5);
            bins.TryGetValue(key, out var count);
            count++;
            bins[key] = count;
            biggestBin = Math.Max(biggestBin, count);
            sampled++;
        }

        var range = Math.Max(maxR - minR, Math.Max(maxG - minG, maxB - minB));
        return range > 16 && sampled - biggestBin >= Math.Max(8, sampled / 100);
    }

    /// <summary>The comparison itself, for a picture already taken.</summary>
    public static void Compare(string name, (int Width, int Height, byte[] Bgra) picture,
        int pixelThreshold = DefaultPixelThreshold, double maxDifferingRatio = DefaultMaxDifferingRatio, int maxViewportHeightChange = 0)
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

        // Include the committed baseline alongside actual and diff in CI's review
        // artifact so inspection never depends on a separate repository checkout.
        var baselineCopy = Path.Combine(Output, "baseline", name + ".png");
        Directory.CreateDirectory(Path.GetDirectoryName(baselineCopy)!);
        File.Copy(baseline, baselineCopy, overwrite: true);
        var expected = Load(baseline);
        Assert.True(expected.Width == picture.Width && Math.Abs(expected.Height - picture.Height) <= maxViewportHeightChange,
            $"Screenshot {name} is {picture.Width}x{picture.Height}, its baseline {expected.Width}x{expected.Height}. The picture is in {actual}.");
        if (expected.Height != picture.Height)
        {
            // A responsive list can gain or lose blank viewport below its realized cards.
            // Compare every pixel in the shared top-aligned area, not an outdated empty
            // strip at the bottom. This is opt-in for the specific view whose height moved.
            var sharedHeight = Math.Min(expected.Height, picture.Height);
            expected = (expected.Width, sharedHeight, expected.Bgra[..(expected.Width * sharedHeight * 4)]);
            picture = (picture.Width, sharedHeight, picture.Bgra[..(picture.Width * sharedHeight * 4)]);
        }

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
