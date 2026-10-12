namespace Rex.Media.AppCore.Commands;

/// <summary>Editing and navigation inside a media list take precedence over playback shortcuts.</summary>
public static class ShortcutRouting
{
    /// <summary>
    /// Whether a focused library or playlist owns this chord, including extended selection.
    /// Space stays play and pause, so a video paused by opening the library resumes from the list.
    /// </summary>
    public static bool ListOwns(KeyChord chord) => !chord.Modifiers.HasFlag(KeyModifiers.Alt)
        && (chord.Key is "Enter" or "Left" or "Right" or "Up" or "Down"
            or "Home" or "End" or "PageUp" or "PageDown" or "Delete" or "Backspace" or "F2"
            || (chord.Key == "A" && chord.Modifiers.HasFlag(KeyModifiers.Ctrl)));
}
