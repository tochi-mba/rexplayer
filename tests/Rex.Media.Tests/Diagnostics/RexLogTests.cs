using System.Reflection;
using Rex.Media.Diagnostics;

namespace Rex.Media.Tests.Diagnostics;

public sealed class RexLogTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "rexplayer-log-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public void EntriesBelowTheMinimumAreDroppedExceptCritical()
    {
        var log = new RexLog(new RexLogOptions { Minimum = LogLevel.Warning });

        log.Debug("test", "debug");
        log.Info("test", "info");
        log.Warning("test", "warning");
        log.Error("test", "error");
        log.Critical("test", "critical");

        Assert.Equal(["warning", "error", "critical"], log.Tail(10).Select(e => e.Message));
    }

    [Fact]
    public void TheRingKeepsTheNewestEntries()
    {
        var log = new RexLog(new RexLogOptions { RingCapacity = 16, Minimum = LogLevel.Debug });

        for (var i = 0; i < 40; i++)
        {
            log.Info("test", i.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        Assert.Equal(Enumerable.Range(24, 16).Select(i => i.ToString(System.Globalization.CultureInfo.InvariantCulture)), log.Tail(100).Select(e => e.Message));
        Assert.Equal(["38", "39"], log.Tail(2).Select(e => e.Message));
        Assert.Empty(log.Tail(-5));
    }

    [Fact]
    public void EntriesAreRaisedAsTheyAreWritten()
    {
        var log = RexLog.InMemory();
        var seen = new List<LogEntry>();
        log.EntryWritten += (_, entry) => seen.Add(entry);

        log.Error("engine", "decoder failed");

        var entry = Assert.Single(seen);
        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.Equal("engine", entry.Source);
        Assert.Null(log.FilePath);
    }

    [Fact]
    public void NullSourceAndMessageBecomeEmpty()
    {
        var log = RexLog.InMemory();

        log.Write(LogLevel.Info, null!, null!);

        var entry = Assert.Single(log.Tail(1));
        Assert.Equal(string.Empty, entry.Source);
        Assert.Equal(string.Empty, entry.Message);
    }

    [Theory]
    [InlineData(LogLevel.Debug, "DEBUG")]
    [InlineData(LogLevel.Info, "INFO")]
    [InlineData(LogLevel.Warning, "WARN")]
    [InlineData(LogLevel.Error, "ERROR")]
    [InlineData(LogLevel.Critical, "FATAL")]
    public void LevelsHaveFixedNames(LogLevel level, string name)
    {
        Assert.Equal(name, LogEntry.LevelName(level));
    }

    [Fact]
    public void AnEntryFormatsAsOneLine()
    {
        var entry = new LogEntry(new DateTimeOffset(2026, 10, 5, 12, 0, 1, 2, TimeSpan.Zero), LogLevel.Warning, "net", "slow");

        var line = entry.Format();

        Assert.EndsWith("[WARN] net: slow", line, StringComparison.Ordinal);
        Assert.Matches(@"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3} ", line);
    }

    [Fact]
    public void EntriesAreAppendedToTheFile()
    {
        var log = new RexLog(new RexLogOptions { Directory = _directory });

        log.Info("app", "started");
        log.Info("app", "stopped");

        var lines = File.ReadAllLines(log.FilePath!);
        Assert.Equal(2, lines.Length);
        Assert.EndsWith("app: stopped", lines[1], StringComparison.Ordinal);
    }

    [Fact]
    public void TheFileRotatesAndKeepsOnlyTheConfiguredNumber()
    {
        var log = new RexLog(new RexLogOptions { Directory = _directory, MaxBytes = 1, KeepFiles = 2 });
        var line = new string('x', 70 * 1024);

        for (var i = 0; i < 5; i++)
        {
            log.Info("rotate", line);
        }

        Assert.True(File.Exists(Path.Combine(_directory, "rexplayer.log")));
        Assert.True(File.Exists(Path.Combine(_directory, "rexplayer.1.log")));
        Assert.True(File.Exists(Path.Combine(_directory, "rexplayer.2.log")));
        Assert.False(File.Exists(Path.Combine(_directory, "rexplayer.3.log")));
    }

    [Fact]
    public void ADisabledFileStillReceivesCriticalEntries()
    {
        var log = new RexLog(new RexLogOptions { Directory = _directory, FileEnabled = false });

        log.Error("app", "not written");
        log.Critical("app", "written");

        var text = File.ReadAllText(log.FilePath!);
        Assert.DoesNotContain("not written", text, StringComparison.Ordinal);
        Assert.Contains("written", text, StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnwritableFileSwitchesTheFileOffWithoutThrowing()
    {
        Directory.CreateDirectory(_directory);
        var blocker = Path.Combine(_directory, "blocked");
        File.WriteAllText(blocker, "a file where the log folder should be");
        var log = new RexLog(new RexLogOptions { Directory = blocker });

        log.Info("app", "first");
        log.Info("app", "second");

        Assert.Equal(2, log.Tail(10).Count);
    }

    [Fact]
    public void OptionsAreClamped()
    {
        var options = new RexLogOptions { MaxBytes = 1, KeepFiles = 100 };

        Assert.Equal(64 * 1024, options.MaxBytes);
        Assert.Equal(20, options.KeepFiles);
        Assert.Equal(256L * 1024 * 1024, new RexLogOptions { MaxBytes = long.MaxValue }.MaxBytes);
        Assert.Equal(1, new RexLogOptions { KeepFiles = 0 }.KeepFiles);
    }

    [Fact]
    public void TheLogNeedsOptions()
    {
        Assert.Throws<ArgumentNullException>(() => new RexLog(null!));
    }

    [Fact]
    public void RootCauseUnwrapsSingleCauseWrappers()
    {
        var root = new InvalidOperationException("the real problem");
        var wrapped = new TargetInvocationException(new AggregateException(root));

        Assert.Same(root, ExceptionDiagnostics.RootCause(wrapped));
        Assert.Equal("InvalidOperationException: the real problem", ExceptionDiagnostics.Summary(wrapped));
    }

    [Fact]
    public void RootCauseStopsAtAnAggregateOfSeveral()
    {
        var aggregate = new AggregateException(new IOException("a"), new IOException("b"));

        Assert.Same(aggregate, ExceptionDiagnostics.RootCause(aggregate));
        Assert.Throws<ArgumentNullException>(() => ExceptionDiagnostics.RootCause(null!));
    }

    [Fact]
    public void ATargetInvocationWithoutACauseIsItsOwnRoot()
    {
        var empty = new TargetInvocationException(null);

        Assert.Same(empty, ExceptionDiagnostics.RootCause(empty));
    }
}
