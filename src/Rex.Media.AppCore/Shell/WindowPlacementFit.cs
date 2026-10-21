using Rex.Media.Settings;

namespace Rex.Media.AppCore.Shell;

/// <summary>A screen's work area (the screen less the taskbar), in the desktop's pixels.</summary>
public readonly record struct ScreenArea(int X, int Y, int Width, int Height);

/// <summary>
/// Where the window opens. The place it was left is kept while the screen it was on is still
/// there, but a window saved on a screen since unplugged (a laptop away from its monitor, a remote
/// session at another size) would open where nobody can see or reach it: it comes onto the first
/// screen instead, centred, and never larger than that screen's work area. The title bar is what
/// must be reachable, so a window may hang off an edge as long as enough of its top shows.
/// </summary>
public static class WindowPlacementFit
{
    /// <summary>The smallest window the controls can be used in, as the window's own minimum.</summary>
    public const int MinimumWidth = 360;

    /// <inheritdoc cref="MinimumWidth"/>
    public const int MinimumHeight = 150;

    /// <summary>How much of the title bar must show on a screen, across and down, to drag it back.</summary>
    private const int ReachableWidth = 120;

    private const int ReachableHeight = 24;

    /// <summary>
    /// The placement to open at: <paramref name="saved"/> when its title bar shows on one of
    /// <paramref name="screens"/> (the first being the main one), moved onto the main screen when
    /// it does not; null stays null (the window's own default) and so does a placement when no
    /// screen is known.
    /// </summary>
    public static WindowPlacement? Fit(WindowPlacement? saved, IReadOnlyList<ScreenArea> screens)
    {
        ArgumentNullException.ThrowIfNull(screens);
        if (saved is null || screens.Count == 0)
        {
            return saved;
        }

        foreach (var screen in screens)
        {
            if (Overlap(saved.X, Math.Max(MinimumWidth, saved.Width), screen.X, screen.Width) >= ReachableWidth
                && Overlap(saved.Y, ReachableHeight, screen.Y, screen.Height) >= ReachableHeight)
            {
                return saved with { Width = Width(saved, screen), Height = Height(saved, screen) };
            }
        }

        var main = screens[0];
        var (width, height) = (Width(saved, main), Height(saved, main));
        return saved with { X = main.X + ((main.Width - width) / 2), Y = main.Y + ((main.Height - height) / 2), Width = width, Height = height };
    }

    private static int Width(WindowPlacement saved, ScreenArea screen) =>
        Math.Clamp(saved.Width, Math.Min(MinimumWidth, screen.Width), screen.Width);

    private static int Height(WindowPlacement saved, ScreenArea screen) =>
        Math.Clamp(saved.Height, Math.Min(MinimumHeight, screen.Height), screen.Height);

    /// <summary>How long the stretch [start, start + length) shares with [otherStart, otherStart + otherLength).</summary>
    private static long Overlap(long start, long length, long otherStart, long otherLength) =>
        Math.Max(0, Math.Min(start + length, otherStart + otherLength) - Math.Max(start, otherStart));
}
