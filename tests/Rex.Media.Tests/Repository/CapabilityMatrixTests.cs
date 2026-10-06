using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Rex.Media.TestKit;

namespace Rex.Media.Tests.Repository;

/// <summary>
/// docs/capability-matrix.json is rexplayer's feature list. A row is "verified" only while a test
/// that proves it exists: tests declare what they prove with [Capability], and this class checks the
/// two agree in both directions. It also keeps the readable copy, docs/capability-matrix.md, in step;
/// set REXPLAYER_WRITE_DOCS=1 to rewrite it.
/// </summary>
public sealed partial class CapabilityMatrixTests
{
    private static readonly string[] Statuses = ["planned", "built", "verified", "verified-hardware", "post-1.0"];
    private static readonly string[] Priorities = ["must", "should", "could", "wont", "must/should", "could/wont"];

    private static List<Row> Rows()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(RepoPaths.Combine("docs", "capability-matrix.json")));
        return document.RootElement.GetProperty("capabilities").EnumerateArray().Select(element => new Row(
            element.GetProperty("id").GetString()!,
            element.GetProperty("area").GetString()!,
            element.GetProperty("name").GetString()!,
            element.GetProperty("details").GetString()!,
            element.GetProperty("milestone").GetString()!,
            element.GetProperty("priority").GetString()!,
            element.GetProperty("oracles").GetString()!,
            element.GetProperty("status").GetString()!)).ToList();
    }

    /// <summary>
    /// Every [Capability] on a test method in every test project. They are read from the sources so
    /// one portable test can see the Windows-only projects' claims too.
    /// </summary>
    private static List<(string Test, string Id)> Claims() => RepoPaths.SourceFiles()
        .Where(path => path.StartsWith("tests/", StringComparison.Ordinal) && path.EndsWith(".cs", StringComparison.Ordinal))
        .SelectMany(path => ClaimPattern().Matches(File.ReadAllText(RepoPaths.Combine(path)))
            .Select(match => ($"{Path.GetFileNameWithoutExtension(path)}.{match.Groups["test"].Value}", match.Groups["id"].Value)))
        .ToList();

    /// <summary>A [Capability("ID")] attribute and, looking ahead so stacked attributes each match, the test method it sits on.</summary>
    [GeneratedRegex(@"^[ \t]*\[Capability\(""(?<id>[A-Z0-9-]+)""\)](?=.*?public\s+(?:async\s+Task|void)\s+(?<test>\w+)\s*\()", RegexOptions.Singleline | RegexOptions.Multiline)]
    private static partial Regex ClaimPattern();

    [Fact]
    public void RowsAreUniqueAndUseKnownStatusesAndPriorities()
    {
        var rows = Rows();

        Assert.Equal(rows.Count, rows.Select(row => row.Id).Distinct(StringComparer.Ordinal).Count());
        Assert.All(rows, row => Assert.Contains(row.Status, Statuses));
        Assert.All(rows, row => Assert.Contains(row.Priority, Priorities));
        Assert.All(rows, row => Assert.False(string.IsNullOrWhiteSpace(row.Name)));
    }

    [Fact]
    public void EveryClaimNamesARealRow()
    {
        var ids = Rows().Select(row => row.Id).ToHashSet(StringComparer.Ordinal);

        var unknown = Claims().Where(claim => !ids.Contains(claim.Id)).Select(claim => $"{claim.Test} claims {claim.Id}").ToList();

        Assert.True(unknown.Count == 0, string.Join(Environment.NewLine, unknown));
    }

    [Fact]
    public void EveryVerifiedRowHasATestThatProvesIt()
    {
        var claimed = Claims().Select(claim => claim.Id).ToHashSet(StringComparer.Ordinal);

        var unproved = Rows().Where(row => row.Status == "verified" && !claimed.Contains(row.Id)).Select(row => row.Id).ToList();

        Assert.True(unproved.Count == 0, "Verified without a [Capability] test: " + string.Join(", ", unproved));
    }

    [Fact]
    public void HardwareVerifiedRowsHaveARecordedRun()
    {
        var records = Directory.Exists(RepoPaths.Combine("docs", "hardware-runs"))
            ? string.Join('\n', Directory.GetFiles(RepoPaths.Combine("docs", "hardware-runs"), "*.md").Select(File.ReadAllText))
            : string.Empty;

        var missing = Rows().Where(row => row.Status == "verified-hardware" && !records.Contains(row.Id, StringComparison.Ordinal)).Select(row => row.Id).ToList();

        Assert.True(missing.Count == 0, "Record a hardware run in docs/hardware-runs for: " + string.Join(", ", missing));
    }

    [Fact]
    public void TheReadableMatrixIsCurrent()
    {
        var expected = Markdown(Rows());
        var path = RepoPaths.Combine("docs", "capability-matrix.md");
        if (Environment.GetEnvironmentVariable("REXPLAYER_WRITE_DOCS") == "1")
        {
            File.WriteAllText(path, expected);
        }

        Assert.True(File.Exists(path) && File.ReadAllText(path).ReplaceLineEndings("\n") == expected, "docs/capability-matrix.md is out of date; run the tests with REXPLAYER_WRITE_DOCS=1.");
    }

    private static string Markdown(List<Row> rows)
    {
        var text = new StringBuilder();
        text.Append("# Capability matrix\n\n");
        text.Append("Everything rexplayer does or will do, generated from `capability-matrix.json`. A row is **verified** only while a test\n");
        text.Append("marked with its id passes; **built** means the code exists but the row is not fully proved yet.\n\n");
        var counts = Statuses.Select(status => $"{rows.Count(row => row.Status == status)} {status}");
        text.Append("Totals: ").Append(string.Join(", ", counts)).Append(".\n");
        foreach (var group in rows.GroupBy(row => row.Area))
        {
            text.Append("\n## ").Append(group.Key).Append("\n\n");
            text.Append("| ID | Capability | Details | Milestone | Priority | Status |\n");
            text.Append("|---|---|---|---|---|---|\n");
            foreach (var row in group)
            {
                text.Append(System.Globalization.CultureInfo.InvariantCulture, $"| {row.Id} | {Cell(row.Name)} | {Cell(row.Details)} | {row.Milestone} | {row.Priority} | {row.Status} |\n");
            }
        }

        return text.ToString();
    }

    private static string Cell(string value) => value.Replace("|", "\\|", StringComparison.Ordinal);

    private sealed record Row(string Id, string Area, string Name, string Details, string Milestone, string Priority, string Oracles, string Status);
}
