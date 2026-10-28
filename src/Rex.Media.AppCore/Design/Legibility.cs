namespace Rex.Media.AppCore.Design;

/// <summary>
/// Words over a backdrop: which ink reads there, and how much of a backdrop to cover so that it
/// does. A library banner's words sit on a picture whose colours are unknown, on a colour taken
/// from a cover, or on the page itself; the window asks here instead of assuming its own theme,
/// which once drew dark words over a dark banner in a light window. Contrast is WCAG's: body,
/// meta and kicker text need <see cref="BodyContrast"/>, a large title <see cref="TitleContrast"/>.
/// The weakest words a banner draws are its secondary ones, so every answer here is worked out
/// for those (<see cref="LightWords"/> or <see cref="DarkWords"/>) and holds for brighter or
/// darker words too. Colours are 0xAARRGGBB and blend channel by channel, as the screen blends them.
/// </summary>
public static class Legibility
{
    /// <summary>The contrast body, meta and kicker text needs.</summary>
    public const double BodyContrast = 4.5;

    /// <summary>The contrast a large title needs.</summary>
    public const double TitleContrast = 3;

    /// <summary>The weakest light words: the secondary text that sits on dark ink.</summary>
    public static uint LightWords => DesignTokens.DarkText[1];

    /// <summary>The weakest dark words: the secondary text that sits on light ink.</summary>
    public static uint DarkWords => DesignTokens.LightText[1];

    /// <summary>
    /// <paramref name="over"/> laid on <paramref name="under"/> by its own alpha. What lies under
    /// counts as opaque, and so is the result.
    /// </summary>
    public static uint Over(uint over, uint under)
    {
        var alpha = (over >> 24) / 255.0;
        return 0xFF000000 | (Blend(over, under, alpha, 16) << 16) | (Blend(over, under, alpha, 8) << 8) | Blend(over, under, alpha, 0);
    }

    /// <summary>
    /// What a translucent <paramref name="scrim"/> can leave behind over a picture whose colours are
    /// not known: the scrim over the picture's blackest pixel and over its whitest.
    /// </summary>
    public static uint[] OverAnyPicture(uint scrim) => [Over(scrim, 0xFF000000), Over(scrim, 0xFFFFFFFF)];

    /// <summary>The least contrast <paramref name="text"/> has with any of <paramref name="backdrops"/>.</summary>
    public static double WorstContrast(uint text, IReadOnlyList<uint> backdrops)
    {
        if (backdrops.Count == 0)
        {
            throw new ArgumentException("Words sit on at least one colour.", nameof(backdrops));
        }

        var worst = double.MaxValue;
        foreach (var backdrop in backdrops)
        {
            worst = Math.Min(worst, DesignTokens.Contrast(text, backdrop));
        }

        return worst;
    }

    /// <summary>
    /// Whether light words read over every one of <paramref name="backdrops"/> at least as well as
    /// dark words do: the window then gives the words on them its dark theme.
    /// </summary>
    public static bool LightWordsRead(IReadOnlyList<uint> backdrops) =>
        WorstContrast(LightWords, backdrops) >= WorstContrast(DarkWords, backdrops);

    /// <summary>
    /// The least alpha a scrim of <paramref name="colour"/> needs, over any picture at all, for
    /// <paramref name="text"/> to reach <paramref name="contrast"/>; fully opaque when nothing less will do.
    /// </summary>
    public static byte ScrimAlpha(uint colour, uint text, double contrast)
    {
        byte alpha = 0;
        while (alpha < 0xFF && WorstContrast(text, OverAnyPicture(DesignTokens.WithAlpha(colour, alpha))) < contrast)
        {
            alpha++;
        }

        return alpha;
    }

    /// <summary>
    /// <paramref name="colour"/> moved toward <paramref name="toward"/> only as far as
    /// <paramref name="text"/> needs to reach <paramref name="contrast"/> on it, so a cover's colour
    /// keeps as much of itself as the words allow; all the way when even that is not enough.
    /// </summary>
    public static uint Toward(uint colour, uint toward, uint text, double contrast)
    {
        for (var step = 0; step < 0xFF; step++)
        {
            var moved = Over(DesignTokens.WithAlpha(toward, (byte)step), colour);
            if (DesignTokens.Contrast(text, moved) >= contrast)
            {
                return moved;
            }
        }

        return Over(DesignTokens.WithAlpha(toward, 0xFF), colour);
    }

    private static uint Blend(uint top, uint bottom, double alpha, int shift) =>
        (uint)Math.Round((((top >> shift) & 0xFF) * alpha) + (((bottom >> shift) & 0xFF) * (1 - alpha)));
}
