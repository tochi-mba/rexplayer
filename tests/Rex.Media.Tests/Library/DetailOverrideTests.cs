using Rex.Media.AppCore.Library;
using Rex.Media.Engine;
using Rex.Media.Library;
using Rex.Media.Primitives;
using Rex.Media.Settings;
using Rex.Media.Tests.AppCore;
using static Rex.Media.Tests.AppCore.ControllerHarness;

namespace Rex.Media.Tests.Library;

public sealed class DetailOverrideTests
{
    private static readonly DateTime Monday = new(2026, 10, 5, 9, 0, 0, DateTimeKind.Utc);

    private static LibraryEntry Song(string path, string title, string? album = "Work of Art", int? track = 1) =>
        new() { Path = path, Kind = LibraryKind.Music, Title = title, Artist = "Asake", Album = album, Track = track, Year = 2023, Genre = "Afrobeats" };

    private static MediaInfo Tags(string title) => new()
    {
        FormatName = "MP3",
        Tracks = [],
        Metadata = new Dictionary<string, string> { [MetadataKeys.Title] = title, [MetadataKeys.Artist] = "Asake", [MetadataKeys.Album] = "Work of Art" },
    };

    [Fact]
    public void AnOverrideLaysItsFieldsOverTheEntryAndClearsEmptiedOnes()
    {
        var entry = Song("Sungba.mp3", "Old") with { AlbumArtist = "Asake", Disc = 1 };
        var change = new DetailOverride { Title = " ", Artist = "Asake; Burna Boy", Album = "", AlbumArtist = "Various", Genre = "Afro", Track = "3/12", Disc = "x", Year = "99" };

        var applied = change.ApplyTo(entry);

        Assert.Equal("Sungba", applied.Title);
        Assert.Equal("Asake; Burna Boy", applied.Artist);
        Assert.Null(applied.Album);
        Assert.Equal("Various", applied.AlbumArtist);
        Assert.Equal("Afro", applied.Genre);
        Assert.Equal(3, applied.Track);
        Assert.Null(applied.Disc);
        Assert.Null(applied.Year);
        Assert.Equal(2024, new DetailOverride { Year = "2024-01-01" }.ApplyTo(entry).Year);
        Assert.Equal(entry, new DetailOverride().ApplyTo(entry));
        // A typed artist replaces the file's list of artists; other changes keep it.
        var listed = entry with { Artists = "Asake; Olamide" };
        Assert.Null(change.ApplyTo(listed).Artists);
        Assert.Equal("Asake; Olamide", new DetailOverride { Genre = "Afro" }.ApplyTo(listed).Artists);
        Assert.True(new DetailOverride().IsEmpty);
        Assert.False(change.IsEmpty);
    }

    [Fact]
    public void ALaterOverrideWinsFieldByField()
    {
        var first = new DetailOverride { Title = "A", Artist = "B", Album = "C", AlbumArtist = "D", Genre = "E", Track = "1", Disc = "2", Year = "2020" };

        Assert.Equal(first with { Title = "Z" }, first.Then(new DetailOverride { Title = "Z" }));
        Assert.Equal(first, new DetailOverride().Then(first));
    }

    [Fact]
    public void TheLibraryKeepsAnOverrideAcrossRescansAndForgetsItWhenTheFileHoldsTheDetails()
    {
        var store = RexStore.InMemory();
        var library = new MediaLibrary(store);
        library.AddFolder(".");
        library.Scan(".", [new LibraryFile("Sungba.mp3", 1, Monday)], _ => LibraryKind.Music);
        library.SetDetails("Sungba.mp3", Tags("Old"));
        var changes = 0;
        library.Changed += (_, _) => changes++;

        library.SetOverride(["Sungba.mp3", "missing.mp3"], new DetailOverride { Title = "Sungba" });
        library.SetOverride(["Sungba.mp3"], new DetailOverride { Album = "" });

        Assert.Equal(2, changes);
        Assert.Equal("Sungba", library.Entry("Sungba.mp3")!.Title);
        Assert.Null(library.Entry("Sungba.mp3")!.Album);
        library.Scan(".", [new LibraryFile("Sungba.mp3", 2, Monday)], _ => LibraryKind.Music);
        library.SetDetails("Sungba.mp3", Tags("Old"));
        var reopened = new MediaLibrary(store).Entry("Sungba.mp3")!;
        Assert.Equal("Sungba", reopened.Title);
        Assert.Null(reopened.Album);

        library.ClearOverride(["Sungba.mp3"]);
        Assert.Null(library.Entry("Sungba.mp3")!.Override);
        Assert.False(library.Entry("Sungba.mp3")!.Probed);
        library.SetDetails("Sungba.mp3", Tags("Old"));
        Assert.Equal("Old", library.Entry("Sungba.mp3")!.Title);
    }

