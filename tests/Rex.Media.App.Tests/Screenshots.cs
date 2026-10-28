using System.Diagnostics;
using System.Globalization;
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
/// tests/screenshot-baselines/ci-windows/&lt;name&gt;.png. Existing committed baselines may
/// only be overwritten with both REXPLAYER_WRITE_SCREENSHOTS=1 and
/// REXPLAYER_VISUAL_APPROVAL_CONFIRMED=1 on the CI lane after inspecting the actual image.
/// A shot with no baseline yet passes
/// and says so. A mismatch fails and leaves the actual picture and a diff (changed pixels in red
/// over a faded copy of the baseline) beside it.
/// </para>
/// <para>
/// A shot is evidence only of a window that has finished drawing. Before capturing, the pointer
/// is moved off the window (no hover fill or tooltip from the test's last click), the scene's UI
/// Automation contract must hold, the window must have painted its content (its client area is
/// not the backdrop's one flat colour and the readiness control's box shows something drawn), and
/// two frames taken <see cref="SettleInterval"/> apart must agree, no pixel changing by more than
/// <see cref="SettledPixelThreshold"/>, so nothing animating (a selection pill sliding, an
/// expander opening, a fill fading) is caught mid-way.
/// </para>
/// </summary>
internal static class Screenshots
{
    /// <summary>A pixel differs when any channel moves by more than this (0 to 255): antialiasing noise stays under it.</summary>
    public const int DefaultPixelThreshold = 40;

    /// <summary>A shot matches while no more than this share of its pixels differ.</summary>
    public const double DefaultMaxDifferingRatio = 0.005;

    /// <summary>
    /// Within one run the same machine draws a still window identically, so between two frames a
    /// pixel has changed when a channel moves by more than this: a fade still under way shows.
    /// </summary>
    internal const int SettledPixelThreshold = 8;

    /// <summary>A control's box shows something drawn when at least this many of its pixels differ from its most common colour.</summary>
    internal const int MinimumInk = 8;

    /// <summary>
    /// Frames compared for stillness are at least this far apart. A control's transition lasts a
    /// few hundred milliseconds, so it moves visibly in this time, while two frames taken a few
    /// milliseconds apart can both land inside it and agree.
    /// </summary>
    internal static readonly TimeSpan SettleInterval = TimeSpan.FromMilliseconds(250);

    /// <summary>How long a scene may take to hold still before the older, tolerant rule decides.</summary>
    private static readonly TimeSpan SettleLimit = TimeSpan.FromSeconds(4);

    public static string Lane => Environment.GetEnvironmentVariable("REXPLAYER_UI_LANE") is { Length: > 0 } lane
        ? lane
        : "local-" + Environment.MachineName.ToLowerInvariant();

    private static readonly JsonSerializerOptions MetadataJsonOptions = new() { WriteIndented = true };

    private static bool Recording => Environment.GetEnvironmentVariable("REXPLAYER_WRITE_SCREENSHOTS") == "1";

    private static string Baselines => RepoPaths.Combine("tests", "screenshot-baselines", Lane);

    private static string Output => RepoPaths.Combine("artifacts", "screenshots", Lane);

    /// <summary>Pin the actual UIA host viewport before capturing a declared scene.</summary>
    internal static void PinViewport(AppProcess app, int width, int height)
    {
        var desktop = WindowPicture.DesktopBounds;
        Assert.True(width <= desktop.Width && height <= desktop.Height,
            $"A {width}x{height} desktop screenshot cannot fit the available {desktop.Width}x{desktop.Height} screen.");
        var transform = (TransformPattern)app.Window.GetCurrentPattern(TransformPattern.Pattern);
        transform.Resize(width, height);
        Wait.For(() =>
        {
            var actual = app.Window.Current.BoundingRectangle;
            return Math.Abs(actual.Width - width) < 2 && Math.Abs(actual.Height - height) < 2
                && WindowPicture.IsFullyOnDesktop(app.Handle);
        }, $"the {width}x{height} pinned screenshot viewport to fit on the visible desktop");
    }

