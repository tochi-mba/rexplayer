namespace Rex.Media.AppCore.Design;

/// <summary>
/// What kind of control a screen shows in a place, in terms both shells can draw: the kind decides
/// its shape and its colours, so two shells that agree on the kind agree on the look.
/// </summary>
public enum ShellControlKind
{
    /// <summary>A round or square button that shows only an icon.</summary>
    Icon,

    /// <summary>An icon button that stays lit while its option is on.</summary>
    IconToggle,

    /// <summary>The one icon button a screen leads with, filled in the signal colour.</summary>
    AccentIcon,

    /// <summary>A button that shows words.</summary>
    Text,

    /// <summary>The one worded button a screen leads with, filled in the signal colour.</summary>
    AccentText,

    /// <summary>A worded button outlined so it can be seen over the picture or the dark stage.</summary>
    OverlayText,

    /// <summary>Text that is read, not pressed.</summary>
    Label,

    /// <summary>A bar dragged along to pick a value.</summary>
    Slider,
}

/// <summary>One control on a screen: its Windows automation id, which the Mac shell reports too, and its kind.</summary>
public sealed record ShellControl(string Id, ShellControlKind Kind);

/// <summary>
/// A screen both shells draw: the Look row of the capability matrix it proves and the controls it
/// always shows, in the order a person reads them.
/// </summary>
public sealed record ShellScreen(string LookRow, string Name, IReadOnlyList<ShellControl> Controls);

/// <summary>
/// The one description of each screen's structure, taken from the Windows window
/// (src/Rex.Media.App/MainWindow.xaml). Each shell reports what it draws in the form
/// <see cref="Describe"/> gives, and a test compares that with this description, so a control added
/// to, dropped from or moved in one shell fails the other until both agree. Controls that appear
/// only in special states are left out; a screen lists what it always shows. So is a control that
/// is a feature one platform does not have: Windows' Listen button (AU-25, the sound of another
/// app) answers to its own row of the matrix, not to the look of the bar it sits in.
/// </summary>
public static class ShellScreens
{
    /// <summary>The idle stage before anything plays (LOOK-01).</summary>
    public static ShellScreen Welcome { get; } = new("LOOK-01", "Welcome",
    [
        new("IdleKicker", ShellControlKind.Label),
        new("IdleTitle", ShellControlKind.Label),
        new("IdleHint", ShellControlKind.Label),
        new("WelcomeOpen", ShellControlKind.AccentText),
        new("WelcomeLibrary", ShellControlKind.OverlayText),
        new("WelcomeTagline", ShellControlKind.Label),
    ]);

    /// <summary>The seek row over the transport row at the foot of the player window (LOOK-02).</summary>
    public static ShellScreen Transport { get; } = new("LOOK-02", "Transport",
    [
        new("StartOverButton", ShellControlKind.Text),
        new("Elapsed", ShellControlKind.Label),
        new("SeekBar", ShellControlKind.Slider),
        new("Remaining", ShellControlKind.Label),
        new("PreviousButton", ShellControlKind.Icon),
        new("PlayPauseButton", ShellControlKind.AccentIcon),
        new("StopButton", ShellControlKind.Icon),
        new("NextButton", ShellControlKind.Icon),
        new("ShuffleButton", ShellControlKind.IconToggle),
        new("RepeatButton", ShellControlKind.IconToggle),
        new("PlaylistButton", ShellControlKind.IconToggle),
        new("LibraryButton", ShellControlKind.IconToggle),
        new("FullScreenButton", ShellControlKind.Icon),
        new("NowPlaying", ShellControlKind.Label),
        new("SpeedButton", ShellControlKind.Text),
        new("MuteButton", ShellControlKind.Icon),
        new("VolumeBar", ShellControlKind.Slider),
    ]);

    /// <summary>Every screen described so far, in the order of their Look rows.</summary>
    public static IReadOnlyList<ShellScreen> All { get; } = [Welcome, Transport];

    /// <summary>
    /// The stable spelling of a kind in a description. It is part of what a shell reports over
    /// automation, so it never changes once written.
    /// </summary>
    public static string Spell(ShellControlKind kind) => kind switch
    {
        ShellControlKind.Icon => "icon",
        ShellControlKind.IconToggle => "icon-toggle",
        ShellControlKind.AccentIcon => "accent-icon",
        ShellControlKind.Text => "text",
        ShellControlKind.AccentText => "accent-text",
        ShellControlKind.OverlayText => "overlay-text",
        ShellControlKind.Label => "label",
        ShellControlKind.Slider => "slider",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    /// <summary>
    /// The screen in one line, <c>Id:kind;Id:kind;...</c>: what a shell answers to <c>ui.describe</c>
    /// and what a test compares, so the two shells are held to the same text.
    /// </summary>
    public static string Describe(ShellScreen screen) =>
        string.Join(';', screen.Controls.Select(control => control.Id + ":" + Spell(control.Kind)));

    /// <summary>The screen that the Look row <paramref name="id"/> proves, or null when no screen is described for it.</summary>
    public static ShellScreen? ForLookRow(string id) =>
        All.FirstOrDefault(screen => string.Equals(screen.LookRow, id, StringComparison.Ordinal));
}
