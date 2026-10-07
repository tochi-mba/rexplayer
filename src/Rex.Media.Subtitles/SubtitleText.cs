// Spec: The Unicode Standard section 23.8 (byte order marks for UTF-8, UTF-16LE and UTF-16BE) and RFC 3629 section 4 (the syntax of a valid UTF-8 byte sequence), used to tell how a subtitle file's text is encoded.
using System.Text;

namespace Rex.Media.Subtitles;

/// <summary>
/// Turns a subtitle file's bytes into text (SUB-15). A byte-order mark decides first; then text
/// that is valid UTF-8 is UTF-8; then, as for most older subtitle files, the fallback code page
/// the user chose (Windows-1252, Western European, unless they chose another).
/// </summary>
public static class SubtitleText
{
    static SubtitleText() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    /// <summary>The code pages offered as the fallback, by name.</summary>
    public static IReadOnlyList<(int CodePage, string Name)> Fallbacks { get; } =
    [
        (1252, "Western European (Windows-1252)"),
        (1250, "Central European (Windows-1250)"),
        (1251, "Cyrillic (Windows-1251)"),
        (1253, "Greek (Windows-1253)"),
        (1254, "Turkish (Windows-1254)"),
        (1255, "Hebrew (Windows-1255)"),
        (1256, "Arabic (Windows-1256)"),
        (1257, "Baltic (Windows-1257)"),
        (874, "Thai (Windows-874)"),
        (932, "Japanese (Shift-JIS)"),
        (936, "Simplified Chinese (GBK)"),
        (949, "Korean (EUC-KR)"),
        (950, "Traditional Chinese (Big5)"),
        (20866, "Cyrillic (KOI8-R)"),
        (28592, "Central European (ISO-8859-2)"),
    ];

    /// <summary>The text in <paramref name="bytes"/>, and which encoding was used.</summary>
    public static (string Text, Encoding Encoding) Decode(ReadOnlySpan<byte> bytes, int fallbackCodePage = 1252)
    {
        if (bytes.StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]))
        {
            return (Encoding.UTF8.GetString(bytes[3..]), Encoding.UTF8);
        }

        if (bytes.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xFE]))
        {
            return (Encoding.Unicode.GetString(bytes[2..]), Encoding.Unicode);
        }

        if (bytes.StartsWith((ReadOnlySpan<byte>)[0xFE, 0xFF]))
        {
            return (Encoding.BigEndianUnicode.GetString(bytes[2..]), Encoding.BigEndianUnicode);
        }

        if (IsUtf8(bytes))
        {
            return (Encoding.UTF8.GetString(bytes), Encoding.UTF8);
        }

        var fallback = Fallbacks.Any(entry => entry.CodePage == fallbackCodePage) ? Encoding.GetEncoding(fallbackCodePage) : Encoding.GetEncoding(1252);
        return (fallback.GetString(bytes), fallback);
    }

    /// <summary>Whether every byte sequence is well-formed UTF-8 (plain ASCII included).</summary>
    internal static bool IsUtf8(ReadOnlySpan<byte> bytes)
    {
        var strict = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
        try
        {
            strict.GetCharCount(bytes);
            return true;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }
}
