using Rex.Media.AppCore.Metadata;

namespace Rex.Media.Tests.AppCore;

/// <summary>Keeping each free service's pace per host, and knowing the answers that ask for fewer requests.</summary>
public sealed class PoliteHostsTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("https://musicbrainz.org/ws/2/release-group/", 1.1)]
    [InlineData("https://www.wikidata.org/w/api.php", 1.0)]
    [InlineData("https://query.wikidata.org/sparql", 1.0)]
    [InlineData("https://en.wikipedia.org/api/rest_v1/page/summary/X", 1.0)]
    [InlineData("https://commons.wikimedia.org/wiki/Special:FilePath/X.jpg", 1.0)]
    [InlineData("https://upload.wikimedia.org/x.jpg", 1.0)]
    [InlineData("https://api.tvmaze.com/search/shows?q=x", null)]
    [InlineData("https://coverartarchive.org/release-group/x/front-500", null)]
    [InlineData("https://notwikipedia.org/x", null)]
    public void EachHostKeepsItsOwnPace(string url, double? seconds) =>
        Assert.Equal(seconds is { } gap ? TimeSpan.FromSeconds(gap) : null, PoliteHosts.IntervalFor(new Uri(url)));

    [Fact]
    public void RequestsToOneHostWaitTheirTurnWhileOtherHostsDoNot()
    {
        var hosts = new PoliteHosts();
        var wikidata = new Uri("https://www.wikidata.org/w/api.php");
        var wikipedia = new Uri("https://en.wikipedia.org/api/rest_v1/page/summary/X");
        var tvmaze = new Uri("https://api.tvmaze.com/shows/1");

        Assert.Equal(TimeSpan.Zero, hosts.Delay(wikidata, Start));
        Assert.Equal(TimeSpan.FromSeconds(1), hosts.Delay(wikidata, Start));
        Assert.Equal(TimeSpan.Zero, hosts.Delay(wikipedia, Start));
        Assert.Equal(TimeSpan.Zero, hosts.Delay(tvmaze, Start));
        Assert.Equal(TimeSpan.Zero, hosts.Delay(tvmaze, Start));
        Assert.Equal(TimeSpan.Zero, hosts.Delay(wikidata, Start.AddSeconds(5)));
        Assert.Throws<ArgumentNullException>(() => PoliteHosts.IntervalFor(null!));
    }

    [Theory]
    [InlineData(429, true)]
    [InlineData(503, true)]
    [InlineData(200, false)]
    [InlineData(404, false)]
    [InlineData(500, false)]
    public void TooManyRequestsAndUnavailableAskForBackoff(int status, bool backsOff) =>
        Assert.Equal(backsOff, PoliteHosts.BacksOff(status));
}
