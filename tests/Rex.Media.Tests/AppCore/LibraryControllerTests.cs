using Rex.Media.AppCore.Player;
using Rex.Media.Engine;
using Rex.Media.Library;
using Rex.Media.Settings;
using Rex.Media.TestKit;
using static Rex.Media.Tests.AppCore.ControllerHarness;

namespace Rex.Media.Tests.AppCore;

/// <summary>The player and the library (LIB-05): views played and queued, and plays counted.</summary>
public sealed class LibraryControllerTests
{
    private static readonly DateTime Monday = new(2026, 10, 5, 9, 0, 0, DateTimeKind.Utc);

    private static LibraryEntry Entry(string name, string? artist = "Asake") =>
        new() { Path = name, Kind = LibraryKind.Music, Title = Path.GetFileNameWithoutExtension(name) + "!", Artist = artist, Added = Monday };

    private static (ControllerHarness Harness, MediaLibrary Library) Harness(bool keepHistory = true)
    {
        var harness = new ControllerHarness(autoPlay: false, settings: new PlayerSettings { TitleSeconds = 0, KeepHistory = keepHistory });
        var library = new MediaLibrary(RexStore.InMemory());
        library.AddFolder(".");
        foreach (var name in new[] { "Sungba.wav", "Terminator.wav", "Joha.wav" })
        {
            harness.Files[name] = Count(0, 8000);
        }

        library.Scan(".", [new LibraryFile("Sungba.wav", 1, Monday), new LibraryFile("Terminator.wav", 1, Monday), new LibraryFile("Joha.wav", 1, Monday)], _ => LibraryKind.Music);
        harness.Controller.Library = library;
        return (harness, library);
    }

    [Fact]
    [Capability("LIB-05")]
    public void AViewPlaysFromTheChosenEntryAndCountsThePlay()
    {
        var (harness, library) = Harness();
        using var _ = harness;
        var controller = harness.Controller;
        var view = new[] { Entry("Sungba.wav"), Entry("Terminator.wav", artist: null) };

        controller.PlayFromLibrary(view, start: 1);
        harness.PumpUntil(c => c.State == SessionState.Ready && c.Item?.Location == "Terminator.wav");

        Assert.Equal(["Sungba!", "Terminator!"], controller.Playlist.Items.Select(item => item.Title));
        Assert.Equal(("Asake", (string?)null), (controller.Playlist.Items[0].Artist, controller.Playlist.Items[1].Artist));
        Assert.Equal(1, library.Entry("Terminator.wav")!.Plays);

        // Continue watching supplies an explicit point: it starts there without making a resume offer.
        controller.PlayFromLibrary(view, start: 0, resumeAt: TimeSpan.FromMilliseconds(250));
        harness.PumpUntil(c => c.State == SessionState.Ready && c.Item?.Location == "Sungba.wav");
        Assert.Equal(TimeSpan.FromMilliseconds(250), controller.Position);
        Assert.Null(controller.ResumeOffer);

        // Queued after, and played only when nothing plays; nothing to play does nothing.
        controller.EnqueueFromLibrary([Entry("Joha.wav")]);
        controller.EnqueueFromLibrary([]);
        controller.PlayFromLibrary([]);
        Assert.Equal(["Sungba.wav", "Terminator.wav", "Joha.wav"], controller.Playlist.Items.Select(item => item.Location));
        Assert.Equal("Sungba.wav", controller.Item!.Location);

        // Clearing the history clears the play counts.
        controller.ClearHistory();
        Assert.Equal(0, library.Entry("Terminator.wav")!.Plays);
        Assert.Null(controller.LeftAt("Terminator.wav"));
        Assert.Throws<ArgumentNullException>(() => controller.PlayFromLibrary(null!));
        Assert.Throws<ArgumentNullException>(() => controller.EnqueueFromLibrary(null!));
        Assert.Throws<ArgumentNullException>(() => PlayerController.ItemOf(null!));
    }

    [Fact]
    [Capability("LIB-05")]
    public void WithoutHistoryPlaysAreNotCounted()
    {
        var (harness, library) = Harness(keepHistory: false);
        using var _ = harness;

        harness.Controller.PlayFromLibrary([Entry("Sungba.wav")], start: 5);
        harness.PumpUntil(c => c.State == SessionState.Ready);

        Assert.Equal(0, library.Entry("Sungba.wav")!.Plays);
    }
}
