// Spec: The Unicode Standard section 23.8 (byte order marks) and RFC 3629 section 4 (valid UTF-8), used to read a playlist file's text.
using System.Text;

namespace Rex.Media.Library.Playlists;

/// <summary>
/// A playlist file's text (PLF-01): by its byte-order mark; else as UTF-8 when it is valid UTF-8,
/// as .m3u8 files and most new files are; else as Windows-1252, as older .m3u files were written.
/// </summary>
public static class PlaylistText
{
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    static PlaylistText() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    public static string Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]))
        {
            return Encoding.UTF8.GetString(bytes[3..]);
        }

        if (bytes.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xFE]))
        {
            return Encoding.Unicode.GetString(bytes[2..]);
        }

        if (bytes.StartsWith((ReadOnlySpan<byte>)[0xFE, 0xFF]))
        {
            return Encoding.BigEndianUnicode.GetString(bytes[2..]);
        }

        try
        {
            return StrictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return Encoding.GetEncoding(1252).GetString(bytes);
        }
    }

    /// <summary>The lines of <paramref name="text"/>, whatever ends them.</summary>
    internal static string[] Lines(string text) => text.Split(["\r\n", "\n", "\r"], StringSplitOptions.None);
}
