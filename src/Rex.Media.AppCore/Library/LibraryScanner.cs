using System.Globalization;
using Rex.Media.Diagnostics;
using Rex.Media.IO;
using Rex.Media.AppCore.Player;
using Rex.Media.Library;
using Rex.Media.Primitives;

namespace Rex.Media.AppCore.Library;

/// <summary>
/// Keeps the library (LIB-05) in line with its folders, out of the way: a scan of a folder (or all
/// of them) is asked for, and one worker at below-normal priority lists them, then reads the details
/// of new and changed files a batch at a time. Asking again while it works only adds to what it will
/// do. Files are listed and read through functions passed in, so tests need no disk.
/// </summary>
public sealed class LibraryScanner : IDisposable
{
    /// <summary>How many files' details are kept in one write.</summary>
    public const int Batch = 25;

    private const string LogSource = "library";

    private readonly MediaLibrary _library;
    private readonly Func<string, IEnumerable<LibraryFile>> _list;
    private readonly Func<string, CancellationToken, MediaInfo?> _probe;
    private readonly RexLog _log;
    private readonly bool _background;
    private readonly object _gate = new();
    private readonly HashSet<string> _pending = new(StringComparer.OrdinalIgnoreCase);
    private readonly CancellationTokenSource _stop = new();
    private bool _everything;
    private Thread? _worker;
    private string _status = "";

