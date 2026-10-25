namespace Rex.Media.AppCore.Library;

/// <summary>
/// Detect the direction of user-owned media titles from their first strongly
/// directional letter. Numeric track numbers, punctuation and emoji do not
/// force an Arabic/Hebrew title into a left-to-right line.
/// </summary>
public static class TextDirection
{
    public static bool IsRightToLeft(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        foreach (var character in value)
        {
            // Hebrew, Arabic, Syriac, Thaana, N'Ko, and Arabic
            // presentation forms: their titles should start at the right.
            if (character is >= '\u0590' and <= '\u08FF'
                or >= '\uFB1D' and <= '\uFDFF'
                or >= '\uFE70' and <= '\uFEFC')
            {
                return true;
            }

            if (char.IsLetter(character))
            {
                return false;
            }
        }

        return false;
    }
}
