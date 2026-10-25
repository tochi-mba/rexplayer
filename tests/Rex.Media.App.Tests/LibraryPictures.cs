namespace Rex.Media.App.Tests;

/// <summary>Reading the library's artwork off the screen: whether a frame shows a picture or its placeholder.</summary>
internal static class LibraryPictures
{
    /// <summary>A band of <paramref name="area"/>, each edge a share of its width or height.</summary>
    public static System.Windows.Rect Band(System.Windows.Rect area, double left, double right, double top, double bottom) =>
        new(area.Left + (area.Width * left), area.Top + (area.Height * top), area.Width * (right - left), area.Height * (bottom - top));

    /// <summary>
    /// Waits until most of <paramref name="area"/> (on the screen) is strongly coloured: the test
    /// pattern's picture, never a black bar or the grey placeholder.
    /// </summary>
    public static void WaitFor(AppProcess app, System.Windows.Rect area, string what)
    {
        var share = 0.0;
        Wait.For(() =>
        {
            var part = Screenshots.Crop(WindowPicture.Take(app.Handle), WindowPicture.Bounds(app.Handle), area);
            var coloured = 0;
            for (var i = 0; i + 3 < part.Bgra.Length; i += 4)
            {
                var (b, g, r) = (part.Bgra[i], part.Bgra[i + 1], part.Bgra[i + 2]);
                coloured += Math.Max(r, Math.Max(g, b)) - Math.Min(r, Math.Min(g, b)) > 60 ? 1 : 0;
            }

            share = part.Bgra.Length == 0 ? 0 : coloured / (part.Bgra.Length / 4.0);
            return share > 0.5;
        }, what, () => $"{share:P0} of the band was coloured.");
    }
}