    /// <summary>
    /// Compares the app's window, or the control <paramref name="automationId"/> within it, with the
    /// baseline called <paramref name="name"/> (letters, digits and hyphens). Wait for the layout to
    /// settle first (a condition on the controls shown), since a shot is taken at once.
    /// </summary>
    public static void Match(AppProcess app, string name, string? automationId = null,
        int pixelThreshold = DefaultPixelThreshold, double maxDifferingRatio = DefaultMaxDifferingRatio,
        int maxViewportHeightChange = 0, Func<bool>? extraReadiness = null)
    {
        var scene = ScreenshotScenes.Find(name);
        Assert.True(scene.RequiredLane == "ci-windows" || scene.RequiredLane == Lane,
            $"Scene {name} requires the real {scene.RequiredLane} desktop, not {Lane}.");
        // The test's last click left the pointer over a control: its hover fill, pressed look or
        // tooltip is not part of the scene.
        var pointerParked = Pointer.ParkOutsideWindow(app);
        // WinUI exposes UIA elements before they have been painted. Require the
        // scene's semantic contract first, then independently inspect pixels.
        var ready = Wait.Within(AppProcess.Patience, () =>
            scene.ExpectedAutomationIds.All(app.IsShown) &&
            scene.ExpectedVisibleData.All(item =>
                app.IsShown(item.AutomationId) && app.Text(item.AutomationId) == item.Name)
            && (extraReadiness?.Invoke() ?? true));
        if (!ready)
        {
            // A missing UIA witness is still a useful screenshot failure, not an
            // excuse to omit the screenshot and the application's diagnostic log.
            try
            {
                var failedFrame = Capture(app, scene);
                WriteCaptureMetadata(scene, app, failedFrame, automationId, pixelThreshold,
                    maxDifferingRatio, false, false, false);
                Save(Path.Combine(Output, "actual", name + ".png"), failedFrame);
                Save(Path.Combine(Output, "window", name + ".png"), failedFrame);
            }
            catch (Exception error)
            {
                Directory.CreateDirectory(Path.Combine(Output, "actual"));
                File.WriteAllText(Path.Combine(Output, "actual", name + ".capture-error.txt"), error.ToString());
                File.WriteAllText(Path.Combine(Output, "actual", name + ".log"), app.LogText);
            }

            Assert.Fail($"Screenshot {name} never reached its declared UI Automation readiness state. See its captured evidence.");
        }

        // Cards and banners fetch their pictures after the layout is ready, and a placeholder
        // holds still long enough to pass the two-frame check below. Capture only once the
        // window has no picture request queued or loading.
        var picturesSettled = Wait.Within(AppProcess.Patience, () => app.PicturesPending == 0);

        // A text label, playback button or list pane can be visually uniform
        // while the surrounding scene genuinely renders. Only the actual
        // compositor-backed dialogs and menus need a varied control crop.
        var needsPixelWitness = scene.CaptureMode == "desktop" &&
            (scene.Route.StartsWith("settings/", StringComparison.Ordinal) ||
                scene.Route.StartsWith("dialog/", StringComparison.Ordinal) ||
                scene.Route.StartsWith("menu/", StringComparison.Ordinal) ||
                scene.Route == "player/error");
        (int Width, int Height, byte[] Bgra) picture = default;
        (int Width, int Height, byte[] Bgra) fullWindow = default;
        (int Width, int Height, byte[] Bgra) witness = default;
        // UI Automation also reports controls the compositor has not drawn yet. On a slow runner
        // the window can still show nothing but its backdrop, one flat colour under a title bar
        // whose text alone makes the whole frame look varied, so wait (as long as any other UI
        // wait) until the window has painted its content before taking a frame as evidence.
        string? unpainted = null;
        var sinceCapture = Stopwatch.StartNew();
        var rendered = Wait.Within(AppProcess.Patience, () =>
        {
            try
            {
                var captured = Capture(app, scene);
                sinceCapture.Restart();
                fullWindow = captured;
                picture = captured;
                unpainted = Unpainted(app, captured, scene.ReadinessId, pixelThreshold);
                if (unpainted is not null)
                {
                    return false;
                }

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
            }
            catch (Exception error)
            {
                RecordCaptureError(app, name, error);
                return false;
            }
        });
        if (picture.Bgra is not { Length: > 0 })
        {
            WriteCaptureMetadata(scene, app, (0, 0, []), automationId, pixelThreshold,
                maxDifferingRatio, true, false, false);
            Assert.Fail($"Screenshot {name} could not capture any pixels. See its retained capture error and process log.");
            return;
        }

        // Two independent compositor reads of the same scene must agree before one becomes
        // evidence. A pill sliding along a list, an expander opening or a fill fading changes
        // little between two quick frames, so the frames are taken SettleInterval apart and must
        // not differ at all (no channel moving by more than SettledPixelThreshold). Successive
        // frames are compared, each with the one before, until a pair agrees. A scene that never
        // holds still (a spinning ring, a running clock) is judged by the older tolerance on the
        // last pair that met it, and its metadata and the test output say it never settled.
        (int Width, int Height, byte[] Bgra) repeat = default;
        (int Width, int Height, byte[] Bgra) repeatedWindow = default;
        ((int Width, int Height, byte[] Bgra) Picture, (int Width, int Height, byte[] Bgra) Window,
            (int Width, int Height, byte[] Bgra) Repeat, double Difference)? tolerated = null;
        var repeatDifference = 1.0;
        var changedPixels = -1;
        var settled = false;
        var advanceFrame = false;
        var settling = Stopwatch.StartNew();
        if (rendered)
        {
            _ = Wait.Within(SettleLimit, () =>
            {
                try
                {
                    if (advanceFrame && repeat.Bgra is { Length: > 0 })
                    {
                        picture = repeat;
                        fullWindow = repeatedWindow;
                        advanceFrame = false;
                    }

                    var early = SettleInterval - sinceCapture.Elapsed;
                    if (early > TimeSpan.Zero)
                    {
                        Thread.Sleep(early);
                    }

                    var captured = Capture(app, scene);
                    sinceCapture.Restart();
                    repeat = captured;
                    repeatedWindow = captured;
                    if (automationId is not null && !TryCropControl(app, captured, automationId, out repeat))
                    {
                        repeat = captured;
                        return false;
                    }

                    if (repeat.Width != picture.Width || repeat.Height != picture.Height)
                    {
                        advanceFrame = true;
                        return false;
                    }

                    repeatDifference = Changed(picture, repeat, pixelThreshold) / (double)(picture.Width * picture.Height);
                    changedPixels = Changed(picture, repeat, SettledPixelThreshold);
                    var picturesIdle = app.PicturesPending == 0;
                    settled = picturesIdle && changedPixels == 0;
                    if (!settled && picturesIdle && repeatDifference <= maxDifferingRatio)
                    {
                        tolerated = (picture, fullWindow, repeat, repeatDifference);
                    }

                    advanceFrame = !settled;
                    return settled;
                }
                catch (Exception error)
                {
                    RecordCaptureError(app, name, error);
                    return false;
                }
            });
        }

        var settleTime = settling.Elapsed;
        if (!settled && tolerated is { } moving)
        {
            (picture, fullWindow, repeat, repeatDifference) = moving;
            TestContext.Current.SendDiagnosticMessage(
                $"Screenshot {name}: still changing after {SettleLimit.TotalSeconds:0} s ({changedPixels} pixels moved between the last two frames), so the last pair within {maxDifferingRatio:P2} was kept. See repeat-diff/{name}.png.");
        }

        var repeatable = settled || tolerated is not null;

        // Persist artifacts *before* any screenshot assertion, including failed
        // readiness, missing popups, occlusion and mismatched committed baselines.
        WriteCaptureMetadata(scene, app, picture, automationId, pixelThreshold,
            maxDifferingRatio, true, rendered, needsPixelWitness && HasVisibleVariation(witness),
            repeatable, repeatDifference,
            new
            {
                pointerParked,
                painted = unpainted is null,
                unpainted,
                settled,
                changedPixels,
                settleMilliseconds = (int)settleTime.TotalMilliseconds,
                settleIntervalMilliseconds = (int)SettleInterval.TotalMilliseconds
            });
        Save(Path.Combine(Output, "actual", name + ".png"), picture);
        Save(Path.Combine(Output, "window", name + ".png"), fullWindow);
        if (repeat.Bgra is { Length: > 0 })
        {
            Save(Path.Combine(Output, "repeat", name + ".png"), repeat);
            if (!settled && repeat.Width == picture.Width && repeat.Height == picture.Height)
            {
                var (_, repeatDiff) = Difference(picture, repeat, SettledPixelThreshold);
                Save(Path.Combine(Output, "repeat-diff", name + ".png"), repeatDiff);
            }
        }
        if (automationId is not null)
        {
            Save(Path.Combine(Output, "crop", name + ".png"), picture);
        }

        Assert.True(scene.CaptureMode != "desktop" || WindowPicture.IsFullyOnDesktop(app.Handle),
            $"Screenshot {name} extends beyond the visible desktop. Black offscreen pixels must never become composited evidence.");
        Assert.True(ViewportMatchesDeclared(scene, app) is not false,
            $"Screenshot {name} does not have the declared {scene.Dimensions} viewport.");
        Assert.True(unpainted is null,
            $"Screenshot {name} was never painted: {unpainted}, {AppProcess.Patience.TotalSeconds:0} s after UI Automation reported {scene.ReadinessId}. "
            + $"The last frame is window/{name}.png in the screenshots artefact. A whole window that stays one colour is not being drawn "
            + "(minimised, covered, on a locked or disconnected desktop, or the app stalled before its first frame: see its .log beside it); "
            + "one control that stays blank is a rendering defect in the app.");
        Assert.True(HasVisibleVariation(picture),
            $"Screenshot {name} appears blank or uniform; wait for the expected UI before capturing it.");
        Assert.True(!needsPixelWitness || HasVisibleVariation(witness),
            $"Screenshot {name} does not visibly render the expected popup control {scene.ReadinessId}, despite UI Automation reporting it. See its actual capture.");
        Assert.True(picturesSettled, $"Screenshot {name} was taken while the window still had {app.PicturesPending} picture request(s) loading after {AppProcess.Patience.TotalSeconds:0} s.");
        Assert.True(rendered, $"Screenshot {name} did not render the declared image and pixel witness before the deadline.");
        Assert.True(repeatable,
            $"Screenshot {name} did not stabilize on a second desktop capture ({repeatDifference:P2} differing pixels). Inspect repeat and repeat-diff artifacts.");

        Compare(name, picture, pixelThreshold, maxDifferingRatio, maxViewportHeightChange);
    }

