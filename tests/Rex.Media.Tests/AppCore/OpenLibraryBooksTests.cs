using System.Text;
using System.Text.Json;
using Rex.Media.AppCore.Metadata;

namespace Rex.Media.Tests.AppCore;

public sealed class OpenLibraryBooksTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "rexplayer-books-" + Guid.NewGuid().ToString("N"));
    private readonly Dictionary<string, byte[]> _answers = new(StringComparer.Ordinal);
    private readonly List<string> _calls = [];

    private MetadataFetcher Fetcher => new(new MetadataCache(_folder), (uri, _) =>
    {
        _calls.Add(uri.AbsoluteUri);
        return Task.FromResult(_answers.GetValueOrDefault(uri.AbsoluteUri));
    });

    private void Answer(Uri uri, string json) => _answers[uri.AbsoluteUri] = Encoding.UTF8.GetBytes(json);

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    private const string Search = """
        {"docs":[
          {"title":"Another Book","author_name":["J. R. R. Tolkien"],"key":"/works/OL12W"},
          {"title":"The Hobbit","author_name":["Someone Else"],"key":"/works/OL13W"},
          {"title":"The Hobbit","author_name":["J. R. R. Tolkien"],"key":"/works/OL123W"}
        ]}
        """;

    [Fact]
    public void SearchMatchesBothTheWorkTitleAndAnActualAuthor()
    {
        Assert.Equal("OL123W", OpenLibraryBooks.Match("The Hobbit", "J. R. R. Tolkien", Search));
        Assert.Null(OpenLibraryBooks.Match("The Hobbit", "Not Tolkien", Search));
        Assert.Null(OpenLibraryBooks.Match("Another Book", "Another Author", Search));
        Assert.Equal("OL3W", OpenLibraryBooks.Match("Name", "Writer",
            """{"docs":[{"title":"Name","author_name":["Writer"],"key":"OL3W"}]}"""));
        Assert.Equal("OL31W", OpenLibraryBooks.Match("name", "writer",
            """{"docs":[{"title":"NAME","author_name":["WRITER"],"key":"/works/OL31W"}]}"""));
        Assert.Equal("the hobbit\nj r r tolkien", OpenLibraryBooks.BookKey("The Hobbit", "J. R. R. Tolkien"));
        Assert.Equal("the hobbit\n", OpenLibraryBooks.BookKey("The Hobbit", null));
        Assert.Contains("title=The%20Hobbit", OpenLibraryBooks.SearchUri("The Hobbit", "Tolkien").AbsoluteUri, StringComparison.Ordinal);
        Assert.Equal("https://openlibrary.org/works/OL123W.json", OpenLibraryBooks.WorkUri("OL123W").AbsoluteUri);
        Assert.Equal("https://openlibrary.org/works/OL123W", OpenLibraryBooks.PageUri("OL123W").AbsoluteUri);
    }

    [Theory]
    [InlineData("{}", null)]
    [InlineData("""{"docs":null}""", null)]
    [InlineData("""{"docs":[null,{},{"title":"X","author_name":["A"],"key":"bad"}]}""", null)]
    [InlineData("""{"docs":[{"title":"X","author_name":["A"],"key":"/works/OLbadW"}]}""", null)]
    [InlineData("""{"docs":[{"title":"X","author_name":["A"],"key":"/works/OL34M"}]}""", null)]
    [InlineData("""{"docs":[{"title":"X","author_name":"A","key":"OL42W"}]}""", null)]
    [InlineData("""{"docs":[{"title":6,"author_name":["A"],"key":"OL42W"}]}""", null)]
    [InlineData("""{"docs":[{"title":"X","author_name":[5,null],"key":"OL42W"}]}""", null)]
    [InlineData("""{"docs":[{"title":"X","author_name":["A"],"key":42}]}""", null)]
    public void SearchRejectsUncertainCandidates(string answer, string? expected) =>
        Assert.Equal(expected, OpenLibraryBooks.Match("X", "A", answer));

    [Fact]
    public void WorkDescriptionsCanBePlainTextOrAValueObject()
    {
        Assert.Equal("A story.", OpenLibraryBooks.Description("""{"description":" A story. "}"""));
        Assert.Equal("Another story.", OpenLibraryBooks.Description("""{"description":{"type":"/type/text","value":" Another story. "}}"""));
        Assert.Null(OpenLibraryBooks.Description("{}"));
        Assert.Null(OpenLibraryBooks.Description("""{"description":{}}"""));
        Assert.Null(OpenLibraryBooks.Description("""{"description":33}"""));
        Assert.Null(OpenLibraryBooks.Description("""{"description":" "}"""));
        Assert.Throws<JsonException>(() => OpenLibraryBooks.Description("{"));
    }

    [Fact]
    public async Task ExactBookFetchRetainsTheSynopsisAndCreditsOffline()
    {
        Answer(OpenLibraryBooks.SearchUri("The Hobbit", "J. R. R. Tolkien"), Search);
        Answer(OpenLibraryBooks.WorkUri("OL123W"), """{"description":{"value":" A young hobbit takes an unexpected journey. "}}""");

        var first = Fetcher;
        Assert.True(await first.FetchBookAsync("The Hobbit", "J. R. R. Tolkien", CancellationToken.None));
        var book = first.CachedBook("The Hobbit", "J. R. R. Tolkien");
        Assert.Equal("OL123W", book?.WorkId);
        Assert.Equal("A young hobbit takes an unexpected journey.", book?.Synopsis);
        Assert.True(book!.Current);
        Assert.Equal(book.ToJson(), Assert.Single(first.CachedBooks()).ToJson());
        Assert.Equal(book.ToJson(), EnrichedBook.FromJson(book.ToJson())!.ToJson());
        Assert.Equal(2, _calls.Count);
        Assert.True(await Fetcher.FetchBookAsync("The Hobbit", "J. R. R. Tolkien", CancellationToken.None));
        Assert.Equal(2, _calls.Count);
    }

    [Fact]
    public async Task AConfirmedMissIsKeptAndBadResponsesCanRetry()
    {
        Answer(OpenLibraryBooks.SearchUri("X", "A"), """{"docs":[]}""");
        var fetcher = Fetcher;
        Assert.True(await fetcher.FetchBookAsync("X", "A", CancellationToken.None));
        Assert.Null(fetcher.CachedBook("X", "A")?.WorkId);
        Assert.True(fetcher.CachedBook("X", "A")!.Current);
        Assert.True(await fetcher.FetchBookAsync("X", "A", CancellationToken.None));

        Assert.False(await fetcher.FetchBookAsync("Missing", "B", CancellationToken.None));
        Answer(OpenLibraryBooks.SearchUri("Broken", "B"), "{");
        Assert.False(await fetcher.FetchBookAsync("Broken", "B", CancellationToken.None));
        Answer(OpenLibraryBooks.SearchUri("Found", "B"), """{"docs":[{"title":"Found","author_name":["B"],"key":"OL77W"}]}""");
        Assert.False(await fetcher.FetchBookAsync("Found", "B", CancellationToken.None));
        Answer(OpenLibraryBooks.WorkUri("OL77W"), """{"description":null}""");
        Assert.True(await fetcher.FetchBookAsync("Found", "B", CancellationToken.None));
        Assert.Null(fetcher.CachedBook("Found", "B")?.Synopsis);
        await Assert.ThrowsAsync<ArgumentException>(() => fetcher.FetchBookAsync("", "B", CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => fetcher.FetchBookAsync("X", " ", CancellationToken.None));
    }

    [Fact]
    public void ABookRecordReadsAsJsonOrIsRejected()
    {
        Assert.Null(EnrichedBook.FromJson("{"));
        Assert.Throws<ArgumentNullException>(() => EnrichedBook.FromJson(null!));
        Assert.False(new EnrichedBook { Title = "Old" }.Current);
        Assert.True(new EnrichedBook { Title = "Current", Schema = EnrichedBook.CurrentSchema }.Current);
    }
}
