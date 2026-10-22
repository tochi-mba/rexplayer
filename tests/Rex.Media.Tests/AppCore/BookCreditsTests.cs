using Rex.Media.AppCore.Library;
using Rex.Media.Library;
using Rex.Media.Primitives;
using static Rex.Media.Tests.AppCore.AudioRulesTests;

namespace Rex.Media.Tests.AppCore;

/// <summary>Writer tags, local layouts, bylines and narrator/author ambiguity in audiobook shelves.</summary>
public sealed class BookCreditsTests
{
    [Fact]
    public void RoaldDahlFromWrittenCreditAndMixedTaggedChapters()
    {
        var first = AudioFile("Audiobooks/Charlie and the Chocolate Factory/01.mp3", TimeSpan.FromMinutes(40),
            "Narrated by David Smith", "Charlie and the Chocolate Factory") with
        {
            Author = "Roald Dahl",
            Narrator = "David Smith",
        };
        var next = AudioFile("Audiobooks/Charlie and the Chocolate Factory/02.mp3", TimeSpan.FromMinutes(40),
            album: "Charlie and the Chocolate Factory");
        var books = ListeningViews.Books([first, next]);
        var book = Assert.Single(books);
        Assert.Equal("Charlie and the Chocolate Factory", book.Name);
        Assert.Equal("Roald Dahl", book.Detail);
        Assert.Equal([first, next], book.Entries);
        Assert.Equal(["Roald Dahl"], ListeningViews.Authors(books).Select(author => author.Name));
        Assert.Equal([book], ListeningViews.BooksByAuthor(books, "ROALD DAHL"));
    }

    [Theory]
    [InlineData("Audiobooks/Roald Dahl/Charlie and the Chocolate Factory/Disc 2/03.mp3", "Roald Dahl")]
    [InlineData("Books/Roald Dahl/Matilda/01.m4a", "Roald Dahl")]
    [InlineData("Audio_Book/Roald Dahl/Matilda/01.m4a", "Roald Dahl")]
    [InlineData("Audio Books/Ursula K. Le Guin/A Wizard of Earthsea/Part 2.mp3", "Ursula K. Le Guin")]
    [InlineData("Downloads/Matilda (written by Roald Dahl).m4b", null)]
    [InlineData("Audiobooks/Matilda/Disc 2/03.mp3", null)]
    [InlineData("Audiobooks/The Long Road/Disc 3/01.mp3", null)]
    [InlineData("Audiobooks/A Great Book/Part 12/01.mp3", null)]
    [InlineData("Audiobooks/The Great Gatsby/03.mp3", null)]
    [InlineData("Music/Roald Dahl/Matilda.mp3", null)]
    public void BookFolderInferenceRequiresARecognizableAuthorLayout(string path, string? expected)
    {
        var part = AudioFile(path, TimeSpan.FromMinutes(35));
        Assert.Equal(expected, BookCredits.AuthorFolder(part));
    }

    [Theory]
    [InlineData("Charlie and the Chocolate Factory by Roald Dahl", "Roald Dahl", "Charlie and the Chocolate Factory")]
    [InlineData("Matilda (by Roald Dahl)", "Roald Dahl", "Matilda")]
    [InlineData("The Witches - written by Roald Dahl", "Roald Dahl", "The Witches")]
    [InlineData("A Day by the Sea", null, "A Day by the Sea")]
    [InlineData("A Book narrated by Jim Reader", null, "A Book narrated by Jim Reader")]
    [InlineData("Matilda", null, "Matilda")]
    public void ExplicitBylinesAreFoundWithoutTreatingTitlesAsPeople(string named, string? author, string title)
    {
        var part = AudioFile("Downloads/01.m4b", TimeSpan.FromHours(4), album: named);
        Assert.Equal(author, BookCredits.Author(part));
        Assert.Equal(title, BookCredits.Title(named));
    }

    [Fact]
    public void BookTitleFoldersWithDiscSubfoldersAreNotMistakenForAuthorNames()
    {
        var chapter = AudioFile("Audiobooks/The Long Road/Disc 2/01.mp3", TimeSpan.FromMinutes(40));
        var book = Assert.Single(ListeningViews.Books([chapter]));
        Assert.Equal(("The Long Road", (string?)null), (book.Name, book.Detail));
    }

