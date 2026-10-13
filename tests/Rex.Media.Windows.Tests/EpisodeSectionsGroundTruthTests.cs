using System.Text.Json;
using Rex.Media.AppCore;
using Rex.Media.AppCore.Player;
using Rex.Media.Codecs.MediaFoundation;

namespace Rex.Media.Windows.Tests;

/// <summary>
/// Intro and credits detection on real seasons, against times found by hand from the pictures
/// (Fixtures/episode-sections.json: times only, no media). The seasons are the owner's own files, so
/// the test runs only where REXPLAYER_SECTION_MEDIA names the folder that holds them and skips
/// elsewhere. Analyses are kept in the player's section cache, so a second run takes seconds.
/// </summary>
public sealed class EpisodeSectionsGroundTruthTests
{
    private sealed record Expected(string Show, string Episode, double? IntroStart, double? IntroEnd,
        double CreditsStart, double CreditsEnd);

    private sealed record Fixture(double Tolerance, Expected[] Episodes);

    private static readonly string? Media = Environment.GetEnvironmentVariable("REXPLAYER_SECTION_MEDIA") is { Length: > 0 } chosen
        ? chosen
        : null;

    private static readonly string DataRoot = Environment.GetEnvironmentVariable("REXPLAYER_ROOT") is { Length: > 0 } root
        ? root
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "REX", "rexplayer");

    public static TheoryData<string> Episodes()
    {
        var data = new TheoryData<string>();
        foreach (var episode in Load().Episodes)
        {
            data.Add(episode.Show + " " + episode.Episode);
        }

        return data;
    }

    private static Fixture Load()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "episode-sections.json")));
        var root = document.RootElement;
        static double? Time(JsonElement episode, string name) =>
            episode.GetProperty(name).ValueKind == JsonValueKind.Null ? null : episode.GetProperty(name).GetDouble();
        return new Fixture(root.GetProperty("tolerance").GetDouble(), [.. root.GetProperty("episodes").EnumerateArray().Select(episode =>
            new Expected(episode.GetProperty("show").GetString()!, episode.GetProperty("episode").GetString()!,
                Time(episode, "introStart"), Time(episode, "introEnd"),
                episode.GetProperty("creditsStart").GetDouble(), episode.GetProperty("creditsEnd").GetDouble()))]);
    }

    [Theory]
    [MemberData(nameof(Episodes))]
    public void DetectionMatchesTheHandCheckedTimes(string name)
    {
        var fixture = Load();
        var expected = fixture.Episodes.Single(episode => episode.Show + " " + episode.Episode == name);
        var path = Media is not null && Directory.Exists(Media)
            ? Directory.EnumerateFiles(Media, "*", SearchOption.AllDirectories).FirstOrDefault(file =>
                file.Contains(expected.Show, StringComparison.OrdinalIgnoreCase) &&
                Path.GetFileName(file).Contains(expected.Episode, StringComparison.OrdinalIgnoreCase) &&
                EpisodeGrouping.Identify(file) is not null)
            : null;
        if (path is null)
        {
            Assert.Skip($"Set REXPLAYER_SECTION_MEDIA to the folder holding {name}.");
        }

        var decoders = MediaRegistries.Decoders(new MfDecoderFactory());
        var cache = new SectionAnalysisCache(Path.Combine(DataRoot, "cache", "sections"));
        EpisodeAnalysis Analysis(string file)
        {
            var signature = SectionAnalysis.SignatureOf(file);
            if (cache.Get(file, signature) is { } kept)
            {
                return kept;
            }

            var made = SectionAnalysis.Analyze(file, decoders, TestContext.Current.CancellationToken);
            cache.Set(made);
            return made;
        }

        var siblings = EpisodeGrouping.Siblings(path, Directory.EnumerateFiles(Path.GetDirectoryName(path)!), SectionDetector.SiblingsCompared)
            .Select(Analysis).ToArray();
        var own = Analysis(path);
        var found = SectionDetector.Detect(own, siblings);
        var tolerance = fixture.Tolerance;

        var intro = found.Best(EpisodeSectionKind.Intro, own.Duration);
        if (expected.IntroStart is { } introStart && expected.IntroEnd is { } introEnd)
        {
            Assert.NotNull(intro);
            Assert.InRange(intro.Start.TotalSeconds, introStart - tolerance, introStart + tolerance);
            Assert.InRange(intro.End.TotalSeconds, introEnd - tolerance, introEnd + tolerance);
        }
        else
        {
            // Nothing worth skipping at the start: offering a skip there would skip the story.
            Assert.Null(intro);
        }

        var credits = found.Best(EpisodeSectionKind.Credits, own.Duration);
        Assert.NotNull(credits);
        Assert.InRange(credits.Start.TotalSeconds, expected.CreditsStart - tolerance, expected.CreditsStart + tolerance);
        Assert.InRange(credits.End.TotalSeconds, expected.CreditsEnd - tolerance, expected.CreditsEnd + tolerance);
    }
}
