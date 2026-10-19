using Rex.Media.AppCore.Metadata;

namespace Rex.Media.Tests.AppCore;

/// <summary>
/// Matching a name from the disk to the catalogue's candidates: exact names win, a year from the
/// file must agree, a lone near-exact reading is taken, and anything doubtful stays unmatched.
/// </summary>
public sealed class MetadataMatchTests
{
    private static ShowCandidate Show(int id, string name, int? year = null) => new(id, name, year, null, [], null, null);

    [Theory]
    [InlineData("The Show!", "show")]
    [InlineData("Show", "show")]
    [InlineData("S.H.O.W.", "s h o w")]
    [InlineData("  Family-Guy (2019) ", "family guy 2019")]
    [InlineData("---", "")]
    public void NamesNormalizeToWhatIdentifiesThem(string name, string normalized) => Assert.Equal(normalized, MetadataMatch.Normalize(name));

    [Fact]
    public void AnExactNameWinsAndTheYearDecidesBetweenRevivals()
    {
        var old = Show(1, "The Show", 1984);
        var revival = Show(2, "The Show", 2019);
        var other = Show(3, "The Show Abroad", 2019);

        Assert.Equal(old, MetadataMatch.Pick("The Show", null, [old, revival, other]));
        Assert.Equal(revival, MetadataMatch.Pick("The Show", 2019, [old, revival, other]));
        Assert.Equal(old, MetadataMatch.Pick("show", 1984, [old, revival, other]));
        // With a year, a dated exact candidate beats an undated one.
        Assert.Equal(revival, MetadataMatch.Pick("The Show", 2019, [Show(4, "The Show"), revival]));
        Assert.Equal(4, MetadataMatch.Pick("The Show", 2019, [Show(4, "The Show"), old])?.Id);
    }

    [Fact]
    public void ALoneNearExactReadingIsTakenAndADoubtfulOneIsNot()
    {
        Assert.Equal(1, MetadataMatch.Pick("The Show", null, [Show(1, "The Show (2019)"), Show(2, "Elsewhere")])?.Id);
        Assert.Equal(1, MetadataMatch.Pick("The Show (2019)", null, [Show(1, "The Show")])?.Id);
        Assert.Null(MetadataMatch.Pick("The Show", null, [Show(1, "The Show (2019)"), Show(2, "The Show Abroad")]));
        Assert.Null(MetadataMatch.Pick("The Show", null, [Show(1, "Some Other Thing")]));
        Assert.Null(MetadataMatch.Pick("The Show", 2019, [Show(1, "The Show Abroad", 1984)]));
        Assert.Null(MetadataMatch.Pick("", null, [Show(1, "The Show")]));
        Assert.Null(MetadataMatch.Pick("The Show", null, []));
        Assert.Throws<ArgumentNullException>(() => MetadataMatch.Pick("x", null, null!));
        Assert.Throws<ArgumentNullException>(() => MetadataMatch.Normalize(null!));
    }

    [Fact]
    public void CandidatesDescribeThemselvesBriefly()
    {
        Assert.Equal("The Show (2019) — Short.", MetadataMatch.Describe(Show(1, "The Show", 2019) with { Summary = "Short." }));
        Assert.Equal("The Show", MetadataMatch.Describe(Show(1, "The Show")));
        var longSummary = new string('a', 200);
        var described = MetadataMatch.Describe(Show(1, "The Show") with { Summary = longSummary });
        Assert.EndsWith("…", described);
        Assert.True(described.Length < 140);
        Assert.Throws<ArgumentNullException>(() => MetadataMatch.Describe(null!));
    }
}
