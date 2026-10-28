using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Rex.Media.AppCore.Design;

namespace Rex.Media.App;

/// <summary>
/// The REX tokens for code that builds its own elements: App.xaml's brushes, WinUI's theme brushes
/// and <see cref="DesignTokens"/> colours. A themed brush is the one for the window's own theme,
/// which can differ from Windows' (the app's resources alone answer for Windows' theme, so a dark
/// window on a light desktop would get dark text on its dark surfaces).
/// </summary>
internal static class RexTheme
{
    private static Windows.UI.ViewManagement.AccessibilitySettings? s_accessibility;
    private static FrameworkElement? s_root;

    /// <summary>
    /// The element whose theme decides which themed brush code gets, the window's root, and what to
    /// do when its theme changes.
    /// </summary>
    public static void Follow(FrameworkElement root, Action themeChanged)
    {
        s_root = root;
        root.ActualThemeChanged += (_, _) => themeChanged();
    }

    /// <summary>A brush such as <c>RexOverlayTextBrush</c> or <c>TextFillColorPrimaryBrush</c>, for the window's theme.</summary>
    public static Brush Brush(string key) => (Brush)Resource(key);

    /// <summary>A resource for the window's theme: its themed version if it has one, else the shared one.</summary>
    public static object Resource(string key)
    {
        var resources = Application.Current.Resources;
        foreach (var theme in ThemeNames())
        {
            if (FindThemed(resources, key, theme) is { } found)
            {
                return found;
            }
        }

        return resources[key];
    }

    /// <summary>A <see cref="DesignTokens"/> colour, 0xAARRGGBB, as a XAML colour.</summary>
    public static Windows.UI.Color Color(uint argb) =>
        Windows.UI.Color.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);

    // WinUI's own dictionaries name the dark theme "Default".
    private static string[] ThemeNames() => (s_accessibility ??= new()).HighContrast ? ["HighContrast"]
        : s_root?.ActualTheme == ElementTheme.Light ? ["Light"]
        : ["Dark", "Default"];

    private static object? FindThemed(ResourceDictionary dictionary, string key, string theme)
    {
        if (dictionary.ThemeDictionaries.TryGetValue(theme, out var themed)
            && themed is ResourceDictionary forTheme
            && Find(forTheme, key) is { } found)
        {
            return found;
        }

        foreach (var merged in dictionary.MergedDictionaries)
        {
            if (FindThemed(merged, key, theme) is { } inMerged)
            {
                return inMerged;
            }
        }

        return null;
    }

    private static object? Find(ResourceDictionary dictionary, string key)
    {
        if (dictionary.TryGetValue(key, out var found))
        {
            return found;
        }

        foreach (var merged in dictionary.MergedDictionaries)
        {
            if (Find(merged, key) is { } inMerged)
            {
                return inMerged;
            }
        }

        return null;
    }
}
