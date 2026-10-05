using System.Text.RegularExpressions;
using System.Xml.Linq;
using Rex.Media.TestKit;

namespace Rex.Media.Tests.Repository;

/// <summary>
/// Rules about the repository itself. Each one exists because breaking it would be easy and
/// silent: a name the project must never carry, a file that grows past the point anyone reads it,
/// a parser with no specification behind it, a script nobody reviews.
/// </summary>
public sealed partial class RepositoryTests
{
    private static readonly string[] SourceExtensions =
    [
        ".cs", ".xaml", ".ps1", ".py", ".js", ".mjs", ".css", ".html", ".md", ".yml", ".yaml",
        ".json", ".iss", ".hlsl", ".csproj", ".props", ".targets", ".slnx", ".svg",
    ];

    /// <summary>Script files a reviewer has read. Anything else that executes is a surprise.</summary>
    private static readonly string[] AllowedScripts =
    [
        "dev.ps1",
        "installer/build.ps1",
        "installer/prepare-upgrade.ps1",
        "scripts/check_site.py",
        "scripts/make-fixtures.ps1",
        "scripts/make-icon.ps1",
        "site/app.js",
        "tests/site/app.test.mjs",
        "tests/site/pages.spec.js",
        "tests/site/serve.py",
        "tests/site/test_check_site.py",
        "playwright.config.js",
    ];

    /// <summary>Folders whose every source file implements a published format and must say which.</summary>
    private static readonly string[] SpecificationFolders =
    [
        "src/Rex.Media.Containers/",
        "src/Rex.Media.Codecs.Software/",
        "src/Rex.Media.Subtitles/",
        "src/Rex.Media.Net/",
        "src/Rex.Media.Cast/",
        "src/Rex.Media.Discs/",
    ];

