namespace Rex.Media.AppCore.Commands;

/// <summary>The modifier keys of a chord.</summary>
[Flags]
public enum KeyModifiers
{
    None = 0,
    Ctrl = 1,
    Alt = 2,
    Shift = 4,
}

/// <summary>
/// A key with its modifiers, written the way people write shortcuts: "Ctrl+Shift+O", "Space",
/// "Alt+1". Key names are rexplayer's own small vocabulary, independent of any UI framework; the app
/// translates its key events into them. Text is case-insensitive when parsed and canonical
/// (Ctrl, Alt, Shift, then the key) when written.
/// </summary>
public readonly record struct KeyChord(KeyModifiers Modifiers, string Key)
{
    private static readonly HashSet<string> Named = new(StringComparer.OrdinalIgnoreCase)
    {
        "Space", "Enter", "Escape", "Tab", "Backspace", "Delete", "Insert", "Home", "End", "PageUp", "PageDown",
        "Left", "Right", "Up", "Down", "Plus", "Minus", "Equals", "LeftBracket", "RightBracket", "Slash", "Comma", "Period",
        "MediaPlayPause", "MediaStop", "MediaNext", "MediaPrevious",
    };

    /// <summary>Every key name a chord may use: letters, digits, F1 to F24 and <see cref="Named"/>.</summary>
    public static bool IsKey(string name) =>
        (name.Length == 1 && char.IsAsciiLetterOrDigit(name[0]))
        || (name.Length is 2 or 3 && (name[0] is 'F' or 'f') && int.TryParse(name.AsSpan(1), out var n) && n is >= 1 and <= 24 && name[1] != '0')
        || Named.Contains(name);

    /// <summary>The chord in <paramref name="text"/>, or null when it is not one.</summary>
    public static KeyChord? TryParse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var parts = text.Split('+', StringSplitOptions.TrimEntries);
        var modifiers = KeyModifiers.None;
        foreach (var part in parts[..^1])
        {
            var modifier = part.ToUpperInvariant() switch
            {
                "CTRL" or "CONTROL" => KeyModifiers.Ctrl,
                "ALT" => KeyModifiers.Alt,
                "SHIFT" => KeyModifiers.Shift,
                _ => KeyModifiers.None,
            };
            if (modifier == KeyModifiers.None || modifiers.HasFlag(modifier))
            {
                return null;
            }

            modifiers |= modifier;
        }

        var key = parts[^1];
        return IsKey(key) ? new KeyChord(modifiers, Canonical(key)) : null;
    }

    public static KeyChord Parse(string text) => TryParse(text) ?? throw new FormatException($"'{text}' is not a shortcut.");

    public override string ToString()
    {
        var text = Modifiers.HasFlag(KeyModifiers.Ctrl) ? "Ctrl+" : "";
        text += Modifiers.HasFlag(KeyModifiers.Alt) ? "Alt+" : "";
        text += Modifiers.HasFlag(KeyModifiers.Shift) ? "Shift+" : "";
        return text + Key;
    }

    private static string Canonical(string key)
    {
        if (key.Length == 1)
        {
            return key.ToUpperInvariant();
        }

        if (Named.TryGetValue(key, out var named))
        {
            return named;
        }

        return key.ToUpperInvariant();
    }
}
