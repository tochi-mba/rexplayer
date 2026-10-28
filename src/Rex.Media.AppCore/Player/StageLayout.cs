namespace Rex.Media.AppCore.Player;

/// <summary>What the stage measured, for <see cref="StageLayout.Resolve"/>: its size and what it holds.</summary>
/// <param name="Width">The stage's width.</param>
/// <param name="Height">The height of the stage left showing above the transport.</param>
/// <param name="HasCover">A decoded cover is ready to show.</param>
/// <param name="LyricsShown">Lyric lines are on the stage.</param>
/// <param name="VisualizerWanted">A visualisation is chosen for what plays.</param>
/// <param name="FullBleed">The visualisation is drawn in software, across the stage's whole width.</param>
/// <param name="TitleHeight">The title block's measured height, the cover included when it stands above the words.</param>
/// <param name="FooterHeight">The measured lyrics status and lyrics actions; 0 when neither shows.</param>
public readonly record struct StageLayoutInput(
    double Width,
    double Height,
    bool HasCover = false,
    bool LyricsShown = false,
    bool VisualizerWanted = false,
    bool FullBleed = false,
    double TitleHeight = 0,
    double FooterHeight = 0);

/// <summary>Where the visualisation draws: its margins inside the stage.</summary>
public readonly record struct StageBand(double Left, double Top, double Right, double Bottom);

/// <summary>How the stage arranges its title block, cover, lyrics and visualisation.</summary>
/// <param name="Compact">A small stage: the cover stands above the words, and every inset is smaller.</param>
/// <param name="Tiny">The smallest stage: no cover, no lyrics status or actions, and the smallest title.</param>
/// <param name="Inset">The margin around the title block.</param>
/// <param name="BottomInset">The title block's bottom margin: below a centred block it leaves the lyrics status and actions their room.</param>
/// <param name="TitleSize">The title's font size.</param>
/// <param name="TextMaxWidth">The widest the title block's words may be.</param>
/// <param name="CoverShown">Whether the cover shows.</param>
/// <param name="CoverSize">The cover's side.</param>
/// <param name="ColumnSpacing">The gap between a cover and the words beside it; none without a cover.</param>
/// <param name="RowSpacing">The gap between a cover and the words under it; none without a cover.</param>
/// <param name="TextBesideCover">The words stand beside the cover, aligned to it, rather than centred.</param>
/// <param name="TitleAtTop">The title block keeps to the top, leaving the rest to the visualisation and the lyrics.</param>
/// <param name="FooterShown">Whether the lyrics status and lyrics actions show.</param>
/// <param name="DockSide">The lyrics dock's left and right margins.</param>
/// <param name="DockBottom">The lyrics dock's bottom margin.</param>
/// <param name="LyricsHeight">The height of the lyric lines' scrolling box.</param>
/// <param name="VisualizerShown">Whether the visualisation shows: a stage too small for it leaves it out.</param>
/// <param name="Band">Where the visualisation draws, when it shows.</param>
public readonly record struct StageLayoutView(
    bool Compact,
    bool Tiny,
    double Inset,
    double BottomInset,
    double TitleSize,
    double TextMaxWidth,
    bool CoverShown,
    double CoverSize,
    double ColumnSpacing,
    double RowSpacing,
    bool TextBesideCover,
    bool TitleAtTop,
    bool FooterShown,
    double DockSide,
    double DockBottom,
    double LyricsHeight,
    bool VisualizerShown,
    StageBand Band);

/// <summary>
/// One vertical order for the stage while sound plays or nothing is open: the title block, the
/// visualisation in its own band under it, then the lyrics dock (lines, status, actions) at the
/// bottom. Nothing draws over the title, and a stage too small for a part leaves that part out
/// rather than piling the parts on one another.
/// </summary>
public static class StageLayout
{
    /// <summary>Narrower than this, or shorter than <see cref="CompactHeight"/>, the stage is compact.</summary>
    public const double CompactWidth = 760;

    /// <summary>Shorter than this, the stage is compact.</summary>
    public const double CompactHeight = 450;

    /// <summary>Narrower than this, or shorter than <see cref="TinyHeight"/>, the stage is tiny.</summary>
    public const double TinyWidth = 480;

    /// <summary>Shorter than this, the stage is tiny.</summary>
    public const double TinyHeight = 280;

    /// <summary>The largest cover.</summary>
    public const double LargestCover = 240;