    [Fact]
    public void AMovieChoiceOutlivesRescansReopeningAndTagsWrittenIntoTheFile()
    {
        var store = RexStore.InMemory();
        var library = new MediaLibrary(store);
        library.AddFolder(".");
        library.Scan(".", [new LibraryFile("IMG_1234.mp4", 1, Monday)], _ => LibraryKind.Video);

        library.SetOverride(["IMG_1234.mp4"], new DetailOverride { Movie = true });
        library.SetOverride(["IMG_1234.mp4"], new DetailOverride { Title = "Home Movie" });
        library.Scan(".", [new LibraryFile("IMG_1234.mp4", 2, Monday)], _ => LibraryKind.Video);
        library.SetDetails("IMG_1234.mp4", Tags("Old"));

        var reopened = new MediaLibrary(store).Entry("IMG_1234.mp4")!;
        Assert.Equal(new DetailOverride { Movie = true, Title = "Home Movie" }, reopened.Override);
        Assert.Equal("Home Movie", reopened.Title);
        library.ClearOverride(["IMG_1234.mp4"]);
        Assert.Equal(new DetailOverride { Movie = true }, library.Entry("IMG_1234.mp4")!.Override);
        Assert.Equal(new DetailOverride { Movie = false }, new DetailOverride { Movie = true }.Then(new DetailOverride { Movie = false }));
        Assert.Null(new DetailOverride { Title = "Only tags" }.Kept());
    }

    [Fact]
    public void TheDialogSaysWhereTheDetailsGoAndNeverWritesIntoADownloadingFile()
    {
        Assert.Equal("Saved in rexplayer only, as Preferences asks; the files are not changed.", DetailsForm.Note(2, 0, 1, writing: false));
        Assert.Equal("Saved into the files.", DetailsForm.Note(2, 2, 0, writing: true));
        Assert.Equal("This file is still downloading, so its details are saved in rexplayer only until it is whole.", DetailsForm.Note(1, 0, 1, writing: true));
        Assert.Equal("These files are still downloading, so their details are saved in rexplayer only until they are whole.", DetailsForm.Note(3, 0, 3, writing: true));
        Assert.Equal(Rex.Media.Containers.Tags.TagFileWriter.LibraryOnlyMessage, DetailsForm.Note(2, 0, 0, writing: true));
        Assert.Equal("1 of these files can't be written to; their details are saved in rexplayer only.", DetailsForm.Note(3, 2, 0, writing: true));
        Assert.Equal("2 of these files can't be written to (1 still downloading); their details are saved in rexplayer only.", DetailsForm.Note(3, 1, 1, writing: true));
    }

    [Fact]
    public void TheFormShowsSharedValuesAndSeveralValues()
    {
        var songs = new[] { Song("a.mp3", "Sungba"), Song("b.mp3", "Terminator", track: 2) };

        var fields = DetailsForm.Fields(songs);

        Assert.Equal(["Title", "Artist", "Album", "Album artist", "Track", "Disc", "Year", "Genre"], fields.Select(field => field.Label));
        Assert.True(fields[0].Several);
        Assert.Equal("", fields[0].Value);
        Assert.Equal(new DetailField(MetadataKeys.Artist, "Artist", "Asake", false), fields[1]);
        Assert.Equal("", fields[3].Value);
        Assert.False(fields[3].Several);
        Assert.True(fields[4].Several);
        Assert.Equal("", fields[5].Value);
        Assert.Equal("2023", fields[6].Value);
        Assert.Equal("Afrobeats", fields[7].Value);
        Assert.Throws<ArgumentOutOfRangeException>(() => DetailsForm.Fields([]));
    }

    [Fact]
    public void OnlyChangedBoxesAreSaved()
    {
        var fields = DetailsForm.Fields([Song("a.mp3", "Sungba"), Song("b.mp3", "Terminator")]);

        var change = DetailsForm.Change(fields, new Dictionary<string, string>
        {
            [MetadataKeys.Title] = "",
            [MetadataKeys.Artist] = " Asake ",
            [MetadataKeys.Album] = "",
            [MetadataKeys.Genre] = "Afro-fusion",
            [MetadataKeys.Disc] = "1",
        });

        Assert.Equal(new DetailOverride { Album = "", Genre = "Afro-fusion", Disc = "1" }, change);
        Assert.Equal(new DetailOverride { Title = "Lonely At The Top" }, DetailsForm.Change(fields, new Dictionary<string, string> { [MetadataKeys.Title] = "Lonely At The Top" }));
    }

