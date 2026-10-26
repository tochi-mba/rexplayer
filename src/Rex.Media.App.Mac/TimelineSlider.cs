namespace Rex.Media.App.Mac;

/// <summary>A native seek slider with buffered extent, chapter marks and hover callbacks.</summary>
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
