using System.Globalization;
using Rex.Media.AppCore.Library;
using Rex.Media.Library;
using Rex.Media.TestKit;
using static Rex.Media.Tests.AppCore.AudioRulesTests;

namespace Rex.Media.Tests.AppCore;

/// <summary>How the Audiobooks, Podcasts and Recordings views gather and order what they hold (LIB-05).</summary>
public sealed class ListeningViewsTests
{
    private static readonly Func<string, TimeSpan?> NeverLeft = _ => null;

    private static string[] Titles(IEnumerable<LibraryEntry> entries) => [.. entries.Select(entry => entry.Title)];

    [Fact]
    [Capability("LIB-05")]
    public void TheShelvesHoldTheAudioThatIsNotMusic()
    {
        var song = AudioFile("Music/Asake/Sungba.mp3", TimeSpan.FromMinutes(3), "Asake", "Mr. Money with the Vibe");
        var book = AudioFile("Audiobooks/Long Book.m4b", TimeSpan.FromHours(6), "An Author", "Long Book");
        var episode = AudioFile("Inbox/The Daily.mp3", TimeSpan.FromMinutes(25), "Host", "The Daily", genre: "Podcast");
        var memo = AudioFile("Phone/Recording 001.m4a", TimeSpan.FromMinutes(1)) with { Modified = new DateTime(2024, 1, 5, 10, 0, 0, DateTimeKind.Utc) };
        var newer = AudioFile("Phone/Voice 003.m4a", TimeSpan.FromMinutes(1)) with { Modified = new DateTime(2024, 2, 5, 10, 0, 0, DateTimeKind.Utc) };
        var ding = AudioFile("Downloads/ding.mp3", TimeSpan.FromSeconds(2));
        var chime = AudioFile("Downloads/Chime.mp3", TimeSpan.FromSeconds(1));
        var video = song with { Path = song.Path + ".mp4", Kind = LibraryKind.Video };

        var shelves = ListeningViews.Arrange([song, book, episode, memo, newer, ding, chime, video], AudioRules.Offline);
        Assert.Equal(["Long Book"], shelves.Audiobooks.Select(group => group.Name));
        Assert.Equal(["The Daily"], shelves.Podcasts.Select(group => group.Name));
        Assert.Equal(["Voice 003", "Recording 001"], Titles(shelves.Recordings));
        Assert.Equal(["Chime", "ding"], Titles(shelves.Sounds));
    }


    [Fact]
    [Capability("LIB-05")]
    public void WholeLengthMp3AndNamedM4aBooksAppearOnTheAudiobooksShelf()
    {
        var book = AudioFile("Downloads/A Long Story.mp3", TimeSpan.FromHours(6), "An Author", "A Long Story");
        var labelled = AudioFile("Downloads/Another (Unabridged).m4a", TimeSpan.FromHours(3), "Another Author", "Another");
        var song = AudioFile("Music/Single.mp3", TimeSpan.FromMinutes(3), "Singer", "Album");
        var shelves = ListeningViews.Arrange([book, labelled, song], AudioRules.Offline);

        Assert.Equal(["A Long Story", "Another"], shelves.Audiobooks.Select(group => group.Name));
        Assert.All(shelves.Audiobooks, group => Assert.Single(group.Entries));
        Assert.Empty(shelves.Podcasts);
        Assert.Empty(shelves.Recordings);
    }

    [Fact]
    [Capability("LIB-05")]
    public void ABooksPartsInDiscFoldersOrPartFilesAreOneBookInReadingOrder()
    {
        var parts = new[]
        {
            AudioFile("Audiobooks/The Long Road/Disc 2/02.mp3", TimeSpan.FromMinutes(70)),
            AudioFile("Audiobooks/The Long Road/Disc 1/01.mp3", TimeSpan.FromMinutes(70)),
            AudioFile("Audiobooks/The Long Road/CD10/01.mp3", TimeSpan.FromMinutes(70)),
            AudioFile("Audiobooks/Short Tale - Part 2.mp3", TimeSpan.FromMinutes(40)),
            AudioFile("Audiobooks/Short Tale - Part 1 of 2.mp3", TimeSpan.FromMinutes(40)),
            AudioFile("Audiobooks/Short Tale - Part 10.mp3", TimeSpan.FromMinutes(40)),
        };
        var books = ListeningViews.Books(parts);
        Assert.Equal(["Short Tale", "The Long Road"], books.Select(book => book.Name));
        Assert.Equal(["Short Tale - Part 1 of 2", "Short Tale - Part 2", "Short Tale - Part 10"], Titles(books[0].Entries));
        Assert.Equal([parts[1], parts[0], parts[2]], books[1].Entries);
        Assert.Null(books[1].Detail);
    }

