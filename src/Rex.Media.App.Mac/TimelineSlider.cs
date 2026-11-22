using Rex.Media.AppCore.Design;

namespace Rex.Media.App.Mac;

/// <summary>
/// A slider drawn as the Windows shell draws its own: a thin rounded track in the hairline colour,
/// the part behind the knob filled with the accent, and a round knob. AppKit still tracks the mouse,
/// the keys and the accessibility value; only the drawing is the theme's.
/// </summary>
internal class RexSlider : NSSlider
{
    private const double KnobRadius = 8;
    private const double TrackHeight = 4;

    public RexSlider(CGRect frame) : base(frame)
    {
        TranslatesAutoresizingMaskIntoConstraints = false;
    }

    /// <summary>The track, inset by the knob's radius so the knob never leaves the view.</summary>
    protected CGRect TrackRect => new(
        Bounds.X + KnobRadius, Bounds.GetMidY() - TrackHeight / 2,
        Math.Max(0, (double)Bounds.Width - 2 * KnobRadius), TrackHeight);

    /// <summary>Where the value sits between the ends, from 0 to 1.</summary>
    protected double Fraction => MaxValue > MinValue ? Math.Clamp((DoubleValue - MinValue) / (MaxValue - MinValue), 0, 1) : 0;

    /// <summary>The knob's circle, centred on the track at the value.</summary>
    protected CGRect KnobRect
    {
        get
        {
            var track = TrackRect;
            var x = (double)track.X + (double)track.Width * Fraction;
            return new CGRect(x - KnobRadius, (double)track.GetMidY() - KnobRadius, 2 * KnobRadius, 2 * KnobRadius);
        }
    }

    /// <summary>The share of the track a pointer at <paramref name="x"/> (in the view's coordinates) points at.</summary>
    protected double FractionAt(nfloat x)
    {
        var track = TrackRect;
        return Math.Clamp(((double)x - (double)track.X) / Math.Max(1, (double)track.Width), 0, 1);
    }

    public override void DrawRect(CGRect dirtyRect)
    {
        var appearance = EffectiveAppearance;
        var track = TrackRect;
        NSGraphicsContext.GlobalSaveGraphicsState();
        NSGraphicsContext.CurrentContext?.CGContext.SetAlpha(Enabled ? 1 : (nfloat)0.4);
        FillRounded(track, MacTheme.Role(DesignRole.Hairline, appearance));
        DrawUnderProgress(track);
        FillRounded(new CGRect(track.X, track.Y, (double)track.Width * Fraction, track.Height), MacTheme.Accent);
        DrawOverProgress(track);
        var knob = KnobRect;
        MacTheme.Role(DesignRole.Surface, appearance).SetFill();
        NSBezierPath.FromOvalInRect(knob).Fill();
        var rim = NSBezierPath.FromOvalInRect(knob.Inset((nfloat)0.5, (nfloat)0.5));
        rim.LineWidth = 1;
        MacTheme.Role(DesignRole.Hairline, appearance).SetStroke();
        rim.Stroke();
        MacTheme.Accent.SetFill();
        NSBezierPath.FromOvalInRect(knob.Inset(4, 4)).Fill();
        NSGraphicsContext.GlobalRestoreGraphicsState();
    }

    /// <summary>Draws what lies on the track under the accent fill: a buffered extent.</summary>
    protected virtual void DrawUnderProgress(CGRect track)
    {
    }

    /// <summary>Draws what lies on top of the track, under the knob: chapter marks, a coverage rail.</summary>
    protected virtual void DrawOverProgress(CGRect track)
    {
    }

    /// <summary>Fills <paramref name="rect"/> with its ends rounded.</summary>
    protected static void FillRounded(CGRect rect, NSColor colour)
    {
        if (rect.Width <= 0)
        {
            return;
        }

        colour.SetFill();
        var radius = (nfloat)Math.Min((double)rect.Height / 2, (double)rect.Width / 2);
        NSBezierPath.FromRoundedRect(rect, radius, radius).Fill();
    }
}

