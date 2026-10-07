using Rex.Media.Settings;
using Rex.Media.Subtitles;

namespace Rex.Media.AppCore.Player;

/// <summary>A colour with its opacity, as the window draws it.</summary>
public readonly record struct Argb(byte A, byte R, byte G, byte B)
{
    /// <summary>The colour 0xRRGGBB at <paramref name="opacityPercent"/> percent opacity.</summary>
    public static Argb From(int rgb, int opacityPercent) => new(
        (byte)Math.Round(255 * Math.Clamp(opacityPercent, 0, 100) / 100.0),
        (byte)(rgb >> 16),
        (byte)(rgb >> 8),
        (byte)rgb);
}

/// <summary>A rectangle on the window, in its own units.</summary>
public readonly record struct Area(double X, double Y, double Width, double Height);

/// <summary>
/// How subtitles look and where they sit (OSD-01 to OSD-05), worked out from the settings and the
/// size of the picture, so the window only has to draw what this says.
/// </summary>
public sealed record SubtitleLook
{
    /// <summary>The sizes the size keys and the menu step through, in percent.</summary>
    public static IReadOnlyList<int> Sizes { get; } = [50, 75, 100, 125, 150, 200, 300, 400];

    /// <summary>The height of a line at 100 %, as a share of the picture's height.</summary>
    private const double NormalShare = 0.055;

    public required string Font { get; init; }

    public required double FontSize { get; init; }

    public required bool Bold { get; init; }

    public required Argb Text { get; init; }

    /// <summary>How far the outline reaches from each letter, or 0 for none.</summary>
    public required double Outline { get; init; }

    public required Argb OutlineColor { get; init; }

    /// <summary>How far the shadow falls below and right, or 0 for none.</summary>
    public required double ShadowOffset { get; init; }

    public required Argb Shadow { get; init; }

    /// <summary>The box behind the text; fully clear for none.</summary>
    public required Argb Box { get; init; }

    /// <summary>The gap between the edge of the subtitle area and the nearest line.</summary>
    public required double Margin { get; init; }

    /// <summary>Every cue at the bottom, wherever its file placed it.</summary>
    public required bool AllAtBottom { get; init; }

    /// <summary>Whether the colours and weights a subtitle file asks for are used.</summary>
    public required bool RespectStyles { get; init; }

    /// <summary>The look for a picture <paramref name="pictureHeight"/> tall.</summary>
    public static SubtitleLook For(PlayerSettings settings, double pictureHeight)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings = settings.Normalize();
        var height = double.IsFinite(pictureHeight) ? Math.Max(pictureHeight, 0) : 0;
        var fontSize = Math.Max(10, height * NormalShare * settings.SubtitleSize / 100);
        var outline = settings.SubtitleOutline switch
        {
            OutlineChoice.Thin => Math.Max(1, fontSize * 0.03),
            OutlineChoice.Normal => Math.Max(1, fontSize * 0.06),
            OutlineChoice.Thick => Math.Max(1, fontSize * 0.1),
            _ => 0,
        };
        return new SubtitleLook
        {
            Font = settings.SubtitleFont,
            FontSize = fontSize,
            Bold = settings.SubtitleBold,
            Text = Argb.From(settings.SubtitleColor, settings.SubtitleOpacity),
            Outline = outline,
            OutlineColor = Argb.From(settings.SubtitleOutlineColor, settings.SubtitleOpacity),
            ShadowOffset = settings.SubtitleShadowOpacity > 0 ? settings.SubtitleShadowOffset : 0,
            Shadow = Argb.From(settings.SubtitleShadowColor, settings.SubtitleShadowOpacity),
            Box = Argb.From(settings.SubtitleBoxColor, settings.SubtitleBoxOpacity),
            Margin = height * settings.SubtitleMargin / 100,
            AllAtBottom = settings.SubtitlesAtBottom,
            RespectStyles = settings.SubtitleStyles == SubtitleStyleChoice.Respect,
        };
    }

    /// <summary>The next size up (<paramref name="steps"/> &gt; 0) or down from <paramref name="current"/>.</summary>
    public static int StepSize(int current, int steps)
    {
        if (steps > 0)
        {
            return Sizes.FirstOrDefault(size => size > current, Sizes[^1]);
        }

        return steps < 0 ? Sizes.LastOrDefault(size => size < current, Sizes[0]) : current;
    }

    /// <summary>
    /// Where a picture shaped <paramref name="across"/> : <paramref name="down"/> sits when fitted
    /// into a <paramref name="width"/> by <paramref name="height"/> stage, with bars filling the rest.
    /// </summary>
    public static Area Picture(double width, double height, long across, long down)
    {
        if (across <= 0 || down <= 0 || width <= 0 || height <= 0)
        {
            return new Area(0, 0, Math.Max(width, 0), Math.Max(height, 0));
        }

        var shape = (double)across / down;
        return width / height > shape
            ? new Area((width - (height * shape)) / 2, 0, height * shape, height)
            : new Area(0, (height - (width / shape)) / 2, width, width / shape);
    }

    /// <summary>
    /// Where subtitles go on a stage <paramref name="stageHeight"/> tall showing <paramref name="picture"/>:
    /// over the picture, or down into the bars below and above it when <paramref name="inBars"/>.
    /// </summary>
    public static Area SubtitleArea(Area picture, double stageHeight, bool inBars) =>
        inBars ? picture with { Y = 0, Height = Math.Max(stageHeight, picture.Height) } : picture;

    /// <summary>Where <paramref name="cue"/> goes, after the user's choice to put everything at the bottom.</summary>
    public SubtitlePlacement PlacementOf(SubtitleCue cue)
    {
        ArgumentNullException.ThrowIfNull(cue);
        return AllAtBottom ? SubtitlePlacement.Bottom : cue.Placement;
    }

    /// <summary>The colour <paramref name="run"/> is drawn in.</summary>
    public Argb ColorOf(SubtitleRun run)
    {
        ArgumentNullException.ThrowIfNull(run);
        return RespectStyles && run.Color is { } color ? Argb.From(color, 100) with { A = Text.A } : Text;
    }

    /// <summary>Whether <paramref name="run"/> is drawn bold.</summary>
    public bool IsBold(SubtitleRun run)
    {
        ArgumentNullException.ThrowIfNull(run);
        return Bold || (RespectStyles && run.Bold);
    }
}