    /// <summary>A cover smaller than this is left out.</summary>
    public const double SmallestCover = 80;

    /// <summary>The gap between a cover and the words beside it.</summary>
    public const double CoverGap = 32;

    /// <summary>The gap between a cover and the words under it.</summary>
    public const double StackGap = 10;

    /// <summary>The gap between the title block, the visualisation and the lyrics.</summary>
    public const double Gap = 16;

    /// <summary>The shortest band a visualisation is drawn in.</summary>
    public const double ShortestBand = 48;

    /// <summary>A stage shorter than this has no room for a visualisation as well as the title.</summary>
    public const double ShortestVisualizerStage = 200;

    /// <summary>A stage narrower than this has no room for a visualisation.</summary>
    public const double NarrowestVisualizerStage = 320;

    /// <summary>The tallest the lyric lines' box grows.</summary>
    public const double TallestLyrics = 360;

    /// <summary>The lyric lines' box keeps room for about two lines even when the stage is short.</summary>
    public const double ShortestLyrics = 96;

    public static StageLayoutView Resolve(in StageLayoutInput input)
    {
        var width = Math.Max(0, input.Width);
        var height = Math.Max(0, input.Height);
        var compact = width < CompactWidth || height < CompactHeight;
        var tiny = width < TinyWidth || height < TinyHeight;
        double inset = tiny ? 12 : compact ? 20 : 32;
        double dockSide = compact ? 12 : 48;
        double dockBottom = compact ? 12 : 24;

        // The tiniest stage keeps only the title: a status line or buttons would sit on it.
        var footerShown = !tiny || input.LyricsShown;
        var footer = footerShown && input.FooterHeight > 0 ? input.FooterHeight + dockBottom : 0;
        var visualizer = input.VisualizerWanted && height >= ShortestVisualizerStage && width >= NarrowestVisualizerStage;
        var atTop = visualizer || input.LyricsShown;

        // Beside the lyrics a cover takes only the height their shortest box does not need.
        var room = compact ? height - 180 : height - 48;
        if (input.LyricsShown && !compact)
        {
            room = Math.Min(room, height - inset - Gap - ShortestLyrics - footer);
        }

        var cover = Math.Max(0, Math.Min(LargestCover, Math.Min(width - 48, room)));
        var coverShown = input.HasCover && !tiny && !(compact && input.LyricsShown) && cover >= SmallestCover;

        var titleBottom = inset + Math.Max(0, input.TitleHeight);
        var wantedLyrics = Math.Min(TallestLyrics, height * (tiny ? 0.35 : 0.45));
        var roomForLyrics = height - titleBottom - Gap - footer;
        var lyrics = Math.Max(0, Math.Min(wantedLyrics, Math.Max(ShortestLyrics, roomForLyrics)));

        return new StageLayoutView(
            compact,
            tiny,
            inset,
            atTop ? inset : inset + footer,
            tiny ? 24 : compact ? 32 : 44,
            compact ? Math.Max(160, width - 40) : 560,
            coverShown,
            cover,
            coverShown && !compact ? CoverGap : 0,
            coverShown && compact ? StackGap : 0,
            coverShown && !compact,
            atTop,
            footerShown,
            dockSide,
            dockBottom,
            lyrics,
            visualizer,
            visualizer ? Band(height, titleBottom, footer, input.FullBleed, tiny, compact) : default);
    }

    /// <summary>
    /// The visualisation's band: under the title block and above the lyrics status and actions. A
    /// software stage spans the whole width; the shapes keep a margin. A band squeezed shorter than
    /// <see cref="ShortestBand"/> reaches down behind the status first, then up behind the title's
    /// last lines, never shorter.
    /// </summary>
    private static StageBand Band(double height, double titleBottom, double footer, bool fullBleed, bool tiny, bool compact)
    {
        double side = fullBleed ? 0 : tiny ? 8 : compact ? 20 : 48;
        double resting = fullBleed ? 0 : tiny ? 8 : compact ? 12 : 48;
        var top = titleBottom + Gap;
        var bottom = footer > 0 ? footer + Gap : resting;
        if (height - top - bottom < ShortestBand)
        {
            bottom = Math.Max(0, height - top - ShortestBand);
            top = Math.Min(top, Math.Max(0, height - bottom - ShortestBand));
        }

        return new StageBand(side, top, side, bottom);
    }
}
