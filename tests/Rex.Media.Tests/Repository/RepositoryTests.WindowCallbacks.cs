using System.Text.RegularExpressions;
using Rex.Media.TestKit;

namespace Rex.Media.Tests.Repository;

public sealed partial class RepositoryTests
{
    /// <summary>
    /// Work Windows calls back on the window's thread (a clock's tick, an action queued from
    /// another thread) goes through the window's guard: an exception escaping it ends the process
    /// without reaching App. And no task is dropped unobserved: one that fails is written down with
    /// its stack by MainWindow.Forget instead of vanishing.
    /// </summary>
    [Fact]
    public void TheWindowGuardsEveryCallbackAndObservesEveryTaskItLetsRun()
    {
        var offenders = RepoPaths.SourceFiles()
            .Where(path => path.StartsWith("src/Rex.Media.App/", StringComparison.Ordinal) && path.EndsWith(".cs", StringComparison.Ordinal))
            .SelectMany(path => File.ReadLines(RepoPaths.Combine(path)).Select((line, index) => (path, line, number: index + 1)))
            .Where(found => DroppedTask().IsMatch(found.line)
                || (found.line.Contains(".Tick +=", StringComparison.Ordinal) && !found.line.Contains("OnWindowCallback", StringComparison.Ordinal)))
            .Select(found => $"{found.path}:{found.number}: {found.line.Trim()}")
            .ToList();

        Assert.True(offenders.Count == 0, "Use OnTick for a clock and Forget for a task left to run:" + Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }

    [GeneratedRegex(@"_ = (\w+Async|Task\.Run)\(")]
    private static partial Regex DroppedTask();
}
