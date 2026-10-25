using System.Windows.Automation;
using Rex.Media.TestKit;

namespace Rex.Media.App.Tests;

/// <summary>
/// Genuine offline screenshots for long text and deliberately absent embedded
/// artwork, captured at compact and wide fixed viewports.
/// </summary>
[Collection("desktop")]
public sealed class LibraryEvidenceEdgeTests
{
    [Fact]
    [Capability("LIB-05")]
    public void ALongOfflineTitleWithoutArtworkRendersAtCompactAndWideWidths()
    {
        const string Title = "A very long local song title that should remain browseable at compact width without truncating its accessible name";
        using var scene = new LibraryScene();
        scene.Song(Title, "An Album Without Embedded Cover Artwork", 2024, "Local");
        using var app = scene.Ready(settings => settings with { GenerateAudioArtwork = false }).Start();
        app.Visit("Songs");
        Wait.For(() => app.Lines().SequenceEqual([Title]), "the long offline title to appear");
        Assert.False(app.Line(Title).Current.IsOffscreen, "The row must remain visible to UI Automation.");

        var transform = (TransformPattern)app.Window.GetCurrentPattern(TransformPattern.Pattern);
        transform.Resize(720, 640);
        Wait.For(() => Math.Abs(app.Window.Current.BoundingRectangle.Width - 720) < 2 &&
            Math.Abs(app.Window.Current.BoundingRectangle.Height - 640) < 2,
            "the 720x640 long-title viewport");
        Screenshots.Match(app, "library-long-title-compact", "LibraryPane");

        transform.Resize(1000, 760);
        Wait.For(() => Math.Abs(app.Window.Current.BoundingRectangle.Width - 1000) < 2 &&
            Math.Abs(app.Window.Current.BoundingRectangle.Height - 760) < 2,
            "the 1000x760 missing-artwork viewport");
        Wait.For(() => app.Lines().SequenceEqual([Title]), "the same offline item to survive resize");
        Screenshots.Match(app, "library-missing-artwork-wide", "LibraryPane");
        Assert.Equal(0, app.Close());
    }
}
