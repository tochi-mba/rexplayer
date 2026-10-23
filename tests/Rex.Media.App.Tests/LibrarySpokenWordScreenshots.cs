using System.IO;
using System.Windows.Automation;
using Rex.Media.TestKit;

namespace Rex.Media.App.Tests;

/// <summary>
/// Fixture-backed actual Windows captures for PR #484. Missing baselines deliberately only write
/// CI artifacts; a reviewer must approve the real runner's output before a pixel gate is claimed.
/// </summary>
[Collection("desktop")]
public sealed class LibrarySpokenWordScreenshots : IDisposable
{
    private readonly LibraryScene _scene = new();

    public void Dispose() => _scene.Dispose();

    [Fact]
    [Capability("LIB-05")]
    public void LocalPodcastsAndRecordingsHaveStableWideAndCompactCaptures()
    {
        _scene.Audio(Path.Combine("Podcasts", "Night Talk", "Ep 1.wav"),
            TimeSpan.FromMinutes(20), album: "Night Talk", year: 2024);
        _scene.Audio(Path.Combine("Podcasts", "Night Talk", "Ep 2.wav"),
            TimeSpan.FromMinutes(20), album: "Night Talk", year: 2025);
        _scene.Audio(Path.Combine("Phone", "Recording 001.wav"), TimeSpan.FromMinutes(3));
        _scene.Audio(Path.Combine("Downloads", "ding.wav"), TimeSpan.FromSeconds(2));
        using var app = _scene.Ready().Start();

        Wait.For(() => app.IsShown("LibraryShelf-PodcastShows"),
            "the home podcast shelf to settle before its capture");
        Screenshots.Match(app, "spoken-podcast-home", "LibraryPane");
        app.Visit("Podcasts");
        app.Open("Night Talk");
        Wait.For(() => app.Lines().SequenceEqual(["Ep 2", "Ep 1"])
            && app.Text("LibraryOverview").Contains("Local episodes only", StringComparison.Ordinal),
            "the show title, local-only description and two episode rows");
        Screenshots.Match(app, "spoken-podcast-show", "LibraryPane");

        app.Visit("Recordings");
        Wait.For(() => app.Lines().SequenceEqual(["Recording 001", "ding"]),
            "the recordings and compact sounds group");
        Screenshots.Match(app, "spoken-recordings", "LibraryPane");

        var rectangle = app.Window.Current.BoundingRectangle;
        var transform = (TransformPattern)app.Window.GetCurrentPattern(TransformPattern.Pattern);
        transform.Resize(720, Math.Max(650, rectangle.Height));
        Wait.For(() => app.Window.Current.BoundingRectangle.Width < 760,
            "the compact layout size to be applied");
        Screenshots.Match(app, "spoken-recordings-compact", "LibraryPane");
        transform.Resize(rectangle.Width, rectangle.Height);
        Assert.Equal(0, app.Close());
    }
}
