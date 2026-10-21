using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Rex.Media.TestKit;

namespace Rex.Media.Tests.Repository;

/// <summary>
/// docs/capability-matrix.json is rexplayer's feature list, with one status per platform. A row is
/// "verified" on a platform only while a test that proves it there exists: tests declare what they
/// prove with [Capability], and this class checks the two agree in both directions. It also keeps
/// the readable copies, docs/capability-matrix.md and the parity ledger docs/parity.md, in step;
/// set REXPLAYER_WRITE_DOCS=1 to rewrite them. The ledger is what ADR-0020 calls the one list of
/// differences between the platforms: under the "preview" policy a row may be further along on one
/// platform, under "locked" every must row is done on both or explained, and a release is blocked
/// otherwise.
/// </summary>
public sealed partial class CapabilityMatrixTests
{
    private static readonly string[] Statuses = ["planned", "built", "verified", "verified-hardware", "post-1.0", "not-applicable", "gap"];
    private static readonly string[] Priorities = ["must", "should", "could", "wont", "must/should", "could/wont"];
    private static readonly string[] Policies = ["preview", "locked"];

    /// <summary>What a platform's status must be, under the locked policy, for a must row to count as done there.</summary>
    private static readonly string[] Done = ["verified", "verified-hardware", "not-applicable"];

    /// <summary>Statuses that only make sense with a note saying why.</summary>
    private static readonly string[] Explained = ["not-applicable", "gap"];

    private static readonly Dictionary<string, (string Name, CapabilityPlatform Platform)> KnownPlatforms = new(StringComparer.Ordinal)
    {
        ["windows"] = ("Windows", CapabilityPlatform.Windows),
        ["macos"] = ("macOS", CapabilityPlatform.MacOS),
    };

    private sealed record Row(string Id, string Area, string Name, string Details, string Milestone, string Priority, string Oracles, IReadOnlyDictionary<string, string> Status, string? ParityNote, int? ParityIssue)
    {
        public string On(string platform) => Status[platform];

        public bool IsMust => Priority.StartsWith("must", StringComparison.Ordinal);

        /// <summary>Whether the platforms are at different points, or one of them is explained away.</summary>
        public bool Differs => Status.Values.Distinct(StringComparer.Ordinal).Count() > 1 || Status.Values.Any(status => Explained.Contains(status, StringComparer.Ordinal));
    }

    private sealed record Matrix(string Policy, IReadOnlyList<string> Platforms, List<Row> Rows);

    private sealed record Claim(string Test, string Id, CapabilityPlatform Platform, CapabilityPlatform Project);