    private static void WriteCaptureMetadata(ScreenshotScenes.ScreenshotScene scene, AppProcess app,
        (int Width, int Height, byte[] Bgra) picture, string? automationId,
        int pixelThreshold, double maxDifferingRatio, bool ready, bool rendered, bool witnessVaried,
        bool repeatable = false, double repeatDifference = 1.0, object? capture = null)
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
            viewportMatchesDeclared = ViewportMatchesDeclared(scene, app),
            actualDimensions = new { width = picture.Width, height = picture.Height },
            fullWindowDimensions = WindowPicture.Bounds(app.Handle).Size.ToString(CultureInfo.InvariantCulture),
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
            // Whether the pointer was moved off the window, the window had painted, and two frames
            // SettleInterval apart agreed (or how many pixels were still changing).
            capture,
            expectedAutomationIds = scene.ExpectedAutomationIds,
            observedAutomationIds = scene.ExpectedAutomationIds.Select(id => ObserveControl(app, id)).ToArray(),
            // Some semantic text readiness controls (such as LibraryTitle) are
            // intentionally outside the scene's primary automation-id list.
            // Record them too, so a reviewer can detect an incorrect title even
            // if the screenshot captured colorful pixels.
            observedVisibleData = scene.ExpectedVisibleData
                .Select(value => ObserveControl(app, value.AutomationId)).ToArray(),
            // Keep an actual UIA witness for every capability-specific action in a
            // media-stage shot, including controls that must be hidden or disabled.
            // Reviewers can correlate these with the unmodified compositor image.
            observedStageAffordances = scene.Id.StartsWith("stage-", StringComparison.Ordinal)
                ? new[]
                {
                    "Stage", "SeekBar", "StartOverButton", "StageChaptersButton",
                    "MovieVersionButton", "PictureIntervalButton",
                    "Lyrics", "Cover", "Subtitles", "KineticCaptions", "PlayPauseButton",
                }.Select(id => ObserveControl(app, id)).ToArray()
                : Array.Empty<object>(),
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
    /// Null means an unresolved legacy dimension label. Pinned dimensions are
    /// checked in the UIA window coordinate system, not against a DPI-scaled
    /// image crop or the invisible Win32 resize border.
    /// </summary>
    private static bool? ViewportMatchesDeclared(ScreenshotScenes.ScreenshotScene scene, AppProcess app)
    {
        if (scene.Dimensions == "maximized")
        {
            var window = (WindowPattern)app.Window.GetCurrentPattern(WindowPattern.Pattern);
            return window.Current.WindowVisualState == WindowVisualState.Maximized;
        }

        var parts = scene.Dimensions.Split('x');
        if (parts.Length != 2 || !int.TryParse(parts[0], out var width)
            || !int.TryParse(parts[1], out var height))
        {
            return null;
        }

        var actual = app.Window.Current.BoundingRectangle;
        return Math.Abs(actual.Width - width) < 2 && Math.Abs(actual.Height - height) < 2;
    }

