using Rex.Media.AppCore.Metadata;

namespace Rex.Media.Tests.AppCore;

/// <summary>"More like this" from the library alone: ranked by shared series, directors, cast and genres.</summary>
public sealed class MoreLikeThisTests
{
    private static EnrichedMovie Movie(int id, string title, string? series = null, string[]? directors = null, string[]? cast = null, string[]? genres = null) => new()
    {
        CatalogueId = id,
        Title = title,
        Collection = series,
        Directors = directors ?? [],
        Cast = [.. (cast ?? []).Select(name => new CastMember(name, null, null))],
        Genres = genres ?? [],
    };

    private static readonly EnrichedMovie Matrix = Movie(1, "The Matrix", "The Matrix series", ["Lana Wachowski"], ["Keanu Reeves", "Carrie-Anne Moss"], ["Science fiction", "Action"]);

    [Fact]
    public void ASharedSeriesOutranksADirectorWhoOutranksAnActorWhoOutranksAGenre()
    {
        var reloaded = Movie(2, "The Matrix Reloaded", "the matrix series");
        var cloud = Movie(3, "Cloud Atlas", directors: ["Lana Wachowski"]);
        var wick = Movie(4, "John Wick", cast: ["keanu reeves"]);
        var alien = Movie(5, "Alien", genres: ["Science fiction"]);
        var notting = Movie(6, "Notting Hill", genres: ["Romance"]);

        var ranked = MoreLikeThis.Rank(Matrix, [("alien", alien), ("notting", notting), ("wick", wick), ("cloud", cloud), ("reloaded", reloaded)]);

        Assert.Equal(["reloaded", "cloud", "wick", "alien"], ranked);
        Assert.Equal(MoreLikeThis.SeriesWeight, MoreLikeThis.Score(Matrix, reloaded));
        Assert.Equal(MoreLikeThis.CastWeight + MoreLikeThis.GenreWeight, MoreLikeThis.Score(Matrix, Movie(7, "Speed", cast: ["Keanu Reeves"], genres: ["Action"])));
        Assert.Equal(0, MoreLikeThis.Score(Movie(8, "A"), Movie(9, "B")));
    }

    [Fact]
    public void TheMovieItselfUnmatchedMoviesAndRepeatsAreLeftOutAndTiesKeepTheLibrarysOrder()
    {
        var first = Movie(2, "Alien", genres: ["Action"]);
        var second = Movie(3, "Aliens", genres: ["Action"]);

        var ranked = MoreLikeThis.Rank(Matrix,
        [
            ("itself", Matrix),
            ("no match", Movie(0, "Unknown", genres: ["Action"])),
            ("first", first),
            ("first again", first),
            ("second", second),
        ]);

        Assert.Equal(["first", "second"], ranked);
        Assert.Equal(["first"], MoreLikeThis.Rank(Matrix, [("first", first), ("second", second)], limit: 1));
        Assert.Empty(MoreLikeThis.Rank(Matrix, [("first", first)], limit: -1));
        Assert.Equal(MoreLikeThis.Limit, MoreLikeThis.Rank(Matrix, [.. Enumerable.Range(10, 20).Select(id => (id, Movie(id, "M", genres: ["Action"])))]).Count);
    }

    [Fact]
    public void NothingIsRankedWithoutAMovieOrALibrary()
    {
        Assert.Throws<ArgumentNullException>(() => MoreLikeThis.Rank<string>(null!, []));
        Assert.Throws<ArgumentNullException>(() => MoreLikeThis.Rank<string>(Matrix, null!));
        Assert.Throws<ArgumentNullException>(() => MoreLikeThis.Score(null!, Matrix));
        Assert.Throws<ArgumentNullException>(() => MoreLikeThis.Score(Matrix, null!));
    }
}