    private static Matrix Load()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(RepoPaths.Combine("docs", "capability-matrix.json")));
        var root = document.RootElement;
        var platforms = root.GetProperty("platforms").EnumerateArray().Select(element => element.GetString()!).ToList();
        var rows = root.GetProperty("capabilities").EnumerateArray().Select(element => new Row(
            element.GetProperty("id").GetString()!,
            element.GetProperty("area").GetString()!,
            element.GetProperty("name").GetString()!,
            element.GetProperty("details").GetString()!,
            element.GetProperty("milestone").GetString()!,
            element.GetProperty("priority").GetString()!,
            element.GetProperty("oracles").GetString()!,
            element.GetProperty("status").EnumerateObject().ToDictionary(property => property.Name, property => property.Value.GetString()!, StringComparer.Ordinal),
            element.TryGetProperty("parityNote", out var note) ? note.GetString() : null,
            element.TryGetProperty("parityIssue", out var issue) ? issue.GetInt32() : null)).ToList();
        return new Matrix(root.GetProperty("parityPolicy").GetString()!, platforms, rows);
    }

    /// <summary>
    /// Every [Capability] on a test method in every test project. They are read from the sources so
    /// one portable test can see the platform-specific projects' claims too. A claim's platform is
    /// the one it names, else the one its project runs on (a project with a platform framework
    /// proves nothing anywhere else).
    /// </summary>
    private static List<Claim> Claims()
    {
        var projects = new Dictionary<string, CapabilityPlatform>(StringComparer.Ordinal);
        return RepoPaths.SourceFiles()
            .Where(path => path.StartsWith("tests/", StringComparison.Ordinal) && path.EndsWith(".cs", StringComparison.Ordinal))
            .SelectMany(path =>
            {
                var project = ProjectPlatform(path, projects);
                return ClaimPattern().Matches(File.ReadAllText(RepoPaths.Combine(path))).Select(match => new Claim(
                    $"{Path.GetFileNameWithoutExtension(path)}.{match.Groups["test"].Value}",
                    match.Groups["id"].Value,
                    match.Groups["platform"].Success ? Enum.Parse<CapabilityPlatform>(match.Groups["platform"].Value) : project,
                    project));
            })
            .ToList();
    }

    /// <summary>The platform the test project at tests/{name}/ runs on, from its target framework.</summary>
    private static CapabilityPlatform ProjectPlatform(string path, Dictionary<string, CapabilityPlatform> cache)
    {
        var name = path.Split('/')[1];
        if (!cache.TryGetValue(name, out var platform))
        {
            var project = RepoPaths.Combine("tests", name, name + ".csproj");
            var framework = File.Exists(project) ? XDocument.Load(project).Descendants("TargetFramework").SingleOrDefault()?.Value ?? "" : "";
            platform = framework.Contains("-windows", StringComparison.Ordinal) ? CapabilityPlatform.Windows
                : framework.Contains("-macos", StringComparison.Ordinal) ? CapabilityPlatform.MacOS
                : CapabilityPlatform.All;
            cache[name] = platform;
        }

        return platform;
    }

    private static bool Proves(Claim claim, string platform) =>
        claim.Platform == CapabilityPlatform.All || claim.Platform == KnownPlatforms[platform].Platform;

    /// <summary>A [Capability("ID")] or [Capability("ID", CapabilityPlatform.X)] attribute and, looking ahead so stacked attributes each match, the test method it sits on.</summary>
    [GeneratedRegex(@"^[ \t]*\[Capability\(""(?<id>[A-Z0-9-]+)""(?:,\s*CapabilityPlatform\.(?<platform>\w+))?\)](?=.*?public\s+(?:async\s+Task|void)\s+(?<test>\w+)\s*\()", RegexOptions.Singleline | RegexOptions.Multiline)]
    private static partial Regex ClaimPattern();

    [Fact]
    public void RowsAreUniqueAndUseKnownStatusesAndPriorities()
    {
        var matrix = Load();

        Assert.Contains(matrix.Policy, Policies);
        Assert.Equal(KnownPlatforms.Keys.Order(StringComparer.Ordinal), matrix.Platforms.Order(StringComparer.Ordinal));
        Assert.Equal(matrix.Rows.Count, matrix.Rows.Select(row => row.Id).Distinct(StringComparer.Ordinal).Count());
        Assert.All(matrix.Rows, row => Assert.Equal(matrix.Platforms.Order(StringComparer.Ordinal), row.Status.Keys.Order(StringComparer.Ordinal)));
        Assert.All(matrix.Rows, row => Assert.All(row.Status.Values, status => Assert.Contains(status, Statuses)));
        Assert.All(matrix.Rows, row => Assert.Contains(row.Priority, Priorities));
        Assert.All(matrix.Rows, row => Assert.False(string.IsNullOrWhiteSpace(row.Name)));
    }

    [Fact]
    public void EveryClaimNamesARealRow()
    {
        var ids = Load().Rows.Select(row => row.Id).ToHashSet(StringComparer.Ordinal);

        var unknown = Claims().Where(claim => !ids.Contains(claim.Id)).Select(claim => $"{claim.Test} claims {claim.Id}").ToList();

        Assert.True(unknown.Count == 0, string.Join(Environment.NewLine, unknown));
    }

    [Fact]
    public void AClaimCannotPromiseAPlatformItsProjectNeverRunsOn()
    {
        var overreaching = Claims()
            .Where(claim => claim.Project != CapabilityPlatform.All && claim.Platform != claim.Project)
            .Select(claim => $"{claim.Test} claims {claim.Id} for {claim.Platform} from a {claim.Project}-only project")
            .ToList();

        Assert.True(overreaching.Count == 0, string.Join(Environment.NewLine, overreaching));
    }

    [Fact]
    public void EveryVerifiedRowHasATestThatProvesItOnThatPlatform()
    {
        var matrix = Load();
        var claims = Claims();

        var unproved = matrix.Platforms
            .SelectMany(platform => matrix.Rows
                .Where(row => row.On(platform) == "verified" && !claims.Any(claim => claim.Id == row.Id && Proves(claim, platform)))
                .Select(row => $"{row.Id} on {platform}"))
            .ToList();

        Assert.True(unproved.Count == 0, "Verified without a [Capability] test for that platform: " + string.Join(", ", unproved));
    }

    [Fact]
    public void GapsAndExemptionsAreExplained()
    {
        var problems = new List<string>();
        foreach (var row in Load().Rows)
        {
            var explained = row.Status.Values.Any(status => Explained.Contains(status, StringComparer.Ordinal));
            if (explained && string.IsNullOrWhiteSpace(row.ParityNote))
            {
                problems.Add($"{row.Id} needs a parityNote saying why a platform is not applicable or a gap.");
            }

            if (!explained && row.ParityNote is not null)
            {
                problems.Add($"{row.Id} has a parityNote but no status to explain.");
            }

            if (row.Status.Values.Contains("gap", StringComparer.Ordinal) && row.ParityIssue is not > 0)
            {
                problems.Add($"{row.Id} is a gap without a parityIssue (the GitHub issue that owns closing it).");
            }
        }

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    [Fact]
    public void TheParityPolicyHolds()
    {
        var matrix = Load();
        if (matrix.Policy != "locked")
        {
            return;
        }

        var behind = matrix.Rows
            .Where(row => row.IsMust)
            .Where(row => matrix.Platforms.Any(platform => !Done.Contains(row.On(platform), StringComparer.Ordinal))
                && row.Status.Values.Distinct(StringComparer.Ordinal).Count() > 1)
            .Select(row => $"{row.Id}: " + string.Join(", ", matrix.Platforms.Select(platform => $"{platform} {row.On(platform)}")))
            .ToList();

        Assert.True(behind.Count == 0, "Parity is locked, yet these must rows are further along on one platform: " + string.Join("; ", behind));
    }

    [Fact]
    public void HardwareVerifiedRowsHaveARecordedRun()
    {
        var records = Directory.Exists(RepoPaths.Combine("docs", "hardware-runs"))
            ? string.Join('\n', Directory.GetFiles(RepoPaths.Combine("docs", "hardware-runs"), "*.md").Select(File.ReadAllText))
            : string.Empty;

        var missing = Load().Rows
            .Where(row => row.Status.Values.Contains("verified-hardware", StringComparer.Ordinal) && !records.Contains(row.Id, StringComparison.Ordinal))
            .Select(row => row.Id)
            .ToList();

        Assert.True(missing.Count == 0, "Record a hardware run in docs/hardware-runs for: " + string.Join(", ", missing));
    }

    [Fact]
    public void TheReadableMatrixIsCurrent() => AssertCurrent("capability-matrix.md", Markdown(Load()));

    [Fact]
    public void TheParityLedgerIsCurrent() => AssertCurrent("parity.md", Ledger(Load()));

    private static void AssertCurrent(string file, string expected)
    {
        var path = RepoPaths.Combine("docs", file);
        if (Environment.GetEnvironmentVariable("REXPLAYER_WRITE_DOCS") == "1")
        {
            File.WriteAllText(path, expected);
        }

        Assert.True(File.Exists(path) && File.ReadAllText(path).ReplaceLineEndings("\n") == expected, $"docs/{file} is out of date; run the tests with REXPLAYER_WRITE_DOCS=1.");
    }

    private static string Markdown(Matrix matrix)
    {
        var text = new StringBuilder();
        text.Append("# Capability matrix\n\n");
        text.Append("Everything rexplayer does or will do, generated from `capability-matrix.json`, with a status per platform. A row is\n");
        text.Append("**verified** on a platform only while a test marked with its id passes there; **built** means the code exists but the\n");
        text.Append("row is not fully proved yet; **not-applicable** and **gap** rows are explained in the [parity ledger](parity.md).\n\n");
        foreach (var platform in matrix.Platforms)
        {
            var counts = Statuses.Select(status => (status, count: matrix.Rows.Count(row => row.On(platform) == status))).Where(pair => pair.count > 0).Select(pair => $"{pair.count} {pair.status}");
            text.Append(KnownPlatforms[platform].Name).Append(": ").Append(string.Join(", ", counts)).Append(".\n");
        }

        var heading = string.Join(" | ", matrix.Platforms.Select(platform => KnownPlatforms[platform].Name));
        foreach (var group in matrix.Rows.GroupBy(row => row.Area))
        {
            text.Append("\n## ").Append(group.Key).Append("\n\n");
            text.Append("| ID | Capability | Details | Milestone | Priority | ").Append(heading).Append(" |\n");
            text.Append("|---|---|---|---|---|").Append(string.Concat(matrix.Platforms.Select(_ => "---|"))).Append('\n');
            foreach (var row in group)
            {
                var statuses = string.Join(" | ", matrix.Platforms.Select(row.On));
                text.Append(System.Globalization.CultureInfo.InvariantCulture, $"| {row.Id} | {Cell(row.Name)} | {Cell(row.Details)} | {row.Milestone} | {row.Priority} | {statuses} |\n");
            }
        }

        return text.ToString();
    }

    private static string Ledger(Matrix matrix)
    {
        var text = new StringBuilder();
        var differing = matrix.Rows.Where(row => row.Differs).ToList();
        text.Append("# Parity ledger\n\n");
        text.Append("Every row of the [capability matrix](capability-matrix.md) where the platforms differ, generated from\n");
        text.Append("`capability-matrix.json`. Parity policy: **").Append(matrix.Policy).Append("**. Under *preview* a row may be further along on one\n");
        text.Append("platform while the other catches up; under *locked* every must row is verified on both platforms or marked not\n");
        text.Append("applicable with a reason, and CI blocks a release otherwise (ADR-0020). A **gap** is a difference someone has\n");
        text.Append("accepted for now: its note says why and its issue says who closes it.\n\n");
        text.Append(System.Globalization.CultureInfo.InvariantCulture, $"Totals: {differing.Count} rows differ ({differing.Count(row => row.IsMust)} must), ");
        text.Append(System.Globalization.CultureInfo.InvariantCulture, $"{matrix.Rows.Count(row => row.Status.Values.Contains("not-applicable", StringComparer.Ordinal))} not applicable, ");
        text.Append(System.Globalization.CultureInfo.InvariantCulture, $"{matrix.Rows.Count(row => row.Status.Values.Contains("gap", StringComparer.Ordinal))} gaps.\n");
        if (differing.Count == 0)
        {
            text.Append("\nThe platforms match on every row.\n");
            return text.ToString();
        }

        var heading = string.Join(" | ", matrix.Platforms.Select(platform => KnownPlatforms[platform].Name));
        foreach (var group in differing.GroupBy(row => row.Area))
        {
            text.Append("\n## ").Append(group.Key).Append("\n\n");
            text.Append("| ID | Capability | Priority | ").Append(heading).Append(" | Note |\n");
            text.Append("|---|---|---|").Append(string.Concat(matrix.Platforms.Select(_ => "---|"))).Append("---|\n");
            foreach (var row in group)
            {
                var statuses = string.Join(" | ", matrix.Platforms.Select(row.On));
                var note = row.ParityNote is null ? "" : Cell(row.ParityNote) + (row.ParityIssue is { } issue ? " (#" + issue.ToString(System.Globalization.CultureInfo.InvariantCulture) + ")" : "");
                text.Append(System.Globalization.CultureInfo.InvariantCulture, $"| {row.Id} | {Cell(row.Name)} | {row.Priority} | {statuses} | {note} |\n");
            }
        }

        return text.ToString();
    }

    private static string Cell(string value) => value.Replace("|", "\\|", StringComparison.Ordinal);
}
