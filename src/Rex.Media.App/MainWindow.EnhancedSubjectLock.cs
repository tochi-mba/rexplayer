using Microsoft.UI.Xaml;
using Rex.Media.Vision;

namespace Rex.Media.App;

/// <summary>
/// Enhanced Subject Lock, the optional model download (ADR-0019). The models are fetched only when
/// the user chooses Download in Preferences, into <c>models</c> under the data folder, and are
/// verified against the hashes the catalogue pins. Subject Lock keeps its own selection, tracking
/// and fill whether or not they are installed.
/// </summary>
public sealed partial class MainWindow
{
    private ModelSet? _subjectModels;
    private CancellationTokenSource? _subjectModelsDownload;

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
                DispatcherQueue.TryEnqueue(() =>
                {
                    _subjectModelsDownload = null;
                    cancel.Dispose();
                    if (models.Ready)
                    {
                        Say("Enhanced Subject Lock is installed");
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
    }

    private static long FreeBytes(string folder) => new DriveInfo(Path.GetPathRoot(Path.GetFullPath(folder))!).AvailableFreeSpace;
}
