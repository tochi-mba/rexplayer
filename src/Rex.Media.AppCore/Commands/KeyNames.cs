namespace Rex.Media.AppCore.Commands;

/// <summary>
/// Shortcuts as people read them (A11Y-02): on the shortcut sheet each key is a keycap with its
/// everyday name ("Page up", "]", the arrow itself, "Play/pause key" for the media key), and in
/// words for a screen reader ("Ctrl+Right arrow"). The canonical names of <see cref="KeyChord"/>
/// stay what the settings file stores and the menus show; these are only for reading.
/// </summary>
public static class KeyNames
{
    /// <summary>The named keys whose canonical name is not what people call them: keycap, then words.</summary>
    private static readonly Dictionary<string, (string Keycap, string Spoken)> Named = new(StringComparer.Ordinal)
    {
        ["Escape"] = ("Esc", "Escape"),
        ["PageUp"] = ("Page up", "Page up"),
        ["PageDown"] = ("Page down", "Page down"),
        ["Left"] = ("\u2190", "Left arrow"),
        ["Right"] = ("\u2192", "Right arrow"),
        ["Up"] = ("\u2191", "Up arrow"),
        ["Down"] = ("\u2193", "Down arrow"),
        ["Plus"] = ("+", "Plus"),
        ["Minus"] = ("-", "Minus"),
        ["Equals"] = ("=", "Equals"),
        ["LeftBracket"] = ("[", "Left bracket"),
        ["RightBracket"] = ("]", "Right bracket"),
        ["Slash"] = ("/", "Slash"),
        ["Comma"] = (",", "Comma"),
        ["Period"] = (".", "Period"),
        ["MediaPlayPause"] = ("Play/pause key", "Play/pause key"),
        ["MediaStop"] = ("Stop key", "Stop key"),
        ["MediaNext"] = ("Next track key", "Next track key"),
        ["MediaPrevious"] = ("Previous track key", "Previous track key"),
    };

    /// <summary>The modifiers in the order each platform writes them, as <see cref="KeyChord.Display"/> does.</summary>
    private static readonly (KeyModifiers Modifier, string Name)[] WindowsModifiers =
        [(KeyModifiers.Ctrl, "Ctrl"), (KeyModifiers.Alt, "Alt"), (KeyModifiers.Shift, "Shift")];

    private static readonly (KeyModifiers Modifier, string Name)[] MacModifiers =
        [(KeyModifiers.Alt, "Option"), (KeyModifiers.Shift, "Shift"), (KeyModifiers.Ctrl, "Cmd")];

    /// <summary>The keys of <paramref name="chord"/> as their keycaps read, modifiers first.</summary>
    public static IReadOnlyList<string> Keycaps(KeyChord chord, ShortcutStyle style = ShortcutStyle.Windows) =>
        [.. Modifiers(chord.Modifiers, style), Named.TryGetValue(chord.Key, out var named) ? named.Keycap : chord.Key];

    /// <summary><paramref name="chord"/> in words, such as "Ctrl+Alt+Right arrow".</summary>
    public static string Spoken(KeyChord chord, ShortcutStyle style = ShortcutStyle.Windows) =>
        string.Join("+", [.. Modifiers(chord.Modifiers, style), Named.TryGetValue(chord.Key, out var named) ? named.Spoken : chord.Key]);

    /// <summary>Every one of <paramref name="chords"/> in words, "Space or Play/pause key"; empty when there are none.</summary>
    public static string Spoken(IEnumerable<KeyChord> chords, ShortcutStyle style = ShortcutStyle.Windows)
    {
        ArgumentNullException.ThrowIfNull(chords);
        return string.Join(" or ", chords.Select(chord => Spoken(chord, style)));
    }

    private static IEnumerable<string> Modifiers(KeyModifiers modifiers, ShortcutStyle style) =>
        (style == ShortcutStyle.Mac ? MacModifiers : WindowsModifiers)
            .Where(entry => modifiers.HasFlag(entry.Modifier))
            .Select(entry => entry.Name);
}