    private static (int Width, int Height, byte[] Bgra) Capture(AppProcess app, ScreenshotScenes.ScreenshotScene scene) =>
        scene.CaptureMode == "desktop" ? WindowPicture.TakeDesktop(app.Handle) : WindowPicture.Take(app.Handle);

    /// <summary>
    /// Why a frame does not show the window's content yet, or null once it does. A window WinUI
    /// has not drawn into shows only its backdrop (#202020 dark, #F3F3F3 light, or a plain grey
    /// through PrintWindow) across the whole client area, and a control not drawn yet leaves its
    /// box one flat colour although UI Automation already places it there.
    /// </summary>
    private static string? Unpainted(AppProcess app, (int Width, int Height, byte[] Bgra) frame,
        string readinessId, int pixelThreshold)
    {
        var window = WindowPicture.Bounds(app.Handle);
        var client = WindowPicture.ClientBounds(app.Handle);
        var content = Crop(frame, window, client);
        if (!HasVisibleVariation(content))
        {
            return $"its whole client area is one flat colour ({Hex(MostCommonColour(content))}), the backdrop of a window with nothing drawn in it";
        }

        try
        {
            var control = app.Window.FindFirst(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.AutomationIdProperty, readinessId));
            var area = control is null || control.Current.IsOffscreen ? System.Windows.Rect.Empty : control.Current.BoundingRectangle;
            area.Intersect(client);
            if (area.IsEmpty || area.Width < 1 || area.Height < 1)
            {
                return $"{readinessId} has no place inside the client area";
            }

            var box = Crop(frame, window, area);
            return HasInk(box, pixelThreshold) ? null
                : $"{readinessId} is one flat colour ({Hex(MostCommonColour(box))}) across its box {area.ToString(CultureInfo.InvariantCulture)}, so nothing is drawn where UI Automation places it";
        }
        catch (ElementNotAvailableException)
        {
            return $"{readinessId} was replaced while its pixels were checked";
        }
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
            if (area.Width <= 0 || area.Height <= 0
                || area.Right <= bounds.Left || area.Bottom <= bounds.Top
                || area.Left >= bounds.Right || area.Top >= bounds.Bottom)
            {
                return false;
            }