    [Fact]
    public void NoTrackedFileNamesTheBannedProducts()
    {
        var problems = new List<string>();
        foreach (var path in RepoPaths.SourceFiles())
        {
            foreach (var word in Lexicon.Find(path))
            {
                problems.Add($"{path}: the path contains a banned word ({word.Length} letters)");
            }

            if (!Lexicon.IsCheckedText(path))
            {
                continue;
            }

            var text = File.ReadAllText(RepoPaths.Combine(path));
            foreach (var word in Lexicon.Find(text))
            {
                problems.Add($"{path}: the text contains a banned word ({word.Length} letters)");
            }
        }

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    [Theory]
    [InlineData("plain words", false)]
    [InlineData("a valcano and a velocity", false)]
    [InlineData("libXYZ", true)]
    [InlineData("FooXyzBar", true)]
    [InlineData("XYZ", true)]
    [InlineData("the XYZ player", true)]
    [InlineData("PRODUCER-ORG", true)]
    [InlineData("producerorg.org", true)]
    public void TheLexiconCatchesEveryCasingAndIdentifier(string sample, bool expected)
    {
        var text = sample
            .Replace("XYZ", Lexicon.Banned[0].ToUpperInvariant(), StringComparison.Ordinal)
            .Replace("Xyz", char.ToUpperInvariant(Lexicon.Banned[0][0]) + Lexicon.Banned[0][1..], StringComparison.Ordinal)
            .Replace("PRODUCER-ORG", Lexicon.Banned[1].ToUpperInvariant(), StringComparison.Ordinal)
            .Replace("producerorg", Lexicon.Banned[1], StringComparison.Ordinal);

        Assert.Equal(expected, Lexicon.Find(text).Any());
    }

    [Theory]
    [InlineData("site/index.html", true)]
    [InlineData("tests/fixtures/media/wav/pcm/clip.wav", false)]
    [InlineData("package-lock.json", false)]
    [InlineData("assets/rexplayer.ico", false)]
    public void TheLexiconSkipsOnlyBinariesAndTheLockfile(string path, bool checkedText)
    {
        Assert.Equal(checkedText, Lexicon.IsCheckedText(path));
    }

    [Fact]
    public void NoSourceFileExceedsOneThousandLines()
    {
        var tooLong = SourceFiles()
            .Where(path => !path.StartsWith("docs/capability-matrix", StringComparison.Ordinal))
            .Select(path => (path, lines: File.ReadLines(RepoPaths.Combine(path)).Count()))
            .Where(file => file.lines > 1000)
            .Select(file => $"{file.path}: {file.lines} lines")
            .ToList();

        Assert.True(tooLong.Count == 0, "Split these files by area:" + Environment.NewLine + string.Join(Environment.NewLine, tooLong));
    }

    [Fact]
    public void NoFileHardCodesSomeonesHomeFolder()
    {
        var offenders = SourceFiles()
            .Where(path => !path.Equals("tests/Rex.Media.Tests/Repository/RepositoryTests.cs", StringComparison.Ordinal))
            .Where(path => UserPath().IsMatch(File.ReadAllText(RepoPaths.Combine(path))))
            .ToList();

        Assert.True(offenders.Count == 0, "Use an environment variable or a relative path in: " + string.Join(", ", offenders));
    }

    [Fact]
    public void OnlyReviewedScriptsExist()
    {
        var scripts = RepoPaths.SourceFiles()
            .Where(path => path.EndsWith(".ps1", StringComparison.Ordinal)
                || path.EndsWith(".py", StringComparison.Ordinal)
                || path.EndsWith(".mjs", StringComparison.Ordinal)
                || path.EndsWith(".js", StringComparison.Ordinal)
                || path.EndsWith(".bat", StringComparison.Ordinal)
                || path.EndsWith(".cmd", StringComparison.Ordinal)
                || path.EndsWith(".sh", StringComparison.Ordinal))
            .Where(path => !AllowedScripts.Contains(path, StringComparer.Ordinal))
            .ToList();

        Assert.True(scripts.Count == 0, "Add these to the allowlist in RepositoryTests once reviewed: " + string.Join(", ", scripts));
    }

    [Fact]
    public void EveryFormatImplementationCitesItsSpecification()
    {
        var missing = RepoPaths.SourceFiles()
            .Where(path => path.EndsWith(".cs", StringComparison.Ordinal))
            .Where(path => SpecificationFolders.Any(folder => path.StartsWith(folder, StringComparison.Ordinal)))
            .Where(path => !File.ReadLines(RepoPaths.Combine(path)).Take(30).Any(line => line.TrimStart().StartsWith("// Spec:", StringComparison.Ordinal)))
            .ToList();

        Assert.True(missing.Count == 0, "Add a '// Spec:' line naming the standard and clause to: " + string.Join(", ", missing));
    }

    [Fact]
    public void UnsafeCodeLivesOnlyInInteropAndListedKernels()
    {
        var allowed = File.ReadAllLines(RepoPaths.Combine("tests", "unsafe-allowlist.txt"))
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith('#'))
            .Select(line => line.Split(' ', 2)[0])
            .ToHashSet(StringComparer.Ordinal);
        var offenders = RepoPaths.SourceFiles()
            .Where(path => path.StartsWith("src/", StringComparison.Ordinal) && path.EndsWith(".cs", StringComparison.Ordinal))
            .Where(path => !path.StartsWith("src/Rex.Media.Interop/", StringComparison.Ordinal))
            .Where(path => !allowed.Contains(path))
            .Where(path => UnsafeKeyword().IsMatch(File.ReadAllText(RepoPaths.Combine(path))))
            .ToList();

        Assert.True(offenders.Count == 0, "Move unsafe code to Rex.Media.Interop, or list the kernel with its bounds argument in tests/unsafe-allowlist.txt: " + string.Join(", ", offenders));
    }

    [Fact]
    public void NativeCallsLiveOnlyInInterop()
    {
        var offenders = RepoPaths.SourceFiles()
            .Where(path => path.StartsWith("src/", StringComparison.Ordinal) && path.EndsWith(".cs", StringComparison.Ordinal))
            .Where(path => !path.StartsWith("src/Rex.Media.Interop/", StringComparison.Ordinal))
            .Where(path => NativeImport().IsMatch(File.ReadAllText(RepoPaths.Combine(path))))
            .ToList();

        Assert.True(offenders.Count == 0, "Declare native entry points in Rex.Media.Interop: " + string.Join(", ", offenders));
    }

    [Fact]
    public void NoWorkIsLeftUnticketed()
    {
        var offenders = SourceFiles()
            .Where(path => path.StartsWith("src/", StringComparison.Ordinal) || path.StartsWith("tests/", StringComparison.Ordinal))
            .Where(path => !path.Equals("tests/Rex.Media.Tests/Repository/RepositoryTests.cs", StringComparison.Ordinal))
            .Where(path => UnticketedMarker().IsMatch(File.ReadAllText(RepoPaths.Combine(path))))
            .ToList();

        Assert.True(offenders.Count == 0, "Name an issue (TODO(#12): ...) or finish the work in: " + string.Join(", ", offenders));
    }

