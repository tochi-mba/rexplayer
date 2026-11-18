namespace Rex.Media.App.Mac;

/// <summary>A native seek slider with buffered extent, chapter marks, transcript coverage and hover callbacks.</summary>
internal sealed class TimelineSlider : NSSlider
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
        var point = ConvertPointFromView(theEvent.LocationInWindow, null);
        _hover(Math.Clamp((double)point.X / Math.Max(1, (double)Bounds.Width), 0, 1));
        base.MouseMoved(theEvent);
    }

    public override void MouseExited(NSEvent theEvent)
    {
        _leave();
        base.MouseExited(theEvent);
    }

    public override void DrawRect(CGRect dirtyRect)
    {
        var track = new CGRect(Bounds.X + 3, Bounds.GetMidY() - 2, (Bounds.Width - 6) * Buffered, 4);
        NSColor.TertiaryLabel.SetFill();
        NSGraphics.RectFill(track);
        base.DrawRect(dirtyRect);
        NSColor.ControlAccent.SetFill();
        foreach (var chapter in Chapters)
        {
            var x = Bounds.X + 3 + (Bounds.Width - 6) * chapter;
            NSGraphics.RectFill(new CGRect(x - 1, Bounds.GetMidY() - 6, 2, 12));
        }

        DrawCoverage();
    }

    private void DrawCoverage()
    {
        if (Coverage.Count == 0) return;
        var width = Bounds.Width - 6;
        // Below the track, clipped around the knob so the knob always reads as on top.
        var knob = Cell is NSSliderCell cell ? cell.KnobRectFlipped(IsFlipped) : CGRect.Empty;
        NSGraphicsContext.GlobalSaveGraphicsState();
        var clip = NSBezierPath.FromRect(Bounds);
        if (!knob.IsEmpty) clip.AppendPath(NSBezierPath.FromOvalInRect(knob));
        clip.WindingRule = NSWindingRule.EvenOdd;
        clip.AddClip();
        NSColor.ControlAccent.ColorWithAlphaComponent((System.Runtime.InteropServices.NFloat)0.45).SetFill();
        var y = IsFlipped ? Bounds.GetMidY() + 4 : Bounds.GetMidY() - 6;
        foreach (var (from, to) in Coverage)
        {
            var x = Bounds.X + 3 + width * from;
            NSBezierPath.FromRect(new CGRect(x, y, Math.Max(2, width * (to - from)), 2)).Fill();
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
