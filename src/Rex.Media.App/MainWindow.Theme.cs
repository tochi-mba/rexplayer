using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Rex.Media.Settings;

namespace Rex.Media.App;

public sealed partial class MainWindow
{
    /// <summary>Light, dark or Windows' choice; the title bar follows the content.</summary>
    private void ApplyTheme()
    {
        Root.RequestedTheme = _settings.Theme switch
        {
            ThemeChoice.Light => ElementTheme.Light,
            ThemeChoice.Dark => ElementTheme.Dark,
            _ => ElementTheme.Default,
        };
        AppWindow.TitleBar.PreferredTheme = _settings.Theme switch
        {
            ThemeChoice.Light => TitleBarTheme.Light,
            ThemeChoice.Dark => TitleBarTheme.Dark,
            _ => TitleBarTheme.UseDefaultAppMode,
        };
    }

    /// <summary>
    /// The library lines whose colours come from code follow a change of theme at once; the rest
    /// of the window follows by itself, through its theme resources.
    /// </summary>
    private void RefreshThemedRows()
    {
        foreach (var row in _libraryRows.Concat(_homeShelfRows).OfType<MusicRow>())
        {
            row.RefreshTheme();
        }
    }
}
