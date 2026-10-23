using System.IO;
using Rex.Media.AppCore.Library;
using Rex.Media.Settings;

namespace Rex.Media.App.Tests;

/// <summary>Real Windows UI Automation proof of stage context after crossing media types.</summary>
[Collection("desktop")]
public sealed class MediaStageWindowTests : IDisposable
{
    private readonly LibraryScene _scene = new();

    public void Dispose() => _scene.Dispose();

    [Fact]
    public void SwitchingSongToMovieToPictureUpdatesTheAccessibleStageWithoutStaleContext()
    {
        _scene.WithAsake();
        _scene.Film(Path.Combine("Movies", "Small.Film.2023.mp4"));
        _scene.Picture("still.jpg");
        var cards = new LibraryViewChoice(LibraryLook.Grid, CardSize: 200) { LookChosen = true };
        using var app = _scene.Ready(settings => settings with
        {
            LibraryViews = new Dictionary<string, LibraryViewChoice>(StringComparer.Ordinal)
            {
                ["Movies"] = cards,
            },
        }).Start();

        app.Visit("Songs");
        app.Choose("Sungba");
        app.Press("LibraryPlay");
        Wait.For(() => app.Text("NowPlaying") == "Sungba" &&
            app.Find("Stage").Current.Name == "NOW PLAYING playback stage",
            "the music stage to identify the playing song");

        app.Run(Rex.Media.AppCore.Commands.CommandCatalog.ToggleLibrary);
        app.Visit("Movies");
        Wait.For(() => app.Lines().Contains("Small Film"), "the movie card");
        app.Choose("Small Film");
        app.Press("LibraryPlay");
        Wait.For(() => app.Find("Stage").Current.Name == "MOVIE playback stage",
            "the movie stage to identify its media type");

        app.Run(Rex.Media.AppCore.Commands.CommandCatalog.ToggleLibrary);
        app.Visit("Pictures");
        app.Choose("still");
        app.Press("LibraryPlay");
        Wait.For(() => app.Find("Stage").Current.Name == "PICTURE playback stage",
            "the picture stage to discard the old movie context");
        Assert.False(app.IsShown("Lyrics"), "Lyrics should not carry over to picture playback.");
        Assert.Equal(0, app.Close());
    }
}
