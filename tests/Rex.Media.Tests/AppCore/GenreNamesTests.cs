using Rex.Media.AppCore.Library;
using Rex.Media.Containers.Tags;
using Rex.Media.Library;

namespace Rex.Media.Tests.AppCore;

/// <summary>Genre tags that name several genres, spell one genre several ways, or number it.</summary>
public sealed class GenreNamesTests
{
    [Theory]
    [InlineData("Dance;Pop", new[] { "Dance", "Pop" })]
    [InlineData("Alternativo;Rock", new[] { "Alternative", "Rock" })]
    [InlineData("Electro;Techno/House", new[] { "Electro", "Techno", "House" })]
    [InlineData("Pop;R&B", new[] { "Pop", "R&B" })]
    [InlineData("Pop;Singer & Songwriter", new[] { "Pop", "Singer-Songwriter" })]
    [InlineData("Rap/Hip Hop", new[] { "Rap", "Hip-Hop" })]
    [InlineData("Pop, Rock", new[] { "Pop", "Rock" })]
    [InlineData("Afrobeats; Pop", new[] { "Afrobeats", "Pop" })]
    [InlineData("Pop|Dance\\House", new[] { "Pop", "Dance", "House" })]
    [InlineData("Hip-Hop • Rap", new[] { "Hip-Hop", "Rap" })]
    [InlineData("Jazz · Funk", new[] { "Jazz", "Funk" })]
    [InlineData("Dance\nPop", new[] { "Dance", "Pop" })]
    [InlineData("Alt Rock;Neo-Soul", new[] { "Alternative Rock", "Neo Soul" })]
    public void SeveralGenresInOneTagAreEachTheirOwn(string field, string[] expected) =>
        Assert.Equal(expected, GenreNames.Split(field));

    [Theory]
    [InlineData("Hip Hop", "Hip-Hop")]
    [InlineData("hiphop", "Hip-Hop")]
    [InlineData("RnB", "R&B")]
    [InlineData("Rhythm and Blues", "R&B")]
    [InlineData("R and B", "R&B")]
    [InlineData("electronica", "Electronic")]
    [InlineData("Singer/Songwriter", "Singer-Songwriter")]
    [InlineData("drum n bass", "Drum & Bass")]
    [InlineData("k-pop", "K-Pop")]
    [InlineData("Latino", "Latin")]
    [InlineData("(13)", "Pop")]
    [InlineData("17", "Rock")]
    [InlineData("Pop/Funk", "Pop/Funk")]
    [InlineData("alt. rock", "Alternative Rock")]
    [InlineData("lo fi", "Lo-Fi")]
    [InlineData("Contemporary RnB", "Contemporary R&B")]
    public void OneGenreIsSpelledOneWay(string field, string expected) =>
        Assert.Equal([expected], GenreNames.Split(field));

    [Fact]
    public void UnknownNamesAreKeptAsWrittenAndNotBrokenApart()
    {
        Assert.Equal(["Música asiática"], GenreNames.Split("Música  asiática"));
        // Not every piece is a genre, so the slash and the comma are part of the name.
        Assert.Equal(["AC/DC Tribute"], GenreNames.Split("AC/DC Tribute"));
        Assert.Equal(["Folk, World, & Country"], GenreNames.Split("Folk, World, & Country"));
    }

    [Fact]
    public void AGenreWithBrokenUnicodeRemainsAsTagged()
    {
        var broken = "Unrecognized" + (char)0xD800;
        Assert.Equal([broken], GenreNames.Split(broken));
    }

    [Fact]
    public void CorruptControlOnlyGenresCannotCreateInvisibleHeadings()
    {
        Assert.Equal(["Pop"], GenreNames.Split("Pop;\u0001;\uFFFD;???"));
        Assert.Equal(["Dance", "Música asiática", "音楽", "Afrobeats"],
            GenreNames.Split("\u0002Dance; Música  asiática;音楽;Afrobeats"));
        Assert.Equal(["Unknown"], GenreNames.Split("Unknown\u001B"));
        Assert.Empty(GenreNames.Split("\u0001;\uFFFD;!!!"));
    }

    [Fact]
    public void SupplementaryScriptGenresKeepTheirIdentityWhenDeduplicated()
    {
        // Two distinct Deseret names must not both collapse to the empty key.
        Assert.Equal(["𐐨𐐩", "𐐪𐐫"], GenreNames.Split("𐐨𐐩;𐐪𐐫"));
        Assert.Single(GenreNames.Split("𐐨𐐩;𐐀𐐁"));
        Assert.Equal(["𐐨𐐩", "Jazz"], GenreNames.Split("𐐨𐐩;Jazz"));
    }

    [Fact]
    public void EachGenreAppearsOnceAndEmptyTagsNameNone()
    {
        Assert.Equal(["Pop", "Dance"], GenreNames.Split("Pop; pop ;Dance;POP"));
        Assert.Equal(["Hip-Hop"], GenreNames.Split("Hip Hop;Hip-Hop;HipHop"));
        Assert.Empty(GenreNames.Split(null));
        Assert.Empty(GenreNames.Split("  ;  ; "));
        Assert.Equal("Dance", GenreNames.Primary("Dance;Pop"));
        Assert.Null(GenreNames.Primary(""));
        Assert.Equal("Blues", Id3v1.GenreNames[0]);
    }

    [Fact]
    public void ASongWithSeveralGenresAppearsUnderEachInTheLibrary()
    {
        static LibraryEntry Song(string title, string? genre) =>
            new() { Path = $@"D:\Music\{title}.mp3", Kind = LibraryKind.Music, Title = title, Genre = genre };
        var songs = new[] { Song("One", "Dance;Pop"), Song("Two", "pop"), Song("Three", "Rap/Hip Hop"), Song("Four", null) };

        var genres = LibraryViews.Genres(songs);

        Assert.Equal(["Dance", "Hip-Hop", "Pop", "Rap", LibraryViews.UnknownGenre], genres.Select(genre => genre.Name));
        Assert.Equal(["One", "Two"], genres[2].Entries.Select(entry => entry.Title));
        Assert.Equal(["Four"], genres[4].Entries.Select(entry => entry.Title));
    }
}