/// <summary>The seek bar: a <see cref="RexSlider"/> with buffered extent, chapter marks, transcript coverage and hover callbacks.</summary>
internal sealed class TimelineSlider : RexSlider
{
    private readonly Action<double> _hover;
    private readonly Action _leave;
    private NSTrackingArea? _tracking;

    public TimelineSlider(CGRect frame, Action<double> hover, Action leave) : base(frame)
    {
        _hover = hover;
        _leave = leave;
    }

    public double Buffered { get; set; }

    public IReadOnlyList<double> Chapters { get; set; } = [];

    /// <summary>
    /// The stretches whose transcript is ready, as shares of the timeline exactly as the portable
    /// subtitle buffer reports them; drawn as a faint line under the track and never over the knob.
    /// </summary>
    public IReadOnlyList<(double From, double To)> Coverage { get; set; } = [];

    /// <summary>Where the pointer is along the track, from 0 to 1, for the preview above it.</summary>
    public double HoverFraction(NSEvent theEvent) => FractionAt(ConvertPointFromView(theEvent.LocationInWindow, null).X);

    /// <summary>The point along the track, in the view's coordinates, at <paramref name="fraction"/> of the timeline.</summary>
    public double XAt(double fraction) => (double)TrackRect.X + (double)TrackRect.Width * Math.Clamp(fraction, 0, 1);

    public override void UpdateTrackingAreas()
    {
        base.UpdateTrackingAreas();
        if (_tracking is not null) RemoveTrackingArea(_tracking);
        _tracking = new NSTrackingArea(Bounds,
            NSTrackingAreaOptions.MouseMoved | NSTrackingAreaOptions.MouseEnteredAndExited |
            NSTrackingAreaOptions.ActiveInKeyWindow | NSTrackingAreaOptions.InVisibleRect,
            this, null);
        AddTrackingArea(_tracking);
    }

    public override void MouseMoved(NSEvent theEvent)
    {
        _hover(HoverFraction(theEvent));
        base.MouseMoved(theEvent);
    }

    public override void MouseExited(NSEvent theEvent)
    {
        _leave();
        base.MouseExited(theEvent);
    }

    protected override void DrawUnderProgress(CGRect track)
    {
        var buffered = Math.Clamp(Buffered, 0, 1);
        FillRounded(new CGRect(track.X, track.Y, (double)track.Width * buffered, track.Height),
            MacTheme.Role(DesignRole.TextTertiary, EffectiveAppearance).ColorWithAlphaComponent((nfloat)0.5));
    }

    protected override void DrawOverProgress(CGRect track)
    {
        MacTheme.Role(DesignRole.TextSecondary, EffectiveAppearance).SetFill();
        foreach (var chapter in Chapters)
        {
            var x = (double)track.X + (double)track.Width * chapter;
            NSGraphics.RectFill(new CGRect(x - 1, (double)track.GetMidY() - 5, 2, 10));
        }

        DrawCoverage(track);
    }

    private void DrawCoverage(CGRect track)
    {
        if (Coverage.Count == 0) return;
        // Below the track, clipped around the knob so the knob always reads as on top.
        NSGraphicsContext.GlobalSaveGraphicsState();
        var clip = NSBezierPath.FromRect(Bounds);
        clip.AppendPath(NSBezierPath.FromOvalInRect(KnobRect));
        clip.WindingRule = NSWindingRule.EvenOdd;
        clip.AddClip();
        MacTheme.Tool.ColorWithAlphaComponent((nfloat)0.45).SetFill();
        var y = IsFlipped ? (double)track.GetMaxY() + 4 : (double)track.Y - 6;
        foreach (var (from, to) in Coverage)
        {
            var x = (double)track.X + (double)track.Width * from;
            NSBezierPath.FromRect(new CGRect(x, y, Math.Max(2, (double)track.Width * (to - from)), 2)).Fill();
        }

        NSGraphicsContext.GlobalRestoreGraphicsState();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && _tracking is not null)
        {
            RemoveTrackingArea(_tracking);
            _tracking.Dispose();
            _tracking = null;
        }
        base.Dispose(disposing);
    }
}
