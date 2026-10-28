namespace Rex.Media.AppCore.Shell;

/// <summary>
/// What a platform's dialog adds around its content: <paramref name="Across"/> is its padding and
/// border side to side, <paramref name="Around"/> its title, padding and buttons top to bottom, and
/// <paramref name="Tallest"/> the tallest dialog the platform draws at all.
/// </summary>
public readonly record struct DialogChrome(double Across, double Around, double Tallest);

/// <summary>
/// How big the window's dialogs are (UI-10, UI-17). Ordinary dialogs (a question, a field or two)
/// share one width, a dialog that lists things side by side is wider, and none comes closer than a
/// margin to the window's edges, so a dialog never touches the title bar or covers the whole
/// window. In a window too small for that, a dialog takes what there is, down to a floor that keeps
/// its controls usable. Sizes are of the dialog's content; the platform's <see cref="DialogChrome"/>
/// is added around it.
/// </summary>
public static class DialogLayout
{
    /// <summary>The content width of an ordinary dialog.</summary>
    public const double OrdinaryWidth = 440;

    /// <summary>The content width of a dialog that sets things side by side, such as the shortcut sheet.</summary>
    public const double WideWidth = 560;

    /// <summary>The space kept between a dialog and the window's edges.</summary>
    public const double Margin = 24;

    /// <summary>The space kept in a small window, where every pixel is needed for the dialog.</summary>
    public const double SmallMargin = 8;

    /// <summary>The narrowest and the shortest content a dialog is given, however small the window.</summary>
    public const double MinimumWidth = 240;

    /// <inheritdoc cref="MinimumWidth"/>
    public const double MinimumHeight = 120;

    /// <summary>The space kept around a dialog in a window this big.</summary>
    public static double MarginIn(double windowWidth, double windowHeight) =>
        windowWidth < 600 || windowHeight < 480 ? SmallMargin : Margin;

    /// <summary>The width of a dialog's content that would like <paramref name="preferred"/>, in a window this big.</summary>
    public static double Width(double preferred, double windowWidth, double windowHeight, DialogChrome chrome) =>
        Math.Max(MinimumWidth, Math.Min(preferred, windowWidth - (2 * MarginIn(windowWidth, windowHeight)) - chrome.Across));

    /// <summary>
    /// The height of a dialog's content that would like <paramref name="preferred"/>, in a window
    /// this big: never more than the platform's tallest dialog leaves for content.
    /// </summary>
    public static double Height(double preferred, double windowWidth, double windowHeight, DialogChrome chrome)
    {
        var room = Math.Min(windowHeight - (2 * MarginIn(windowWidth, windowHeight)), chrome.Tallest) - chrome.Around;
        return Math.Max(MinimumHeight, Math.Min(preferred, room));
    }
}

/// <summary>
/// How Preferences fits the window (UI-10). In a wide window its sections are a rail beside the
/// page, with the search above the rail; in a narrower one a section picker and the search sit
/// above the page, side by side, or one above the other when even that is too narrow. The dialog
/// keeps one size while it is open, so it does not jump as a search filters the sections or a
/// shorter section is chosen.
/// </summary>
/// <param name="Wide">Whether the sections are a rail beside the page.</param>
/// <param name="Stacked">Whether the picker and the search sit one above the other.</param>
/// <param name="Width">The content's width.</param>
/// <param name="Height">The content's height.</param>
public readonly record struct PreferencesLayout(bool Wide, bool Stacked, double Width, double Height)
{
    /// <summary>The narrowest window that has room for the rail beside a page.</summary>
    public const double WideFrom = 860;

    /// <summary>The rail's width, and the space between it and the page.</summary>
    public const double RailWidth = 200;

    /// <inheritdoc cref="RailWidth"/>
    public const double Gap = 24;

    /// <summary>The widest a page gets: lines longer than this are hard to read.</summary>
    public const double PageWidth = 560;

    /// <summary>The content's width, rail and page together, in a wide window; and the page's own in a narrow one.</summary>
    public const double WideWidth = 830;

    /// <inheritdoc cref="WideWidth"/>
    public const double NarrowWidth = 560;

    /// <summary>The narrowest content that sets the picker and the search side by side.</summary>
    public const double SideBySideFrom = 440;

    /// <summary>The layout for a window this big.</summary>
    public static PreferencesLayout For(double windowWidth, double windowHeight, DialogChrome chrome)
    {
        var wide = windowWidth >= WideFrom;
        var width = DialogLayout.Width(wide ? WideWidth : NarrowWidth, windowWidth, windowHeight, chrome);
        var height = DialogLayout.Height(chrome.Tallest, windowWidth, windowHeight, chrome);
        return new PreferencesLayout(wide, !wide && width < SideBySideFrom, width, height);
    }
}
