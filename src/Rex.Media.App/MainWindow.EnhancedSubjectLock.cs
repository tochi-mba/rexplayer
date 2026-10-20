using Microsoft.UI.Xaml;
using Rex.Media.Vision;
using Rex.Media.Vision.WindowsML;

namespace Rex.Media.App;

/// <summary>
/// Enhanced Subject Lock, the optional model download (ADR-0019). The models are fetched only when
/// the user chooses Download in Preferences, into <c>models</c> under the data folder, and are
/// verified against the hashes the catalogue pins. Subject Lock keeps its own selection, tracking
/// and fill whether or not they are installed. Installed models are opened off the window's thread
/// when Subject Lock first opens (or when a download finishes while it is open), handed to the
/// presenter, and released when the window closes or the models are removed; a model that fails to
/// open is logged and Subject Lock runs without it.
/// </summary>
public sealed partial class MainWindow
{
    private ModelSet? _subjectModels;
    private CancellationTokenSource? _subjectModelsDownload;
    private SharedSegmenter? _openSegmenter;
    private SharedInpainter? _openInpainter;
    private bool _subjectModelsOpening;

    /// <summary>The store under the data folder, so REXPLAYER_ROOT moves it with everything else.</summary>
    private static ModelStore SubjectModelStore { get; } = new(Path.Combine(App.DataRoot, "models"));

    /// <summary>The models Subject Lock may use, with their install state; created on first use.</summary>
    private ModelSet SubjectModels => _subjectModels ??= new ModelSet(SubjectModelStore, ModelCatalogue.SubjectLock);

    /// <summary>The Preferences section, bound to the long-lived model set.</summary>
    private UIElement SubjectModelsSection() =>
        new EnhancedSubjectLockSection(SubjectModels, StartSubjectModelsDownload, StopSubjectModelsDownload, RemoveSubjectModels);

    private void StartSubjectModelsDownload()
    {
        if (_subjectModelsDownload is not null)
        {
            return;
        }

        var cancel = _subjectModelsDownload = new CancellationTokenSource();
        var models = SubjectModels;
        _ = Task.Run(async () =>
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd($"rexplayer/{Version}");
            var downloader = new ModelDownloader(http, SubjectModelStore, FreeBytes);
            try
            {
                await models.DownloadAsync(downloader, cancel.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancel.IsCancellationRequested)
            {
                // The section already shows the state the cancel restored.
            }
            finally
            {
                OnWindowThread(() =>
                {
                    _subjectModelsDownload = null;
                    cancel.Dispose();
                    if (models.Ready)
                    {
                        Say("Enhanced Subject Lock is installed");
                        if (_subjectOpen)
                        {
                            OpenSubjectModels();
                        }
                    }
                });
            }
        });
    }

    private void StopSubjectModelsDownload() => _subjectModelsDownload?.Cancel();

    private void RemoveSubjectModels()
    {
        if (_subjectModelsDownload is not null)
        {
            return;
        }

        // The files can go only once nothing has them open, and closing waits for a model still running.
        var closed = CloseSubjectModels();
        _ = closed.ContinueWith(_ => OnWindowThread(() =>
        {
            try
            {
                SubjectModels.Remove();
                Say("Enhanced Subject Lock models removed");
            }
            catch (IOException ex)
            {
                Say("The models could not be removed: " + ex.Message);
            }
            catch (UnauthorizedAccessException ex)
            {
                Say("The models could not be removed: " + ex.Message);
            }
        }), TaskScheduler.Default);
    }

    /// <summary>Opens the installed models on a worker and hands them to the presenter, once.</summary>
    private void OpenSubjectModels()
    {
        if (_subjectModelsOpening || _openSegmenter is not null || _openInpainter is not null || _closing || !SubjectModels.Ready)
        {
            return;
        }

        _subjectModelsOpening = true;
        _ = Task.Run(() =>
        {
            var segmenter = OpenModel("segmentation", () => PromptSegmenter.Open(SubjectModelStore) is { } model ? new SharedSegmenter(model) : null);
            var inpainter = OpenModel("inpainting", () => Inpainter.Open(SubjectModelStore) is { } model ? new SharedInpainter(model) : null);
            OnWindowThread(() =>
            {
                _subjectModelsOpening = false;
                (_openSegmenter, _openInpainter) = (segmenter, inpainter);
                var tracker = segmenter is null ? null : new PromptedMaskTracker(segmenter);
                OnPresenterThread(p => p.UseSubjectModels(segmenter, inpainter, tracker));
            });
            if (_closing)
            {
                // The window went while the models opened: nothing will use them.
                segmenter?.Dispose();
                inpainter?.Dispose();
            }
        });
    }

    private static T? OpenModel<T>(string kind, Func<T?> open)
        where T : class
    {
        try
        {
            var model = open();
            if (model is not null)
            {
                App.Log.Info(LogSource, $"The {kind} model for Subject Lock is open.");
            }

            return model;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // A model that will not load leaves Subject Lock on its own methods.
            App.Log.Warning(LogSource, $"The {kind} model for Subject Lock could not be opened, so Subject Lock uses its own method: {ex.Message}");
            return null;
        }
    }

    /// <summary>Takes the models from the presenter and releases them off the window's thread; the task ends when they are released.</summary>
    private Task CloseSubjectModels()
    {
        var (segmenter, inpainter) = (_openSegmenter, _openInpainter);
        (_openSegmenter, _openInpainter) = (null, null);
        if (segmenter is null && inpainter is null)
        {
            return Task.CompletedTask;
        }

        OnPresenterThread(p => p.UseSubjectModels(null, null, null));
        return Task.Run(() =>
        {
            segmenter?.Dispose();
            inpainter?.Dispose();
        });
    }

    private static long FreeBytes(string folder) => new DriveInfo(Path.GetPathRoot(Path.GetFullPath(folder))!).AvailableFreeSpace;
}
