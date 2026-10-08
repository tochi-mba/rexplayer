namespace Rex.Media.Video;

/// <summary>
/// How far into the picture the view is zoomed, and where (VID-07): <see cref="Zoom"/> times larger,
/// centred on (<see cref="CenterX"/>, <see cref="CenterY"/>), each from 0 to 1 across the picture as
/// it is shown (after any crop). The view never leaves the picture: the centre keeps half a view
/// from every edge.
/// </summary>
public readonly record struct PictureView
{
    /// <summary>The closest the view zooms in.</summary>
    public const double MaxZoom = 16;

    /// <summary>The steps the zoom keys go through.</summary>
    public static IReadOnlyList<double> Steps { get; } = [1, 1.5, 2, 3, 4, 6, 8, 12, 16];

    public PictureView(double zoom, double centerX, double centerY)
    {
        Zoom = double.IsFinite(zoom) ? Math.Clamp(zoom, 1, MaxZoom) : 1;
        var half = 0.5 / Zoom;
        CenterX = Math.Clamp(double.IsFinite(centerX) ? centerX : 0.5, half, 1 - half);
        CenterY = Math.Clamp(double.IsFinite(centerY) ? centerY : 0.5, half, 1 - half);
    }

    /// <summary>All of the picture.</summary>
    public static PictureView Whole { get; } = new(1, 0.5, 0.5);

    public double Zoom { get; }

    public double CenterX { get; }

    public double CenterY { get; }

    /// <summary>Whether this is zoomed in at all.</summary>
    public bool IsZoomed => Zoom > 1;

    /// <summary>The part of the picture in view, from 0 to 1 of it: left, top, width and height.</summary>
    public (double Left, double Top, double Width, double Height) Visible =>
        (CenterX - (0.5 / Zoom), CenterY - (0.5 / Zoom), 1 / Zoom, 1 / Zoom);

    /// <summary>
    /// The view <paramref name="factor"/> times as zoomed, keeping the point at (<paramref name="x"/>,
    /// <paramref name="y"/>) of the view (from 0 to 1 across what is shown) where it is on screen.
    /// </summary>
    public PictureView ZoomAt(double factor, double x, double y)
    {
        var (left, top, width, height) = Visible;
        var pointX = left + (Math.Clamp(x, 0, 1) * width);
        var pointY = top + (Math.Clamp(y, 0, 1) * height);
        var zoom = Math.Clamp(Zoom * factor, 1, MaxZoom);
        var span = 1 / zoom;
        return new PictureView(zoom, pointX - (x * span) + (span / 2), pointY - (y * span) + (span / 2));
    }

    /// <summary>The next zoom step in (<paramref name="steps"/> &gt; 0) or out, about the centre of the view.</summary>
    public PictureView Step(int steps)
    {
        var current = Zoom;
        var zoom = steps > 0 ? Steps.FirstOrDefault(step => step > current + 1e-9, MaxZoom) : steps < 0 ? Steps.LastOrDefault(step => step < current - 1e-9, 1) : current;
        return new PictureView(zoom, CenterX, CenterY);
    }

    /// <summary>The view moved by (<paramref name="dx"/>, <paramref name="dy"/>), each a share of what is shown, as when the picture is dragged.</summary>
    public PictureView PanBy(double dx, double dy) => new(Zoom, CenterX - (dx / Zoom), CenterY - (dy / Zoom));

    /// <summary>The view, as zoomed, centred on (<paramref name="x"/>, <paramref name="y"/>) of the whole picture: a click in the navigator.</summary>
    public PictureView CenteredOn(double x, double y) => new(Zoom, x, y);

    /// <summary>The part of <paramref name="picture"/> (a crop of the decoded picture) this view shows.</summary>
    public SourceRect Within(SourceRect picture)
    {
        var (left, top, width, height) = Visible;
        var pictureWidth = picture.Right - picture.Left;
        var pictureHeight = picture.Bottom - picture.Top;
        return new SourceRect(
            (float)(picture.Left + (left * pictureWidth)),
            (float)(picture.Top + (top * pictureHeight)),
            (float)(picture.Left + ((left + width) * pictureWidth)),
            (float)(picture.Top + ((top + height) * pictureHeight)));
    }

    /// <summary>
    /// Where the navigator (the whole picture, small, in the top left corner) goes on a target
    /// <paramref name="width"/> by <paramref name="height"/>, for a picture shaped <paramref name="across"/> : <paramref name="down"/>:
    /// a fifth of the width or a quarter of the height, whichever is smaller, inset from the corner.
    /// </summary>
    public static (double X, double Y, double Width, double Height) Navigator(double width, double height, long across, long down)
    {
        var shape = across > 0 && down > 0 ? (double)across / down : 16.0 / 9;
        var navigatorWidth = Math.Min(width * 0.2, height * 0.25 * shape);
        var navigatorHeight = navigatorWidth / shape;
        var margin = Math.Min(width, height) * 0.02;
        return (margin, margin, navigatorWidth, navigatorHeight);
    }
}