    [Fact]
    [Capability("LIB-05")]
    public void SeveralBooksInOneFolderPartByAlbumAndAuthorAndTagsOrderTheParts()
    {
        var first = AudioFile("Audiobooks/Mixed/a.mp3", TimeSpan.FromHours(1), "Ann Writer", "First Book", track: 2);
        var firstOpening = AudioFile("Audiobooks/Mixed/b.mp3", TimeSpan.FromHours(1), "Ann Writer", "First Book", track: 1);
        var second = AudioFile("Audiobooks/Mixed/c.mp3", TimeSpan.FromHours(1), "Ann Writer", "Second Book");
        var namesake = AudioFile("Audiobooks/Mixed/d.mp3", TimeSpan.FromHours(1), "Bo Other", "First Book");
        var books = ListeningViews.Books([first, firstOpening, second, namesake]);
        Assert.Equal([("First Book", "Ann Writer"), ("First Book", "Bo Other"), ("Second Book", "Ann Writer")], books.Select(book => (book.Name, book.Detail)));
        Assert.Equal([firstOpening, first], books[0].Entries);

        // A loose book in the root folder is named from its title; one in a drive's root too.
        Assert.Equal("Loose", ListeningViews.Books([AudioFile("Audiobooks/Loose Part 3.m4b", TimeSpan.FromHours(2))])[0].Name);
        Assert.Equal("07", ListeningViews.Books([new LibraryEntry { Path = "D:" + Path.DirectorySeparatorChar + "07.m4b", Kind = LibraryKind.Music, Title = "07" }])[0].Name);
    }

    [Fact]
    [Capability("LIB-05")]
    public void ABookHasChaptersOnePlaceToResumeFromAndItsProgress()
    {
        var day = new DateTime(2024, 3, 1, 9, 0, 0, DateTimeKind.Utc);
        var one = AudioFile("Audiobooks/Book/Part 1.mp3", TimeSpan.FromMinutes(60)) with { LastPlayed = day };
        var two = AudioFile("Audiobooks/Book/Part 2.mp3", TimeSpan.FromMinutes(60), chapters: 2) with { LastPlayed = day.AddHours(1) };
        var three = AudioFile("Audiobooks/Book/Part 3.mp3", TimeSpan.FromMinutes(60));
        var book = new LibraryGroup("Book", null, [one, two, three]);

        Assert.Equal(["Part 1", "Chapter 1", "Chapter 2", "Part 3"], ListeningViews.Chapters(book).Select(chapter => chapter.Title));
        Assert.Equal(TimeSpan.FromMinutes(10), ListeningViews.Chapters(book)[2].Start);
        var untitled = book with { Entries = [two with { Chapters = [new LibraryChapter(TimeSpan.Zero, "")] }] };
        Assert.Equal("Part 2", ListeningViews.Chapters(untitled)[0].Title);

        // The part played last, at the point it was left.
        var left = (string path) => path == two.Path ? TimeSpan.FromMinutes(30) : (TimeSpan?)null;
        var place = ListeningViews.PlaceIn(book, left);
        Assert.Equal(new BookPlace(1, TimeSpan.FromMinutes(30)), place);
        Assert.Equal(0.5, ListeningViews.Progress(book, place), 3);
        Assert.True(ListeningViews.Started(book, NeverLeft));

        // A book never played starts at its first part, unless its first part was left part-way.
        var fresh = new LibraryGroup("Fresh", null, [three, three with { Path = three.Path + "4" }]);
        Assert.Equal(new BookPlace(0, TimeSpan.Zero), ListeningViews.PlaceIn(fresh, NeverLeft));
        Assert.False(ListeningViews.Started(fresh, NeverLeft));
        Assert.True(ListeningViews.Started(fresh, path => path == three.Path ? TimeSpan.FromMinutes(1) : null));

        // Without running times, progress goes by parts.
        var unknown = new LibraryGroup("Unknown", null, [one with { Duration = null }, two with { Duration = null }]);
        Assert.Equal(0.5, ListeningViews.Progress(unknown, new BookPlace(1, TimeSpan.Zero)));
        Assert.Equal("BOOK\nAN AUTHOR", ListeningViews.BookKey(new LibraryGroup("Book", "An Author", [one])));
    }

