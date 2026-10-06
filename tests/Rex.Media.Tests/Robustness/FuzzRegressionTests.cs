using Rex.Media.TestKit;

namespace Rex.Media.Tests.Robustness;

/// <summary>
/// The fuzzing targets on known inputs: every seed, every input the nightly fuzzer kept in
/// tests/fixtures/corpus/&lt;target&gt; (minimised crashes go there once fixed), and degenerate bytes.
/// Each must succeed or be refused cleanly.
/// </summary>
public sealed class FuzzRegressionTests
{
    public static TheoryData<string> Targets => [.. FuzzTargets.All.Keys];

    /// <summary>The seeds for a target and what the fuzzer found for it.</summary>
    private static IEnumerable<string> Inputs(string target)
    {
        var folders = FuzzTargets.All[target].Seeds.Select(seed => $"tests/fixtures/{seed}/").Append($"tests/fixtures/corpus/{target}/");
        return RepoPaths.SourceFiles().Where(path => folders.Any(folder => path.StartsWith(folder, StringComparison.Ordinal)) && !path.EndsWith(".json", StringComparison.Ordinal) && !path.EndsWith(".framemd5", StringComparison.Ordinal));
    }

    [Theory]
    [MemberData(nameof(Targets))]
    [Capability("SEC-01")]
    public void EveryKnownInputSucceedsOrIsRefusedCleanly(string target)
    {
        foreach (var path in Inputs(target))
        {
            FuzzTargets.Run(target, File.ReadAllBytes(RepoPaths.Combine(path)));
        }

        foreach (var degenerate in new byte[][] { [], [0], [0xFF], [0, 0, 1], [1, 0, 0, 0, 0, 0, 0], new byte[64], Enumerable.Repeat((byte)0xFF, 64).ToArray() })
        {
            FuzzTargets.Run(target, degenerate);
        }
    }

    [Fact]
    public void TheDemuxTargetIsSeededFromEveryMediaFixture()
    {
        Assert.Contains(Inputs("demux"), path => path.EndsWith(".mkv", StringComparison.Ordinal));
        Assert.Contains(Inputs("demux"), path => path.EndsWith(".mp3", StringComparison.Ordinal));
    }
}
