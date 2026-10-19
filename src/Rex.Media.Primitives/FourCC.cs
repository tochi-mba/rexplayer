using System.Text;

namespace Rex.Media.Primitives;

/// <summary>Four-character codes, the tags RIFF chunks, MP4 boxes and AVI streams are named by.</summary>
public static class FourCC
{
    /// <summary>The code as text; bytes outside printable ASCII are shown as '?'.</summary>
    public static string ToString(ReadOnlySpan<byte> code)
    {
        ArgumentOutOfRangeException.ThrowIfNotEqual(code.Length, 4, nameof(code));
        var builder = new StringBuilder(4);
        foreach (var b in code)
        {
            builder.Append(b is >= 0x20 and < 0x7F ? (char)b : '?');
        }

        return builder.ToString();
    }

    public static bool Matches(ReadOnlySpan<byte> data, string code)
    {
        ArgumentNullException.ThrowIfNull(code);
        if (data.Length < 4 || code.Length != 4)
        {
            return false;
        }

        for (var i = 0; i < 4; i++)
        {
            if (data[i] != code[i])
            {
                return false;
            }
        }

        return true;
    }
}
