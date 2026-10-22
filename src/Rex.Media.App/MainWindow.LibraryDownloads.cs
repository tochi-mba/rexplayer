using Microsoft.UI.Dispatching;
using Rex.Media.AppCore.Library;
using Rex.Media.IO;
using Rex.Media.Library;

namespace Rex.Media.App;

/// <summary>
/// Files still being downloaded into the library's folders (LIB-05): each card, line and shelf
/// tile wears a badge with a progress ring while its file is partial, its detail line says how far
/// the download is, and both go the moment the file is whole. While such a file is shown the
/// library looks at it again every few seconds, so a download that stops growing is seen to be
/// whole without waiting for the next scan; Play on one too incomplete to open says so instead of
/// failing. Nothing here runs while no file is downloading.
/// </summary>
public sealed partial class MainWindow
{
    private static readonly TimeSpan DownloadRecheckEvery = TimeSpan.FromSeconds(2.5);

    private DispatcherQueueTimer? _downloadRecheck;
    private int _recheckingDownloads;
    private int _downloadWakePending;

    /// <summary>Coalesces watcher notifications before they reach the UI dispatcher.</summary>
    private void QueueDownloadRecheck()
    {
        if (Interlocked.Exchange(ref _downloadWakePending, 1) == 0)
        {
            OnWindowThread(StartDownloadRechecks);
        }
    }

    private void WireDownloadRechecks()
    {
        _downloadRecheck = CreateTimer();
        _downloadRecheck.Interval = DownloadRecheckEvery;
        _downloadRecheck.IsRepeating = true;
        OnTick(_downloadRecheck, RecheckDownloads);
    }

    /// <summary>The badge and detail a row shows for its file's download, or none once it is whole.</summary>
    private static void ShowDownload(LibraryRow row, LibraryEntry entry) =>
        row.SetDownload(entry.Downloading ? Downloads.Detail(entry.Size, entry.FinalSize, entry.Allocated) : null, entry.Downloading ? Downloads.Fraction(entry.Size, entry.FinalSize, entry.Allocated) : null);

    /// <summary>
    /// Brings every shown row's badge in line with the library after it changed, in place (the
    /// view is rebuilt only when what it holds changes), and keeps looking while any file shown
    /// is still downloading.
    /// </summary>
    private void ShowDownloads()
    {
        var downloading = _library!.Entries.Where(entry => entry.Downloading).ToDictionary(entry => entry.Path, StringComparer.OrdinalIgnoreCase);
        foreach (var row in _libraryRows.Concat(_homeShelfRows))
        {
            if (row.Item is not LibraryEntry entry)
            {
                continue;
            }

            if (downloading.GetValueOrDefault(entry.Path) is { } fresh)
            {
                ShowDownload(row, fresh);
            }
            else
            {
                row.SetDownload(null, null);
            }
        }

        if (downloading.Count > 0 || _library.Entries.Any(entry => entry.Suspect))
        {
            StartDownloadRechecks();
        }
        else
        {
            _downloadRecheck?.Stop();
            Interlocked.Exchange(ref _downloadWakePending, 0);
        }
    }

    /// <summary>Starts the cheap per-file watch used instead of repeatedly scanning whole folders.</summary>
    private void StartDownloadRechecks()
    {
        if (_downloadRecheck is not null && !_downloadRecheck.IsRunning)
        {
            _downloadRecheck.Start();
        }
    }

    /// <summary>Looks at the downloading files again, off the window's thread; the library's change brings the rows up to date.</summary>
    private void RecheckDownloads()
    {
        Interlocked.Exchange(ref _downloadWakePending, 0);
        if (Interlocked.Exchange(ref _recheckingDownloads, 1) != 0)
        {
            return;
        }

        var library = _library!;
        var scanner = _scanner!;
        Forget(Task.Run(() =>
        {
            try
            {
                var looked = library.Entries.Where(entry => entry.Downloading || entry.Suspect).Select(entry => entry.Path).ToList();
                library.RecheckDownloads(Stat, File.Exists, LibraryScanner.ReadMarker, InspectFile);

                // A file found whole is read again, for its details and to be sure it opens.
                foreach (var folder in looked.Where(path => library.Entry(path) is { Downloading: false, Probed: false }).Select(Path.GetDirectoryName).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    scanner.Request(folder);
                }

                if (!library.Entries.Any(entry => entry.Downloading || entry.Suspect))
                {
                    OnWindowThread(() =>
                    {
                        // Revalidate on the UI thread: a scan may have discovered another download
                        // while this completion callback was waiting behind other work.
                        if (!library.Entries.Any(entry => entry.Downloading || entry.Suspect))
                        {
                            _downloadRecheck?.Stop();
                            Interlocked.Exchange(ref _downloadWakePending, 0);
                        }
                    });
                }
            }
            finally
            {
                Interlocked.Exchange(ref _recheckingDownloads, 0);
            }
        }));
    }

    /// <summary>
    /// What Play says of <paramref name="entry"/> when its file is still downloading and could not
    /// be opened yet ("Still downloading: 20% so far"), after one more look at the disk; null when
    /// it can play, growing as it goes.
    /// </summary>
    private string? NotYetPlayable(LibraryEntry entry)
    {
        if (!entry.Downloading)
        {
            return null;
        }

        _library!.RecheckDownloads(Stat, File.Exists, LibraryScanner.ReadMarker, InspectFile);
        var fresh = _library.Entry(entry.Path) ?? entry;
        // A changed partial file is deliberately marked unprobed: its earlier failed probe is
        // stale, and the bytes which just arrived may now include enough of the header to open.
        // Only a completed probe of the current bytes is a safe reason to refuse playback.
        return fresh.Downloading && fresh.Probed && fresh.Problem is not null
            ? Downloads.NotYetNotice(fresh.Size, fresh.FinalSize, fresh.Allocated)
            : null;
    }

    /// <summary>The scanner's look at a file: a writer holding it, and a sparse file's bytes on the disk.</summary>
    private static FileLook InspectFile(LibraryFile file) => LibraryScanner.Inspect(file, FileByteSource.Allocation);

    private static LibraryFile? Stat(string path)
    {
        var file = new FileInfo(path);
        return file.Exists ? LibraryScanner.Listed(file) : null;
    }
}