    [Fact]
    [Capability("LIB-05")]
    public void ShowsMergeAcrossFoldersByTheirNameAndListEpisodesNewestFirst()
    {
        var older = AudioFile("Podcasts/The Daily/Ep 1.mp3", TimeSpan.FromMinutes(20), "Host", "The Daily", year: 2023) with { Modified = new DateTime(2023, 5, 1, 0, 0, 0, DateTimeKind.Utc) };
        var newer = AudioFile("Downloads/the-daily/Ep 2.mp3", TimeSpan.FromMinutes(20), "Host", "the daily!") with { Modified = new DateTime(2024, 5, 1, 0, 0, 0, DateTimeKind.Utc), Plays = 1 };
        var loose = AudioFile("Podcasts/Ep 9.mp3", TimeSpan.FromMinutes(20));
        var byFolder = AudioFile("Podcasts/Night Talk/Ep 3.mp3", TimeSpan.FromMinutes(20));
        var byArtist = AudioFile("Podcasts/x/Ep 3.mp3", TimeSpan.FromMinutes(20), "Solo Host");

        var shows = ListeningViews.Shows([older, newer, loose, byFolder, byArtist]);
        Assert.Equal(["Night Talk", "Solo Host", "The Daily", ListeningViews.UnknownShow], shows.Select(show => show.Name));
        var daily = shows[2];
        Assert.Equal([newer, older], daily.Entries);
        Assert.Equal("Host", daily.Detail);
        Assert.Null(shows[1].Detail);
        Assert.Equal([older, newer], ListeningViews.Episodes(daily.Entries, oldestFirst: true));
        Assert.Equal(newer.Modified, ListeningViews.NewestEpisode(daily));
        Assert.Equal(1, ListeningViews.Unplayed(daily, NeverLeft));
        Assert.Equal(0, ListeningViews.Unplayed(daily, path => path == older.Path ? TimeSpan.FromMinutes(1) : null));
        Assert.Equal([older], ListeningViews.UnplayedInOrder(daily, NeverLeft));
    }

    [Fact]
    [Capability("LIB-05")]
    public void AnEpisodesDateComesFromItsTagsThenTheFileThenItsName()
    {
        var file = new DateTime(2024, 6, 2, 8, 0, 0, DateTimeKind.Utc);
        Assert.Equal(file, ListeningViews.EpisodeDate(AudioFile("P/a.mp3", null, year: 2024) with { Modified = file }));
        Assert.Equal(new DateTime(2019, 1, 1, 0, 0, 0, DateTimeKind.Utc), ListeningViews.EpisodeDate(AudioFile("P/a.mp3", null, year: 2019) with { Modified = file }));
        Assert.Equal(file, ListeningViews.EpisodeDate(AudioFile("P/a.mp3", null) with { Modified = file }));
        Assert.Equal(new DateTime(2024, 1, 5), ListeningViews.EpisodeDate(AudioFile("P/Show 2024-01-05.mp3", null)));
        Assert.Equal(DateTime.MinValue, ListeningViews.EpisodeDate(AudioFile("P/Show.mp3", null)));
    }

    [Fact]
    [Capability("LIB-05")]
    public void ContinueListeningAndNewEpisodesHoldOnlyWhatIsRelevant()
    {
        var now = new DateTime(2024, 3, 10, 12, 0, 0, DateTimeKind.Utc);
        var part = AudioFile("Audiobooks/Book/Part 1.mp3", TimeSpan.FromHours(1)) with { LastPlayed = now.AddDays(-2) };
        var finishedPart = AudioFile("Audiobooks/Done/Part 1.mp3", TimeSpan.FromHours(1)) with { LastPlayed = now.AddDays(-1) };
        var unread = AudioFile("Audiobooks/Unread/Part 1.mp3", TimeSpan.FromHours(1));
        var halfHeard = AudioFile("Podcasts/Show/Ep 1.mp3", TimeSpan.FromMinutes(30)) with { LastPlayed = now.AddHours(-1), Added = now.AddDays(-30) };
        var fresh = AudioFile("Podcasts/Show/Ep 2.mp3", TimeSpan.FromMinutes(30)) with { Added = now.AddDays(-1), Modified = now.AddDays(-1) };
        var stale = AudioFile("Podcasts/Show/Ep 0.mp3", TimeSpan.FromMinutes(30)) with { Added = now.AddDays(-40) };
        var shelves = ListeningViews.Arrange([part, finishedPart, unread, halfHeard, fresh, stale], AudioRules.Offline);
        var left = (string path) => path == halfHeard.Path ? TimeSpan.FromMinutes(5) : (TimeSpan?)null;

        var going = ListeningViews.ContinueListening(shelves, left, book => book.Name == "Done");
        Assert.Equal([halfHeard, part], going.Select(item => item.Entry));
        Assert.Equal("Book", going[1].Book!.Name);
        Assert.Null(going[0].Book);
        Assert.Equal([fresh], ListeningViews.NewEpisodes(shelves, left, now));
        Assert.Empty(ListeningViews.NewEpisodes(shelves, left, now.AddDays(30)));
    }