    [Fact]
    public void AWriterFolderWithOneWholeBookFileUsesFileTitleNotWriterAsTheBookTitle()
    {
        var part = AudioFile("Audiobooks/Roald Dahl/Matilda.m4b", TimeSpan.FromHours(5));
        var book = Assert.Single(ListeningViews.Books([part]));
        Assert.Equal(("Matilda", "Roald Dahl"), (book.Name, book.Detail));
    }

    [Fact]
    public void LooseAudiobooksInDownloadsDoNotBecomeOneBookNamedDownloads()
    {
        var matilda = AudioFile("Downloads/Matilda (by Roald Dahl).m4b", TimeSpan.FromHours(5));
        var twits = AudioFile("Downloads/The Twits.m4b", TimeSpan.FromHours(4));
        var books = ListeningViews.Books([matilda, twits]);
        Assert.Equal([("Matilda", "Roald Dahl"), ("The Twits", null)],
            books.Select(book => (book.Name, book.Detail)));
        Assert.All(books, book => Assert.Single(book.Entries));
    }

    [Fact]
    public void CreditPriorityRejectsNarratorsAndSupportsUserEdits()
    {
        var part = AudioFile("Downloads/Book.m4b", TimeSpan.FromHours(4), "Narrator Name", "Book") with
        {
            Author = "Roald Dahl",
            Narrator = "Narrator Name",
            AlbumArtist = "Narrator Name",
        };
        Assert.Equal("Roald Dahl", BookCredits.Author(part));
        Assert.Equal("Kate Winslet", BookCredits.Author(part with { Author = "Kate Winslet", Narrator = "Kate Winslet" }));
        Assert.Equal("Kate Winslet", BookCredits.Author(part with { Override = new DetailOverride { Artist = "Kate Winslet" } }));
        Assert.Null(BookCredits.Author(part with { Author = null }));
        Assert.Equal("Other Writer", BookCredits.Author(part with { Override = new DetailOverride { Artist = "Other Writer" } }));
        Assert.Null(BookCredits.Author(part with { Override = new DetailOverride { Artist = "" } }));
        Assert.Equal("Roald Dahl", BookCredits.Author(part with { Author = null, AlbumArtist = "Roald Dahl" }));
        Assert.Null(BookCredits.Author(part with { Author = "Unknown author", AlbumArtist = "Various Artists", Artist = "Narrated by Reader" }));
        Assert.Null(BookCredits.Author(part with { Author = null, Narrator = null, Artist = "Read by Jim Reader", AlbumArtist = null }));
        Assert.Null(BookCredits.Author(part with { Author = null, Narrator = "Narrated by Kate Winslet", Artist = "Kate Winslet", AlbumArtist = null }));
        Assert.Throws<ArgumentNullException>(() => BookCredits.Author(null!));
        Assert.Throws<ArgumentNullException>(() => BookCredits.Title(null!));
        Assert.Throws<ArgumentNullException>(() => BookCredits.AuthorFolder(null!));
    }