    /// <param name="library">The library to keep up to date.</param>
    /// <param name="list">Every file in a folder and the folders inside it, with its size and change time.</param>
    /// <param name="probe">A file's details, or null when it cannot be read.</param>
    /// <param name="log">Where what the scanner does is written down.</param>
    /// <param name="background">False leaves what is asked for to <see cref="RunPending"/>, for tests that run it themselves.</param>
    public LibraryScanner(MediaLibrary library, Func<string, IEnumerable<LibraryFile>> list, Func<string, CancellationToken, MediaInfo?> probe, RexLog log, bool background = true)
    {
        _background = background;
        _library = library ?? throw new ArgumentNullException(nameof(library));
        _list = list ?? throw new ArgumentNullException(nameof(list));
        _probe = probe ?? throw new ArgumentNullException(nameof(probe));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    /// <summary>Raised when <see cref="Status"/> changes, on the worker's thread.</summary>
    public event EventHandler? StatusChanged;

    /// <summary>What the scanner is doing, for the window; empty while it rests.</summary>
    public string Status
    {
        get
        {
            lock (_gate)
            {
                return _status;
            }
        }
    }

    /// <summary>Whether a worker is running or work is waiting for one.</summary>
    public bool IsBusy
    {
        get
        {
            lock (_gate)
            {
                return _worker is not null;
            }
        }
    }

    /// <summary>
    /// Asks for <paramref name="folder"/> (every watched folder when null) to be scanned and new
    /// files read, starting a worker when none is running.
    /// </summary>
    public void Request(string? folder = null)
    {
        lock (_gate)
        {
            if (_stop.IsCancellationRequested)
            {
                return;
            }

            if (folder is null)
            {
                _everything = true;
            }
            else
            {
                _pending.Add(folder);
            }

            StartWorker();
        }
    }

    /// <summary>Starts a worker unless one is running (or the work is left to <see cref="RunPending"/>). Called holding the gate.</summary>
    private void StartWorker()
    {
        if (_worker is not null || !_background)
        {
            return;
        }

        _worker = new Thread(Work) { IsBackground = true, Priority = ThreadPriority.BelowNormal, Name = "rexplayer library" };
        _worker.Start();
    }

    /// <summary>Does all the work asked for, on the calling thread; the worker runs this.</summary>
    public void RunPending(CancellationToken cancellationToken = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _stop.Token);
        var token = linked.Token;
        try
        {
            while (!token.IsCancellationRequested && Take() is { } folders)
            {
                foreach (var folder in folders)
                {
                    ScanFolder(folder, token);
                }

                ReadDetails(token);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            _log.Debug(LogSource, "The library stopped part-way; it carries on at the next scan.");
        }

        SetStatus("");
    }

    /// <summary>Stops the worker after the file it is reading.</summary>
    public void Dispose()
    {
        Thread? worker;
        lock (_gate)
        {
            _stop.Cancel();
            worker = _worker;
        }

        worker?.Join(TimeSpan.FromSeconds(5));
        _stop.Dispose();
    }

    /// <summary>The worker: does what is asked for, and what is asked for meanwhile, then ends.</summary>
    private void Work()
    {
        do
        {
            RunPending();
        }
        while (!Finished());
    }

    /// <summary>Whether the worker may end: stopped, or nothing asked for since its last look (then it is gone).</summary>
    private bool Finished()
    {
        lock (_gate)
        {
            var finished = _stop.IsCancellationRequested || !(_everything || _pending.Count > 0);
            if (finished)
            {
                _worker = null;
            }

            return finished;
        }
    }

    /// <summary>The folders to scan next, taking them from what is asked for; null when nothing is.</summary>
    private List<string>? Take()
    {
        lock (_gate)
        {
            var watched = _library.Folders;
            List<string> folders = _everything ? [.. watched] : [.. _pending.Where(asked => watched.Any(folder => MediaLibrary.Holds(folder, asked)))];
            var asked = _everything || _pending.Count > 0;
            _everything = false;
            _pending.Clear();
            return asked ? folders : null;
        }
    }

    private void ScanFolder(string folder, CancellationToken token)
    {
        SetStatus("Looking in " + folder);
        var watched = _library.Folders.FirstOrDefault(known => MediaLibrary.Holds(known, folder)) ?? folder;
        try
        {
            var files = _list(folder).TakeWhile(_ => !token.IsCancellationRequested).ToList();
            if (token.IsCancellationRequested)
            {
                return;
            }

            var result = _library.Scan(folder, files, MediaFiles.LibraryKindOf);
            _log.Info(LogSource, $"Scanned {folder} (watched as {watched}): {result.Added} new, {result.Changed} changed, {result.Removed} gone.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A folder that cannot be listed (a drive unplugged) keeps what was known of it.
            _log.Warning(LogSource, $"{folder} could not be looked in, so what is known of it stays: {ex.Message}");
        }
    }

    private void ReadDetails(CancellationToken token)
    {
        var waiting = _library.Unprobed;
        for (var start = 0; start < waiting.Count && !token.IsCancellationRequested; start += Batch)
        {
            SetStatus(string.Create(CultureInfo.CurrentCulture, $"Reading details: {start} of {waiting.Count}"));
            var details = new List<(string, MediaInfo?)>();
            foreach (var entry in waiting.Skip(start).Take(Batch).TakeWhile(_ => !token.IsCancellationRequested))
            {
                details.Add((entry.Path, Probe(entry.Path, token)));
            }

            _library.SetDetails(details);
        }

        if (waiting.Count > 0)
        {
            _log.Info(LogSource, $"Read the details of {waiting.Count} file(s).");
        }
    }

    private MediaInfo? Probe(string path, CancellationToken token)
    {
        try
        {
            return _probe(path, token);
        }
        // Whatever goes wrong reading one file (a reader's own fault included) only leaves it out;
        // being stopped is not one of those.
        catch (Exception ex) when (ex is not OutOfMemoryException && !(ex is OperationCanceledException && token.IsCancellationRequested))
        {
            _log.Debug(LogSource, $"{path} could not be read: {ex.Message}");
            return null;
        }
    }

    private void SetStatus(string status)
    {
        lock (_gate)
        {
            if (_status == status)
            {
                return;
            }

            _status = status;
        }

        StatusChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The details of a file on disk, as the demuxer that opens it reads them; for the window's scanner.</summary>
    public static MediaInfo ProbeFile(string path, CancellationToken cancellationToken)
    {
        using var source = new FileByteSource(path);
        using var demuxer = MediaRegistries.Demuxers().Open(source, cancellationToken);
        return demuxer.Info;
    }

    /// <summary>Every file under <paramref name="folder"/> with its size and change time, passing over what cannot be read; for the window's scanner.</summary>
    public static IEnumerable<LibraryFile> ListFolder(string folder) =>
        new DirectoryInfo(folder)
            .EnumerateFiles("*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.Hidden | FileAttributes.System })
            .Select(file => new LibraryFile(file.FullName, file.Length, file.LastWriteTimeUtc));
}
