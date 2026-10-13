namespace Rex.Media.AppCore.Captions;

/// <summary>
/// A small picture beside a word that has one (OSD-11): the words a talker to camera leans on,
/// each with the symbol creators put beside it. Looked up by the word without its punctuation
/// and case, and by a few of its forms.
/// </summary>
public static class CaptionGlyphs
{
    private static readonly Dictionary<string, string> Glyphs = new(StringComparer.OrdinalIgnoreCase)
    {
        ["mute"] = "🔇",
        ["muted"] = "🔇",
        ["silent"] = "🔇",
        ["sound"] = "🔊",
        ["loud"] = "🔊",
        ["phone"] = "📱",
        ["phones"] = "📱",
        ["money"] = "💰",
        ["cash"] = "💰",
        ["dollars"] = "💰",
        ["paid"] = "💰",
        ["free"] = "🆓",
        ["time"] = "⏰",
        ["hours"] = "⏰",
        ["forever"] = "⏳",
        ["fire"] = "🔥",
        ["hot"] = "🔥",
        ["love"] = "❤️",
        ["heart"] = "❤️",
        ["camera"] = "📷",
        ["video"] = "🎬",
        ["videos"] = "🎬",
        ["idea"] = "💡",
        ["ideas"] = "💡",
        ["warning"] = "⚠️",
        ["problem"] = "⚠️",
        ["danger"] = "⚠️",
        ["music"] = "🎵",
        ["song"] = "🎵",
        ["brain"] = "🧠",
        ["think"] = "🧠",
        ["watch"] = "👀",
        ["look"] = "👀",
        ["eyes"] = "👀",
        ["rocket"] = "🚀",
        ["fast"] = "🚀",
        ["faster"] = "🚀",
        ["stop"] = "🛑",
        ["question"] = "❓",
        ["why"] = "❓",
        ["night"] = "🌙",
        ["sleep"] = "😴",
        ["bed"] = "😴",
        ["work"] = "💼",
        ["meeting"] = "💼",
        ["world"] = "🌍",
        ["win"] = "🏆",
        ["winner"] = "🏆",
        ["best"] = "🏆",
        ["link"] = "🔗",
        ["bio"] = "🔗",
    };

    /// <summary>The glyph for <paramref name="word"/> (punctuation and case aside), or null when it has none.</summary>
    public static string? For(string word)
    {
        ArgumentNullException.ThrowIfNull(word);
        var bare = word.Trim().Trim('.', ',', '!', '?', ';', ':', '"', '\'', '(', ')', '[', ']', '…', '“', '”', '‘', '’');
        return bare.Length > 0 && Glyphs.TryGetValue(bare, out var glyph) ? glyph : null;
    }
}