    [Fact]
    public void TheNewestChangelogEntryIsTheBuildVersion()
    {
        var version = XDocument.Load(RepoPaths.Combine("Directory.Build.props")).Descendants("Version").Single().Value;
        var headings = File.ReadAllLines(RepoPaths.Combine("CHANGELOG.md"))
            .Where(line => line.StartsWith("## ", StringComparison.Ordinal))
            .Select(line => ChangelogHeading().Match(line))
            .ToList();

        Assert.NotEmpty(headings);
        Assert.All(headings, heading => Assert.True(heading.Success, "Headings read '## X.Y.Z - YYYY-MM-DD'."));
        Assert.Equal(version, headings[0].Groups["version"].Value);
        var versions = headings.Select(h => Version.Parse(h.Groups["version"].Value)).ToList();
        Assert.Equal(versions.OrderDescending(), versions);
    }

    [Fact]
    public void ContinuousDeliveryNeverRewritesARelease()
    {
        foreach (var workflow in new[] { ".github/workflows/ci.yml", ".github/workflows/release.yml" })
        {
            var text = File.ReadAllText(RepoPaths.Combine(workflow));
            Assert.DoesNotContain("--clobber", text, StringComparison.Ordinal);
            Assert.DoesNotContain("gh release upload", text, StringComparison.Ordinal);
            Assert.Contains("gh release create", text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ProductPackagesAreOnlyTheOnesTheDependencyRecordAllows()
    {
        var packages = XDocument.Load(RepoPaths.Combine("Directory.Packages.props"));
        var product = packages.Descendants("ItemGroup")
            .Single(group => (string?)group.Attribute("Label") == "Product")
            .Elements("PackageVersion")
            .Select(element => (string)element.Attribute("Include")!)
            .Order(StringComparer.Ordinal);

        Assert.Equal(["Microsoft.Windows.CsWin32", "Microsoft.WindowsAppSDK"], product);

        var testOnly = packages.Descendants("ItemGroup")
            .Single(group => (string?)group.Attribute("Label") == "Test")
            .Elements("PackageVersion")
            .Select(element => (string)element.Attribute("Include")!)
            .ToHashSet(StringComparer.Ordinal);
        var leaks = RepoPaths.SourceFiles()
            .Where(path => path.StartsWith("src/", StringComparison.Ordinal) && path.EndsWith(".csproj", StringComparison.Ordinal))
            .SelectMany(path => XDocument.Load(RepoPaths.Combine(path)).Descendants("PackageReference").Select(reference => (path, package: (string)reference.Attribute("Include")!)))
            .Where(item => testOnly.Contains(item.package))
            .Select(item => $"{item.path} uses {item.package}")
            .ToList();

        Assert.True(leaks.Count == 0, string.Join(Environment.NewLine, leaks));
    }

    [Fact]
    public void EveryPortableSourceFileIsOnTheCoverageList()
    {
        var listed = File.ReadAllLines(RepoPaths.Combine("tests", "coverage-required.txt"))
            .Concat(File.ReadAllLines(RepoPaths.Combine("tests", "coverage-exempt.txt")))
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith('#'))
            .Select(line => line.Split(' ', 2)[0])
            .ToHashSet(StringComparer.Ordinal);
        var portableProjects = RepoPaths.SourceFiles()
            .Where(path => path.StartsWith("src/", StringComparison.Ordinal) && path.EndsWith(".csproj", StringComparison.Ordinal))
            .Where(path => !XDocument.Load(RepoPaths.Combine(path)).Descendants("TargetFramework").Any())
            .Select(path => path[..(path.LastIndexOf('/') + 1)])
            .ToList();

        var missing = RepoPaths.SourceFiles()
            .Where(path => path.EndsWith(".cs", StringComparison.Ordinal) && portableProjects.Any(project => path.StartsWith(project, StringComparison.Ordinal)))
            .Select(Path.GetFileName)
            .Where(name => !listed.Contains(name!))
            .ToList();

        Assert.True(missing.Count == 0, "Add these to tests/coverage-required.txt: " + string.Join(", ", missing));
    }

    private static IEnumerable<string> SourceFiles() =>
        RepoPaths.SourceFiles().Where(path => SourceExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase));

    [GeneratedRegex(@"[A-Z]:\\Users\\|/home/\w+|/Users/\w+/")]
    private static partial Regex UserPath();

    [GeneratedRegex(@"\bunsafe\b")]
    private static partial Regex UnsafeKeyword();

    [GeneratedRegex(@"\[(LibraryImport|DllImport|ComImport)\b")]
    private static partial Regex NativeImport();

    [GeneratedRegex(@"\b(TODO|FIXME|HACK)\b(?!\(#\d+\))")]
    private static partial Regex UnticketedMarker();

    [GeneratedRegex(@"^## (?<version>\d+\.\d+\.\d+) - \d{4}-\d{2}-\d{2}$")]
    private static partial Regex ChangelogHeading();
}
