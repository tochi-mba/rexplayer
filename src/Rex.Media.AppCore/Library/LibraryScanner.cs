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

    /// <summary>How long reading one file's details may take before the file is taken to be damaged and passed over.</summary>
    public static readonly TimeSpan DefaultProbeTimeout = TimeSpan.FromSeconds(20);

    private const string LogSource = "library";

    private readonly MediaLibrary _library;
    private readonly Func<string, IEnumerable<LibraryFile>> _list;
    private readonly Func<string, CancellationToken, MediaInfo?> _probe;
    private readonly Func<LibraryFile, FileLook>? _inspect;
    private readonly RexLog _log;
    private readonly bool _background;
    private readonly TimeSpan _probeTimeout;
    private readonly object _gate = new();
    private readonly HashSet<string> _pending = new(StringComparer.OrdinalIgnoreCase);
    private readonly CancellationTokenSource _stop = new();
    private bool _everything;
    private Thread? _worker;
    private string _status = "";
    private string? _problem;

    /// <param name="library">The library to keep up to date.</param>
    /// <param name="list">Every file in a folder and the folders inside it, with its size and change time.</param>
    /// <param name="probe">A file's details, or null when it cannot be read.</param>
    /// <param name="log">Where what the scanner does is written down.</param>
    /// <param name="background">False leaves what is asked for to <see cref="RunPending"/>, for tests that run it themselves.</param>
    /// <param name="probeTimeout">How long one file's details may take (<see cref="DefaultProbeTimeout"/> when not given).</param>
    /// <param name="inspect">A look at a file that changed or may be downloading (<see cref="Inspect"/> for the window); null looks at names and sizes only.</param>
    public LibraryScanner(MediaLibrary library, Func<string, IEnumerable<LibraryFile>> list, Func<string, CancellationToken, MediaInfo?> probe, RexLog log, bool background = true, TimeSpan? probeTimeout = null, Func<LibraryFile, FileLook>? inspect = null)
    {
        _inspect = inspect;
        _background = background;
        _probeTimeout = probeTimeout ?? DefaultProbeTimeout;
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

    /// <summary>A recoverable folder-enumeration error; null after a newly requested scan.</summary>
    public string? Problem
    {
        get
        {
            lock (_gate)
            {
                return _problem;
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

            _problem = null;
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
        try
        {
            do
            {
                RunPending();
            }
            while (!Finished());
        }
        catch (Exception ex)
        {
            // A fault of the scanner's own must not end the player from this thread: it is written
            // down, and the next request starts a worker afresh.
            _log.Error(LogSource, "The library scan stopped: " + ex);
            lock (_gate)
            {
                _worker = null;
            }

            SetStatus("");
        }
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

            var result = _library.Scan(folder, files, MediaFiles.LibraryKindOf, ReadMarker, _inspect);
            _log.Info(LogSource, $"Scanned {folder} (watched as {watched}): {result.Added} new, {result.Changed} changed, {result.Removed} gone.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A folder that cannot be listed (a drive unplugged) keeps what was known of it.
            _log.Warning(LogSource, $"{folder} could not be looked in, so what is known of it stays: {ex.Message}");
            lock (_gate)
            {
                _problem = $"{watched} could not be read. Reconnect its drive or check its permissions, then scan again. What the library knew of it is kept.";
            }

            StatusChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void ReadDetails(CancellationToken token)
    {
        var waiting = _library.Unprobed;
        for (var start = 0; start < waiting.Count && !token.IsCancellationRequested; start += Batch)
        {
            SetStatus(string.Create(CultureInfo.CurrentCulture, $"Reading details: {start} of {waiting.Count}"));
            var details = new List<(string, MediaInfo?, string?)>();
            foreach (var entry in waiting.Skip(start).Take(Batch).TakeWhile(_ => !token.IsCancellationRequested))
            {
                details.Add(Probe(entry.Path, token));
            }

            _library.SetReadDetails(details);
        }

        if (waiting.Count > 0)
        {
            _log.Info(LogSource, $"Read the details of {waiting.Count} file(s).");
        }
    }

    /// <summary>
    /// A file's details, or why they could not be read. Each file gets <see cref="DefaultProbeTimeout"/>
    /// (or what the scanner was given): a damaged file that a reader takes too long over is passed
    /// over, and its read is told to stop, so one broken file never holds up the library.
    /// </summary>
    private (string Path, MediaInfo? Info, string? Problem) Probe(string path, CancellationToken token)
    {
        var limit = CancellationTokenSource.CreateLinkedTokenSource(token);
        var reading = Task.Run(() => _probe(path, limit.Token), CancellationToken.None);

        // Waiting throws only when the scanner is stopped; the read's own failure is taken below.
        if (Task.WaitAny(new Task[] { reading }, (int)Math.Min(int.MaxValue, _probeTimeout.TotalMilliseconds), token) < 0)
        {
            limit.Cancel();
            _log.Warning(LogSource, $"{path} took longer than {_probeTimeout.TotalSeconds:0} seconds to read, so it was passed over.");
            return (path, null, "Reading it took too long, so it may be damaged.");
        }

        using (limit)
        {
            try
            {
                return (path, reading.GetAwaiter().GetResult(), null);
            }
            // Whatever goes wrong reading one file (a reader's own fault included) only leaves it out;
            // being stopped is not one of those.
            catch (Exception ex) when (!(ex is OperationCanceledException && token.IsCancellationRequested))
            {
                _log.Debug(LogSource, $"{path} could not be read: {ex.Message}");
                return (path, null, Player.DamageNotices.Short(ex.Message));
            }
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

    /// <summary>
    /// The details of a file on disk, as the demuxer that opens it reads them; for the window's
    /// scanner. A picture has none worth reading (its name is its title), so it is not opened.
    /// </summary>
    public static MediaInfo? ProbeFile(string path, CancellationToken cancellationToken)
    {
        if (MediaFiles.IsPicture(path))
        {
            return null;
        }

        using var source = new FileByteSource(path);
        using var demuxer = MediaRegistries.Demuxers().Open(source, cancellationToken);
        return demuxer.Info;
    }

    /// <summary>The head of a downloader's marker file, enough to read the size it declares; null when it cannot be read.</summary>
    public static byte[]? ReadMarker(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var head = new byte[2048];
            return head[..stream.Read(head, 0, head.Length)];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Every file under <paramref name="folder"/> with its size and change time, and whether it is
    /// sparse or a cloud placeholder (read from the listing, nothing opened). An inaccessible
    /// subfolder aborts the full enumeration instead of masquerading as deleted files; the
    /// scanner keeps the last known library until a complete scan succeeds.
    /// </summary>
    public static IEnumerable<LibraryFile> ListFolder(string folder) =>
        new DirectoryInfo(folder)
            .EnumerateFiles("*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = false, AttributesToSkip = FileAttributes.Hidden | FileAttributes.System })
            .Select(Listed);

    /// <summary>The library's view of a file the disk lists.</summary>
    public static LibraryFile Listed(FileInfo file)
    {
        ArgumentNullException.ThrowIfNull(file);
        var attributes = file.Attributes;
        return new LibraryFile(file.FullName, file.Length, file.LastWriteTimeUtc)
        {
            Sparse = (attributes & FileAttributes.SparseFile) != 0,
            Placeholder = (attributes & (FileAttributes.Offline | PlaceholderAttributes)) != 0,
        };
    }

    /// <summary>FILE_ATTRIBUTE_RECALL_ON_OPEN and FILE_ATTRIBUTE_RECALL_ON_DATA_ACCESS: a cloud file whose bytes are elsewhere.</summary>
    private const FileAttributes PlaceholderAttributes = (FileAttributes)0x00440000;

    /// <summary>
    /// A look at the file itself, for the scanner: whether another program holds it open for
    /// writing (opening it while refusing writers fails with a sharing violation; rexplayer's own
    /// readers share writing, so they never count), and, for a sparse file, how many of its bytes
    /// are on the disk (from <paramref name="allocation"/>, when the system can tell).
    /// </summary>
    public static FileLook Inspect(LibraryFile file, IFileAllocation? allocation) =>
        new(WriterHolds(file.Path), file.Sparse && allocation is not null ? AllocatedOf(file.Path, allocation) : null);

    private const int SharingViolation = 32;

    /// <summary>Whether another program holds <paramref name="path"/> open for writing: a look that shuts writers out is refused.</summary>
    private static bool WriterHolds(string path)
    {
        try
        {
            using var handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            return false;
        }
        catch (IOException ex) when ((ex.HResult & 0xFFFF) == SharingViolation)
        {
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>A sparse file's bytes on the disk, read through a handle that lets its writer go on writing; null when it cannot be opened.</summary>
    private static long? AllocatedOf(string path, IFileAllocation allocation)
    {
        try
        {
            using var handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return allocation.Allocated(handle, RandomAccess.GetLength(handle));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
