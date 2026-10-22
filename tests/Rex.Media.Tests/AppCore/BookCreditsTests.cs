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
    [InlineData("Audio Books/Ursula K. Le Guin/A Wizard of Earthsea/Part 2.mp3", "Ursula K. Le Guin")]
    [InlineData("Downloads/Matilda (written by Roald Dahl).m4b", null)]
    [InlineData("Audiobooks/Matilda/Disc 2/03.mp3", null)]
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
    public void AWriterFolderWithOneWholeBookFileUsesFileTitleNotWriterAsTheBookTitle()
    {
        var part = AudioFile("Audiobooks/Roald Dahl/Matilda.m4b", TimeSpan.FromHours(5));
        var book = Assert.Single(ListeningViews.Books([part]));
        Assert.Equal(("Matilda", "Roald Dahl"), (book.Name, book.Detail));
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
        Assert.Null(BookCredits.Author(part with { Author = null }));
        Assert.Equal("Other Writer", BookCredits.Author(part with { Override = new DetailOverride { Artist = "Other Writer" } }));
        Assert.Null(BookCredits.Author(part with { Override = new DetailOverride { Artist = "" } }));
        Assert.Equal("Roald Dahl", BookCredits.Author(part with { Author = null, AlbumArtist = "Roald Dahl" }));
        Assert.Null(BookCredits.Author(part with { Author = "Unknown author", AlbumArtist = "Various Artists", Artist = "Narrated by Reader" }));
        Assert.Null(BookCredits.Author(part with { Author = null, Narrator = null, Artist = "Read by Jim Reader", AlbumArtist = null }));
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
        Assert.Equal(["Author One", "Author Two", null], books.Where(book => book.Name == "Shared Title").Select(book => book.Detail));
        Assert.Single(ListeningViews.BooksByAuthor(books, ListeningViews.UnknownAuthor));
        Assert.Equal(2, ListeningViews.BooksByAuthor(books, "Author One").Count);
        Assert.Equal([first.Path, other.Path], ListeningViews.Authors(books).Single(a => a.Name == "Author One").Entries.Select(e => e.Path).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void ExplicitBookAuthorTagsSurviveLibraryReadsAndCacheRestarts()
    {
        var path = Path.Combine("D:", "Books", "Matilda.m4b");
        var store = RexStore.InMemory();
        var library = new MediaLibrary(store);
        library.AddFolder(Path.Combine("D:", "Books"));
        library.Scan(Path.Combine("D:", "Books"), [new LibraryFile(path, 1000, new DateTime(2025, 1, 2))], _ => LibraryKind.Music);
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
}