    [Fact]
    public void ConflictingCreditsDoNotAbsorbUncreditedChapters()
    {
        var first = AudioFile("Audiobooks/Mixed/01.mp3", TimeSpan.FromMinutes(30), album: "Shared Title") with { Author = "Author One" };
        var second = AudioFile("Audiobooks/Mixed/02.mp3", TimeSpan.FromMinutes(30), album: "Shared Title") with { Author = "Author Two" };
        var unknown = AudioFile("Audiobooks/Mixed/03.mp3", TimeSpan.FromMinutes(30), album: "Shared Title");
        var other = AudioFile("Audiobooks/Mixed/04.mp3", TimeSpan.FromMinutes(30), album: "Different Title") with { Author = "Author One" };
        var books = ListeningViews.Books([first, second, unknown, other]);
        Assert.Equal(4, books.Count);
        Assert.Equal([null, "Author One", "Author Two"], books.Where(book => book.Name == "Shared Title").Select(book => book.Detail));
        Assert.Single(ListeningViews.BooksByAuthor(books, ListeningViews.UnknownAuthor));
        Assert.Equal(2, ListeningViews.BooksByAuthor(books, "Author One").Count);
        Assert.Equal([first.Path, other.Path], ListeningViews.Authors(books).Single(a => a.Name == "Author One").Entries.Select(e => e.Path).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void ExplicitBookAuthorTagsSurviveLibraryReadsAndCacheRestarts()
    {
        var root = Path.Combine("D:" + Path.DirectorySeparatorChar, "Books");
        var path = Path.Combine(root, "Matilda.m4b");
        var store = RexStore.InMemory();
        var library = new MediaLibrary(store);
        library.AddFolder(root);
        library.Scan(root, [new LibraryFile(path, 1000, new DateTime(2025, 1, 2))], _ => LibraryKind.Music);
        var tags = new Dictionary<string, string>
        {
            [MetadataKeys.Title] = "Matilda",
            [MetadataKeys.Author] = "Roald Dahl",
            [MetadataKeys.Artist] = "Kate Winslet",
            [MetadataKeys.Narrator] = "Kate Winslet",
            [MetadataKeys.Genre] = "Audiobook",
        };
        library.SetDetails(path, new MediaInfo { FormatName = "MP4", Tracks = [], Metadata = tags });
        var read = library.Entry(path)!;
        Assert.Equal(("Roald Dahl", "Kate Winslet", MediaLibrary.TagReading), (read.Author, read.Narrator, read.TagReading));
        Assert.Equal("Roald Dahl", Assert.Single(ListeningViews.Books([read])).Detail);
        Assert.Equal("Roald Dahl", Assert.Single(ListeningViews.Books(new MediaLibrary(store).Entries)).Detail);
    }
    [Fact]
    public void VerifiedOnlineAuthorFillsOnlyEntirelyUncreditedBooks()
    {
        var blank = AudioFile("Audiobooks/Chocolate/1.mp3", TimeSpan.FromMinutes(45), album: "Chocolate");
        var other = AudioFile("Audiobooks/Chocolate/2.mp3", TimeSpan.FromMinutes(45), album: "Chocolate");
        var known = AudioFile("Audiobooks/Chocolate/3.mp3", TimeSpan.FromMinutes(45), album: "Chocolate") with { Author = "Another Writer" };
        var none = ListeningViews.Books([blank, other]);
        Assert.Null(Assert.Single(none).Detail);
        var found = ListeningViews.Books([blank, other], book => book.Name == "Chocolate" ? "Roald Dahl" : null);
        Assert.Equal("Roald Dahl", Assert.Single(found).Detail);
        Assert.Equal(2, Assert.Single(found).Entries.Count);
        var conflict = ListeningViews.Books([blank, other, known], _ => "Roald Dahl");
        Assert.Equal("Another Writer", Assert.Single(conflict).Detail);
        Assert.Equal(3, Assert.Single(conflict).Entries.Count);
    }

    [Fact]
    public void ABookInACollectionWithTwoCreditedWritersRemainsSeparated()
    {
        var first = AudioFile("Audiobooks/Mixed/book/1.mp3", TimeSpan.FromMinutes(15), album: "Book") with { Author = "Author One" };
        var second = AudioFile("Audiobooks/Mixed/book/2.mp3", TimeSpan.FromMinutes(15), album: "Book") with { Author = "Author Two" };
        var third = AudioFile("Audiobooks/Mixed/book/3.mp3", TimeSpan.FromMinutes(15), album: "Book");
        var books = ListeningViews.Books([first, second, third], _ => "Wrong Catalogue Writer");
        Assert.Equal(3, books.Count);
        Assert.DoesNotContain(books, book => book.Detail == "Wrong Catalogue Writer");
    }

    [Fact]
    public void WriterCandidatePriorityFollowsExplicitEditsAndDoesNotFallForNarratorTags()
    {
        var entry = AudioFile("Downloads/Chapter.mp3", TimeSpan.FromMinutes(30), "Roald Dahl", "Book") with
        {
            AlbumArtist = "Various Artists",
        };
        Assert.Equal("Roald Dahl", BookCredits.Author(entry));
        Assert.Equal("Jane Austen", BookCredits.Author(entry with { Override = new DetailOverride { AlbumArtist = "Jane Austen" } }));
        Assert.Null(BookCredits.Author(entry with { Override = new DetailOverride { AlbumArtist = "Various Artists" } }));
        Assert.Null(BookCredits.Author(entry with { AlbumArtist = "Narrator", Artist = "Read by James Reader" }));
        Assert.Null(BookCredits.Author(entry with { AlbumArtist = null, Artist = "Unknown artist" }));
        Assert.Null(BookCredits.Author(entry with { AlbumArtist = null, Artist = new string('X', 181) }));
        Assert.Null(BookCredits.Author(entry with { AlbumArtist = null, Artist = "Narrator: Reader" }));
        Assert.Null(BookCredits.Author(entry with { AlbumArtist = null, Artist = null }));
    }

    [Fact]
    public void WritingBylinesWorkInTitleFilenameAndFolderWithoutUsingReaderLabels()
    {
        var entry = AudioFile("Downloads/Matilda (written by Roald Dahl).m4b", TimeSpan.FromHours(4));
        Assert.Equal("Roald Dahl", BookCredits.Author(entry));
        var taggedTitle = AudioFile("Downloads/Untitled.m4b", TimeSpan.FromHours(4),
            title: "Matilda - by Roald Dahl");
        Assert.Equal("Roald Dahl", BookCredits.Author(taggedTitle));
        var folder = AudioFile("Downloads/The Twits (by Roald Dahl)/01.mp3", TimeSpan.FromMinutes(50));
        Assert.Equal("Roald Dahl", BookCredits.Author(folder));
        var narration = AudioFile("Downloads/Novel read by Jim Reader.m4b", TimeSpan.FromHours(4));
        Assert.Null(BookCredits.Author(narration));
        Assert.Equal("Novel read by Jim Reader", BookCredits.Title("Novel read by Jim Reader"));
        Assert.Equal("by Roald Dahl", BookCredits.Title("by Roald Dahl"));
        Assert.Equal("Novel", BookCredits.Title("Novel [written by Roald Dahl]"));
    }

    [Fact]
    public void FilenameWriterCreditsAreUsedWhenEmbeddedTitlesHaveNoByline()
    {
        var entry = AudioFile("Downloads/Matilda (written by Roald Dahl).m4b", TimeSpan.FromHours(4),
            title: "Opening Chapter");
        Assert.Equal("Roald Dahl", BookCredits.Author(entry));
    }

    [Fact]
    public void APlaceholderInAnExplicitBylineIsNotARealWriter()
    {
        var entry = AudioFile("Downloads/01.m4b", TimeSpan.FromHours(4), album: "A Tale by Unknown author");
        Assert.Null(BookCredits.Author(entry));
        Assert.Equal("A Tale by Unknown author", BookCredits.Title(entry.Album!));
    }

    [Theory]
    [InlineData("Unknown author")]
    [InlineData("Various")]
    [InlineData("N/A")]
    [InlineData("Unknown Artist")]
    [InlineData("Narrator")]
    [InlineData("Audio Book")]
    [InlineData("None")]
    public void PlaceholderCreditsDoNotCreateFalseAuthors(string credit)
    {
        var entry = AudioFile("Downloads/Title.m4b", TimeSpan.FromHours(1)) with { Author = credit };
        Assert.Null(BookCredits.Author(entry));
    }

    [Theory]
    [InlineData("Novel by Someone")]
    [InlineData("Novel by Another")]
    [InlineData("Novel by the Ocean")]
    [InlineData("Novel by A B C D E F G")]
    public void AmbiguousOrImplausibleBylinesStayUnattributed(string title)
    {
        var entry = AudioFile("Downloads/01.m4b", TimeSpan.FromHours(1), album: title);
        Assert.Null(BookCredits.Author(entry));
    }

}