    [Fact]
    public void FieldProvenanceSeparatesLocalOverridesFromUnreadAndMixedSources()
    {
        var a = Song("a.mp3", "First");
        var b = Song("b.mp3", "Second");
        Assert.Equal("No selected media", DetailsForm.Provenance([], MetadataKeys.Title));
        Assert.Equal("Indexed locally; some file metadata has not been read yet",
            DetailsForm.Provenance([a], MetadataKeys.Title));

        var read = a with { Probed = true };
        Assert.Equal("From the local media scan; tags and filename fallback may both contribute",
            DetailsForm.Provenance([read], MetadataKeys.Title));
        var overridden = read with { Override = new DetailOverride { Title = "Chosen" } };
        Assert.Equal("Locally overridden in rexplayer",
            DetailsForm.Provenance([overridden], MetadataKeys.Title));
        Assert.Equal("Mixed sources, including rexplayer overrides",
            DetailsForm.Provenance([overridden, b], MetadataKeys.Title));
        Assert.Equal("From the local media scan; tags and filename fallback may both contribute",
            DetailsForm.Provenance([overridden], MetadataKeys.Artist));
        Assert.Throws<ArgumentNullException>(() => DetailsForm.Provenance(null!, MetadataKeys.Title));
        Assert.Throws<ArgumentNullException>(() => DetailsForm.Provenance([read], null!));
    }

    [Theory]
    [InlineData("1", "2", "2026")]
    [InlineData("3/12", "1/2", "2024-11")]
    [InlineData("3/12", "", "2024-11-28")]
    [InlineData("", "", "")]
    public void ValidChangedTrackDiscAndDatesAreAccepted(string track, string disc, string year)
    {
        Assert.Empty(DetailsForm.Problems(new DetailOverride { Track = track, Disc = disc, Year = year }));
        Assert.Empty(DetailsForm.Problems(new DetailOverride()));
    }

    [Theory]
    [InlineData("0", "0", "bad")]
    [InlineData("3/2", "-1", "2024-99-01")]
    [InlineData("hello", "2/0", "2024-02-40")]
    [InlineData("10000", "3/2", "99")]
    public void InvalidChangedTrackDiscAndDatesAreRejectedBeforeWriting(string track, string disc, string year)
    {
        var issues = DetailsForm.Problems(new DetailOverride { Track = track, Disc = disc, Year = year });
        Assert.Equal(3, issues.Count);
        Assert.Contains(issues, issue => issue.Contains("Track", StringComparison.Ordinal));
        Assert.Contains(issues, issue => issue.Contains("Disc", StringComparison.Ordinal));
        Assert.Contains(issues, issue => issue.Contains("Date", StringComparison.Ordinal));
        Assert.Throws<ArgumentNullException>(() => DetailsForm.Problems(null!));
    }

    [Fact]
    public void ApplyLaysAnEditOverAFilesMetadata()
    {
        var metadata = new Dictionary<string, string> { [MetadataKeys.Title] = "Old", [MetadataKeys.Album] = "Work of Art", [MetadataKeys.Composer] = "Ahmed" };

        var applied = DetailsForm.Apply(metadata, new DetailOverride { Title = "Sungba", Album = "", Year = "2023" });

        Assert.Equal("Sungba", applied[MetadataKeys.Title]);
        Assert.False(applied.ContainsKey(MetadataKeys.Album));
        Assert.Equal("2023", applied[MetadataKeys.Date]);
        Assert.Equal("Ahmed", applied[MetadataKeys.Composer]);
    }

    [Fact]
    public void WhatPlaysShowsItsEditedDetailsWithoutReopening()
    {
        using var harness = new ControllerHarness(autoPlay: false, settings: new PlayerSettings { TitleSeconds = 0 });
        harness.Files["Sungba.wav"] = Count(0, 8000);
        var controller = harness.Controller;
        controller.ShowEditedDetails(["Sungba.wav"], new DetailOverride { Title = "Nothing plays" });
        controller.Open(["Sungba.wav"]);
        harness.PumpUntil(c => c.State == SessionState.Ready && c.Info is not null);
        var changes = 0;
        controller.Changed += (_, _) => changes++;

        controller.ShowEditedDetails(["Other.wav"], new DetailOverride { Title = "Terminator" });
        Assert.Equal(0, changes);
        controller.ShowEditedDetails(["SUNGBA.wav"], new DetailOverride { Title = "Sungba", Artist = "Asake" });

        Assert.Equal(1, changes);
        Assert.Equal("Sungba", controller.Title);
        Assert.Equal("Asake", controller.Artist);
        Assert.Equal(SessionState.Ready, controller.State);
    }
}
