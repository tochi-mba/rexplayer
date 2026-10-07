namespace Rex.Media.AppCore.Commands;

/// <summary>
/// Windows virtual-key codes (the numbers every Windows UI framework reports) as the key names of
/// <see cref="KeyChord"/>. The punctuation keys are named for what they carry on a US layout, the
/// layout Windows numbers them by; keys rexplayer has no name for give null and run nothing.
/// </summary>
public static class VirtualKeys
{
    private static readonly Dictionary<int, string> Named = new()
    {
        [0x08] = "Backspace",
        [0x09] = "Tab",
        [0x0D] = "Enter",
        [0x1B] = "Escape",
        [0x20] = "Space",
        [0x21] = "PageUp",
        [0x22] = "PageDown",
        [0x23] = "End",
        [0x24] = "Home",
        [0x25] = "Left",
        [0x26] = "Up",
        [0x27] = "Right",
        [0x28] = "Down",
        [0x2D] = "Insert",
        [0x2E] = "Delete",
        [0x6B] = "Plus",
        [0x6D] = "Minus",
        [0xB0] = "MediaNext",
        [0xB1] = "MediaPrevious",
        [0xB2] = "MediaStop",
        [0xB3] = "MediaPlayPause",
        [0xBB] = "Equals",
        [0xBC] = "Comma",
        [0xBD] = "Minus",
        [0xBE] = "Period",
        [0xBF] = "Slash",
        [0xDB] = "LeftBracket",
        [0xDD] = "RightBracket",
    };

    /// <summary>The key name for <paramref name="virtualKey"/>, or null when it has none.</summary>
    public static string? Name(int virtualKey) => virtualKey switch
    {
        >= 0x30 and <= 0x39 => ((char)virtualKey).ToString(),
        >= 0x41 and <= 0x5A => ((char)virtualKey).ToString(),
        >= 0x60 and <= 0x69 => ((char)('0' + virtualKey - 0x60)).ToString(),
        >= 0x70 and <= 0x87 => "F" + (virtualKey - 0x6F),
        _ => Named.GetValueOrDefault(virtualKey),
    };

    /// <summary>The chord for a key and its modifiers, or null when the key has no name.</summary>
    public static KeyChord? Chord(int virtualKey, bool ctrl, bool alt, bool shift) =>
        Name(virtualKey) is { } name
            ? new KeyChord((ctrl ? KeyModifiers.Ctrl : 0) | (alt ? KeyModifiers.Alt : 0) | (shift ? KeyModifiers.Shift : 0), name)
            : null;
}