    [Fact]
    [Capability("LIB-05")]
    public void RecordingsTakeTheirTimeFromTheNameThenTheFile()
    {
        Assert.Equal(new DateTime(2024, 1, 5, 10, 11, 12), ListeningViews.RecordedAt(AudioFile("Phone/REC_20240105_101112.wav", null)));
        Assert.Equal(new DateTime(2024, 1, 5, 10, 11, 0), ListeningViews.RecordedAt(AudioFile("Phone/2024-01-05 10-11.m4a", null)));
        Assert.Equal(new DateTime(2024, 1, 5), ListeningViews.RecordedAt(AudioFile("Phone/PTT-20240105-WA0003.opus", null)));
        var file = new DateTime(2023, 4, 1, 0, 0, 0, DateTimeKind.Utc);
        Assert.Equal(file, ListeningViews.RecordedAt(AudioFile("Phone/Recording 001.m4a", null) with { Modified = file }));
        Assert.Null(ListeningViews.RecordedAt(AudioFile("Phone/Recording 001.m4a", null)));

        // A date that is no date is passed over.
        Assert.Null(ListeningViews.RecordedAt(AudioFile("Phone/REC_20241332.wav", null)));
    }

    [Fact]
    [Capability("LIB-05")]
    public void TheRecordingsViewGroupsByDayThenMonthWithWhatsAppUndatedAndSoundsApart()
    {
        var now = new DateTime(2024, 3, 20, 15, 0, 0, DateTimeKind.Local);
        LibraryEntry Made(string name, DateTime at) => AudioFile("Phone/" + name, TimeSpan.FromMinutes(1)) with { Modified = at.ToUniversalTime() };
        var today = Made("Voice 001.m4a", now.AddHours(-1));
        var yesterday = Made("Voice 002.m4a", now.AddDays(-1));
        var thisWeek = Made("Voice 003.m4a", now.AddDays(-4));
        var february = Made("Voice 004.m4a", new DateTime(2024, 2, 10, 9, 0, 0, DateTimeKind.Local));
        var named = AudioFile("Phone/REC_20240101_090000.wav", TimeSpan.FromMinutes(1));
        var whatsApp = Made("PTT-20240319-WA0001.opus", now);
        var undated = AudioFile("Phone/Recording 009.m4a", TimeSpan.FromMinutes(1));
        var ding = AudioFile("Downloads/ding.mp3", TimeSpan.FromSeconds(2));

        var sections = ListeningViews.RecordingSections([whatsApp, february, undated, today, named, yesterday, thisWeek], [ding], now);
        var january = new DateTime(2024, 1, 1).ToString("MMMM yyyy", CultureInfo.CurrentCulture);
        var feb = new DateTime(2024, 2, 1).ToString("MMMM yyyy", CultureInfo.CurrentCulture);
        Assert.Equal(["Today", "Yesterday", "This week", feb, january, ListeningViews.WhatsAppHeader, ListeningViews.UndatedHeader, ListeningViews.SoundsHeader],
            sections.Select(section => section.Header));
        Assert.Equal([today], sections[0].Entries);
        Assert.Equal([ding], sections[^1].Entries);
        Assert.DoesNotContain(sections, section => section.Header == ListeningViews.SoundsHeader && section.Entries.Count == 0);
        Assert.Equal(["Today"], ListeningViews.RecordingSections([today], [], now).Select(section => section.Header));
    }

