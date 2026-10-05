using System.Globalization;
using System.Text;

namespace Rex.Media.Diagnostics;

public enum LogLevel
{
    Debug = 0,
    Info = 1,
    Warning = 2,
    Error = 3,
    Critical = 4,
}

/// <summary>One line of the log, as the log console shows it.</summary>
public sealed record LogEntry(DateTimeOffset Timestamp, LogLevel Level, string Source, string Message)
{
    public string Format() => string.Create(
        CultureInfo.InvariantCulture,
        $"{Timestamp.LocalDateTime:yyyy-MM-dd HH:mm:ss.fff} [{LevelName(Level)}] {Source}: {Message}");

    public static string LevelName(LogLevel level) => level switch
    {
        LogLevel.Debug => "DEBUG",
        LogLevel.Info => "INFO",
        LogLevel.Warning => "WARN",
        LogLevel.Error => "ERROR",
        _ => "FATAL",
    };
}

/// <summary>
/// The application log: a rotating text file plus an in-memory ring the log console reads. It never
/// throws. A logger that can fail turns a small problem into a crash, so when the file cannot be
/// written the file side switches itself off and the ring keeps working. Critical entries are written
/// even when file logging is turned off, because they are what a bug report needs.
/// </summary>
public sealed class RexLog
{
    private readonly object _gate = new();
    private readonly LogEntry[] _ring;
    private readonly TimeProvider _clock;
    private int _ringStart;
    private int _ringCount;
    private bool _fileBroken;

    public RexLog(RexLogOptions options, TimeProvider? clock = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        Options = options;
        _clock = clock ?? TimeProvider.System;
        _ring = new LogEntry[Math.Max(16, options.RingCapacity)];
    }

    /// <summary>A log that keeps entries in memory only, for tests and the CLI's quiet mode.</summary>
    public static RexLog InMemory(LogLevel minimum = LogLevel.Debug) => new(new RexLogOptions { Directory = null, Minimum = minimum });

    public RexLogOptions Options { get; }

    /// <summary>Raised after every entry is recorded, on the thread that wrote it.</summary>
    public event EventHandler<LogEntry>? EntryWritten;

    public string? FilePath => Options.Directory is null ? null : Path.Combine(Options.Directory, Options.FileName);

    public void Debug(string source, string message) => Write(LogLevel.Debug, source, message);

    public void Info(string source, string message) => Write(LogLevel.Info, source, message);

    public void Warning(string source, string message) => Write(LogLevel.Warning, source, message);

    public void Error(string source, string message) => Write(LogLevel.Error, source, message);

    public void Critical(string source, string message) => Write(LogLevel.Critical, source, message);

    public void Write(LogLevel level, string source, string message)
    {
        if (level < Options.Minimum && level != LogLevel.Critical)
        {
            return;
        }

        var entry = new LogEntry(_clock.GetLocalNow(), level, source ?? string.Empty, message ?? string.Empty);
        lock (_gate)
        {
            var index = (_ringStart + _ringCount) % _ring.Length;
            _ring[index] = entry;
            if (_ringCount < _ring.Length)
            {
                _ringCount++;
            }
            else
            {
                _ringStart = (_ringStart + 1) % _ring.Length;
            }

            if (Options.FileEnabled || level == LogLevel.Critical)
            {
                AppendToFile(entry);
            }
        }

        EntryWritten?.Invoke(this, entry);
    }

    /// <summary>The newest <paramref name="count"/> entries, oldest first.</summary>
    public IReadOnlyList<LogEntry> Tail(int count)
    {
        lock (_gate)
        {
            var take = Math.Clamp(count, 0, _ringCount);
            var result = new LogEntry[take];
            for (var i = 0; i < take; i++)
            {
                result[i] = _ring[(_ringStart + _ringCount - take + i) % _ring.Length];
            }

            return result;
        }
    }

    private void AppendToFile(LogEntry entry)
    {
        var path = FilePath;
        if (path is null || _fileBroken)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(Options.Directory!);
            var info = new FileInfo(path);
            if (info.Exists && info.Length >= Options.MaxBytes)
            {
                Rotate(path);
            }

            File.AppendAllText(path, entry.Format() + Environment.NewLine, Encoding.UTF8);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The log is advisory. A full disk or a locked file must not take playback down with it.
            _fileBroken = true;
        }
    }

    private void Rotate(string path)
    {
        var directory = Path.GetDirectoryName(path)!;
        var stem = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);
        string Numbered(int n) => Path.Combine(directory, string.Create(CultureInfo.InvariantCulture, $"{stem}.{n}{extension}"));

        File.Delete(Numbered(Options.KeepFiles));
        for (var n = Options.KeepFiles - 1; n >= 1; n--)
        {
            if (File.Exists(Numbered(n)))
            {
                File.Move(Numbered(n), Numbered(n + 1));
            }
        }

        File.Move(path, Numbered(1));
    }
}

/// <summary>Where and how much the log writes. Clamped on construction so a bad setting cannot break it.</summary>
public sealed record RexLogOptions
{
    private readonly long _maxBytes = 4 * 1024 * 1024;
    private readonly int _keepFiles = 3;

    /// <summary>The folder for log files, or null to keep entries in memory only.</summary>
    public string? Directory { get; init; }

    public string FileName { get; init; } = "rexplayer.log";

    public bool FileEnabled { get; init; } = true;

    public LogLevel Minimum { get; init; } = LogLevel.Info;

    public int RingCapacity { get; init; } = 2000;

    public long MaxBytes
    {
        get => _maxBytes;
        init => _maxBytes = Math.Clamp(value, 64 * 1024, 256L * 1024 * 1024);
    }

    public int KeepFiles
    {
        get => _keepFiles;
        init => _keepFiles = Math.Clamp(value, 1, 20);
    }
}
