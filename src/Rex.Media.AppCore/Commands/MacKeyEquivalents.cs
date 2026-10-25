namespace Rex.Media.AppCore.Commands;

/// <summary>A shortcut as an AppKit menu item takes it: one key character and which modifiers go with it.</summary>
public readonly record struct MacKeyEquivalent(string Key, bool Command, bool Option, bool Shift);

/// <summary>
/// rexplayer's shortcuts in the form a Mac menu item wants (its key equivalent), so the macOS menus
/// show and answer the same chords as Windows' (ADR-0020): the primary modifier is Command, Alt is
/// Option, and named keys become the characters AppKit uses for them (its function-key range for
/// arrows and F-keys). Letters are lowercase, because AppKit reads an uppercase letter as one with
/// Shift held.
/// </summary>
public static class MacKeyEquivalents
{
    /// <summary>AppKit's characters for the arrow keys and F1, from NSEvent's function-key range.</summary>
    private const int UpArrow = 0xF700;
    private const int DownArrow = 0xF701;
    private const int LeftArrow = 0xF702;
    private const int RightArrow = 0xF703;
    private const int F1 = 0xF704;
    private const int Home = 0xF729;
    private const int End = 0xF72B;
    private const int PageUp = 0xF72C;
    private const int PageDown = 0xF72D;
    private const int Insert = 0xF727;
    private const int ForwardDelete = 0xF728;

    private static readonly Dictionary<string, string> Named = new(StringComparer.Ordinal)
    {
        ["Space"] = " ",
        ["Enter"] = "\r",
        ["Escape"] = ((char)0x1B).ToString(),
        ["Tab"] = "\t",
        ["Backspace"] = ((char)0x08).ToString(),
        ["Delete"] = ((char)ForwardDelete).ToString(),
        ["Insert"] = ((char)Insert).ToString(),
        ["Home"] = ((char)Home).ToString(),
        ["End"] = ((char)End).ToString(),
        ["PageUp"] = ((char)PageUp).ToString(),
        ["PageDown"] = ((char)PageDown).ToString(),
        ["Up"] = ((char)UpArrow).ToString(),
        ["Down"] = ((char)DownArrow).ToString(),
        ["Left"] = ((char)LeftArrow).ToString(),
        ["Right"] = ((char)RightArrow).ToString(),
        ["Plus"] = "+",
        ["Minus"] = "-",
        ["Equals"] = "=",
        ["LeftBracket"] = "[",
        ["RightBracket"] = "]",
        ["Slash"] = "/",
        ["Comma"] = ",",
        ["Period"] = ".",
    };

    /// <summary>
    /// The key equivalent for <paramref name="chord"/>, or null for keys a menu item cannot carry
    /// (the media keys, which the Mac routes to the playing app itself).
    /// </summary>
    public static MacKeyEquivalent? For(KeyChord chord)
    {
        var key = chord.Key switch
        {
            { Length: 1 } single => single.ToLowerInvariant(),
            ['F', .. var number] when int.TryParse(number, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var n) => ((char)(F1 + n - 1)).ToString(),
            _ => Named.GetValueOrDefault(chord.Key),
        };
        return key is null ? null : new MacKeyEquivalent(
            key,
            chord.Modifiers.HasFlag(KeyModifiers.Ctrl),
            chord.Modifiers.HasFlag(KeyModifiers.Alt),
            chord.Modifiers.HasFlag(KeyModifiers.Shift));
    }
}