    [Fact]
    [Capability("LIB-05")]
    public void ABooksSpeedAndFinishingAreKeptInTheStore()
    {
        var memory = new AudiobookMemory(RexStore.InMemory());
        var book = new LibraryGroup("Book", "An Author", [AudioFile("Audiobooks/Book.m4b", TimeSpan.FromHours(1))]);
        Assert.Null(memory.Speed(book));
        memory.KeepSpeed(book, 1.25);
        Assert.Equal(1.25, memory.Speed(new LibraryGroup("BOOK", "an author", [])));
        memory.KeepSpeed(book, 1);
        Assert.Null(memory.Speed(book));
        Assert.False(memory.Finished(book));
        memory.SetFinished(book, true);
        Assert.True(memory.Finished(book));
        memory.SetFinished(book, false);
        Assert.False(memory.Finished(book));
        memory.KeepSpeed(book, 1.5);
        memory.Store.Set(memory.Store.WithPrefix("audiobook-speed/").Single().Key, "fast");
        Assert.Null(memory.Speed(book));
        Assert.Throws<ArgumentNullException>(() => new AudiobookMemory(null!));
    }

    [Fact]
    public void NullsAreRefused()
    {
        var group = new LibraryGroup("x", null, [AudioFile("a.mp3", null)]);
        var shelves = new ListeningShelves([], [], [], []);
        Assert.Throws<ArgumentNullException>(() => ListeningViews.Arrange(null!, AudioRules.Offline));
        Assert.Throws<ArgumentNullException>(() => ListeningViews.Arrange([], null!));
        Assert.Throws<ArgumentNullException>(() => ListeningViews.Books(null!));
        Assert.Throws<ArgumentNullException>(() => ListeningViews.Shows(null!));
        Assert.Throws<ArgumentNullException>(() => ListeningViews.Episodes(null!, false));
        Assert.Throws<ArgumentNullException>(() => ListeningViews.EpisodeDate(null!));
        Assert.Throws<ArgumentNullException>(() => ListeningViews.NewestEpisode(null!));
        Assert.Throws<ArgumentNullException>(() => ListeningViews.Unplayed(null!, NeverLeft));
        Assert.Throws<ArgumentNullException>(() => ListeningViews.Unplayed(group, null!));
        Assert.Throws<ArgumentNullException>(() => ListeningViews.IsUnplayed(null!, NeverLeft));
        Assert.Throws<ArgumentNullException>(() => ListeningViews.IsUnplayed(group.Entries[0], null!));
        Assert.Throws<ArgumentNullException>(() => ListeningViews.UnplayedInOrder(null!, NeverLeft));
        Assert.Throws<ArgumentNullException>(() => ListeningViews.UnplayedInOrder(group, null!));
        Assert.Throws<ArgumentNullException>(() => ListeningViews.BookKey(null!));
        Assert.Throws<ArgumentNullException>(() => ListeningViews.Chapters(null!));
        Assert.Throws<ArgumentNullException>(() => ListeningViews.PlaceIn(null!, NeverLeft));
        Assert.Throws<ArgumentNullException>(() => ListeningViews.PlaceIn(group, null!));
        Assert.Throws<ArgumentNullException>(() => ListeningViews.Started(null!, NeverLeft));
        Assert.Throws<ArgumentNullException>(() => ListeningViews.Started(group, null!));
        Assert.Throws<ArgumentNullException>(() => ListeningViews.Progress(null!, default));
        Assert.Throws<ArgumentNullException>(() => ListeningViews.ContinueListening(null!, NeverLeft, _ => false));
        Assert.Throws<ArgumentNullException>(() => ListeningViews.ContinueListening(shelves, null!, _ => false));
        Assert.Throws<ArgumentNullException>(() => ListeningViews.ContinueListening(shelves, NeverLeft, null!));
        Assert.Throws<ArgumentNullException>(() => ListeningViews.NewEpisodes(null!, NeverLeft, DateTime.UtcNow));
        Assert.Throws<ArgumentNullException>(() => ListeningViews.NewEpisodes(shelves, null!, DateTime.UtcNow));
        Assert.Throws<ArgumentNullException>(() => ListeningViews.RecordedAt(null!));
        Assert.Throws<ArgumentNullException>(() => ListeningViews.RecordingSections(null!, [], DateTime.Now));
        Assert.Throws<ArgumentNullException>(() => ListeningViews.RecordingSections([], null!, DateTime.Now));
    }
}