            // WinUI flyouts are independent popup HWNDs and can extend a few
            // pixels outside the owning window. The desktop screenshot contains
            // their visible intersection with that window, not the off-window
            // portion. Measure real pixels in that intersection instead of
            // discarding a genuinely visible popup witness.
            var visible = System.Windows.Rect.Intersect(bounds, area);
            crop = Crop(frame, bounds, visible);
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

    private static void RecordCaptureError(AppProcess app, string name, Exception error)
    {
        var dir = Path.Combine(Output, "actual");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, name + ".capture-error.txt"), error.ToString());
        File.WriteAllText(Path.Combine(dir, name + ".log"), app.LogText);
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
                    current.HelpText,
                    current.IsEnabled,
                    current.HasKeyboardFocus,
                    current.IsKeyboardFocusable,
                    bounds = current.BoundingRectangle.ToString(CultureInfo.InvariantCulture),
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

        return new
        {
            automationId,
            shown = false,
            name = (string?)null,
            helpText = (string?)null,
            isEnabled = false,
            hasKeyboardFocus = false,
            isKeyboardFocusable = false,
            bounds = (string?)null,
        };
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
        // A sparse native chapter list can have only 21 contrasting samples
        // among 3,808 while clearly showing three chapter labels. Do not
        // reject it solely because of the white space. Still reject lone
        // pixels, cursor specks and one-line artifacts: require contrast
        // distributed across both dimensions of a real document.
        if (range <= 16) return false;
        if (sampled - biggestBin >= Math.Max(8, sampled * 3 / 400)) return true;
        if (sampled - biggestBin < Math.Max(16, sampled / 200)) return false;
        var dominant = bins.MaxBy(pair => pair.Value).Key;
        var firstX = picture.Width;
        var firstY = picture.Height;
        var lastX = 0;
        var lastY = 0;
        for (var p = 0; p < total; p += step)
        {
            var i = p * 4;
            var key = ((pixels[i + 2] >> 5) << 6) | ((pixels[i + 1] >> 5) << 3) | (pixels[i] >> 5);
            if (key == dominant) continue;
            var x = p % picture.Width;
            var y = p / picture.Width;
            firstX = Math.Min(firstX, x);
            firstY = Math.Min(firstY, y);
            lastX = Math.Max(lastX, x);
            lastY = Math.Max(lastY, y);
        }

        return lastX - firstX >= 24 && lastY - firstY >= 24;
    }

    /// <summary>
    /// Whether a control's box shows anything drawn: at least <see cref="MinimumInk"/> of its
    /// pixels differ from its most common colour by more than <paramref name="pixelThreshold"/>.
    /// Unlike <see cref="HasVisibleVariation"/>, which judges a whole frame, a short label or a
    /// small button passes; a box an unpainted window leaves at its backdrop colour does not.
    /// </summary>
    internal static bool HasInk((int Width, int Height, byte[] Bgra) picture, int pixelThreshold)
    {
        if (picture.Width <= 0 || picture.Height <= 0
            || (long)picture.Width * picture.Height > picture.Bgra.Length / 4)
        {
            return false;
        }

        var (blue, green, red) = MostCommonColour(picture);
        var pixels = picture.Bgra;
        var ink = 0;
        for (var i = 0; i < picture.Width * picture.Height * 4; i += 4)
        {
            var moved = Math.Max(Math.Abs(pixels[i] - blue), Math.Max(Math.Abs(pixels[i + 1] - green), Math.Abs(pixels[i + 2] - red)));
            if (moved > pixelThreshold && ++ink >= MinimumInk)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The colour most of a picture's pixels have, judged from an even sample of at most about 4,000 of them.</summary>
    internal static (int Blue, int Green, int Red) MostCommonColour((int Width, int Height, byte[] Bgra) picture)
    {
        var counts = new Dictionary<int, int>();
        var total = picture.Width * picture.Height;
        var step = Math.Max(1, total / 4000);
        var (common, most) = (0, 0);
        for (var p = 0; p < total; p += step)
        {
            var i = p * 4;
            var key = (picture.Bgra[i + 2] << 16) | (picture.Bgra[i + 1] << 8) | picture.Bgra[i];
            counts.TryGetValue(key, out var count);
            counts[key] = ++count;
            if (count > most)
            {
                (common, most) = (key, count);
            }
        }

        return (common & 0xFF, (common >> 8) & 0xFF, common >> 16);
    }

    private static string Hex((int Blue, int Green, int Red) colour) =>
        string.Create(CultureInfo.InvariantCulture, $"#{colour.Red:X2}{colour.Green:X2}{colour.Blue:X2}");

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
            Assert.True(Lane == "ci-windows" &&
                Environment.GetEnvironmentVariable("REXPLAYER_VISUAL_APPROVAL_CONFIRMED") == "1",
                "Baseline writing requires an explicitly reviewed ci-windows image and confirmation; a local capture cannot become a CI baseline.");
            Assert.True(ScreenshotScenes.Find(name).Baseline == "ci-windows",
                "Unreviewed scenes must be inspected and intentionally promoted through a code review, not silently recorded as approved baselines.");
            Save(baseline, picture);
            TestContext.Current.SendDiagnosticMessage($"Screenshot {name}: explicitly recorded as a reviewed {Lane} baseline.");
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

    /// <summary>How many pixels of two same-sized pictures differ by more than the threshold, without drawing where.</summary>
    internal static int Changed((int Width, int Height, byte[] Bgra) first, (int Width, int Height, byte[] Bgra) second, int pixelThreshold)
    {
        var changed = 0;
        for (var i = 0; i < first.Width * first.Height * 4; i += 4)
        {
            if (Math.Max(Math.Abs(first.Bgra[i] - second.Bgra[i]),
                Math.Max(Math.Abs(first.Bgra[i + 1] - second.Bgra[i + 1]), Math.Abs(first.Bgra[i + 2] - second.Bgra[i + 2]))) > pixelThreshold)
            {
                changed++;
            }
        }

        return changed;
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
