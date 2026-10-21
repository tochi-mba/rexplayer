using System.Diagnostics;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media;
using Rex.Media.AppCore.Automation;
using Rex.Media.AppCore.Performance;

namespace Rex.Media.App;

/// <summary>
/// What scripts/measure-startup.ps1 asks the running player through the automation pipe: its
/// figures (<c>metrics</c>, which with the id "start" also starts watching the window's thread and
/// the pictures' cadence afresh), a jump to a time (<c>seek</c>), and how long something takes
/// (<c>measure</c>): opening the library, Preferences, the Subject Lock panel, the picture effect
/// picker or the effects panel ("open:" and the name), or scanning a folder into the library
/// ("library-scan"). Nothing here runs unless a script asks; watching the window's thread costs a
/// probe every 50 ms only once a measurement has started.
/// </summary>
public sealed partial class MainWindow
{
    private static readonly TimeSpan ProbeEvery = TimeSpan.FromMilliseconds(50);

    private readonly ThreadResponsiveness _windowThread = new();
    private long _watchingSince;
    private Timer? _probe;

    /// <summary>Answers the requests that measure, on the window's thread; null for any other request.</summary>
    private Task<AutomationReply>? AnswerMeasurement(AutomationRequest request) => request.Command switch
    {
        "metrics" => OnWindow(async () => Measured(await MetricsAsync(request.Id == "start"))),
        "seek" when request.To is { } to && double.IsFinite(to) && to >= 0 => OnWindow(() =>
        {
            _player.Seek(TimeSpan.FromSeconds(to));
            return Task.FromResult(new AutomationReply { Ok = true, Position = to });
        }),
        "seek" => Task.FromResult(AutomationReply.Failed("A seek needs \"to\", a time in seconds.")),
        "measure" => OnWindow(() => MeasureAsync(request)),
        _ => null,
    };

    private static AutomationReply Measured(IReadOnlyDictionary<string, double> metrics) => new() { Ok = true, Metrics = metrics };

    /// <summary>Runs <paramref name="work"/> on the window's thread and hands back its answer.</summary>
    private Task<AutomationReply> OnWindow(Func<Task<AutomationReply>> work)
    {
        var answered = new TaskCompletionSource<AutomationReply>(TaskCreationOptions.RunContinuationsAsynchronously);
        var queued = DispatcherQueue.TryEnqueue(async () =>
        {
            try
            {
                answered.SetResult(await work());
            }
            catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException or ArgumentException)
            {
                answered.SetResult(AutomationReply.Failed(ex.Message));
            }
        });
        return queued ? answered.Task : Task.FromResult(AutomationReply.Failed("The player is closing."));
    }

    /// <summary>The figures now; <paramref name="start"/> first starts watching the window and the cadence afresh.</summary>
    private async Task<IReadOnlyDictionary<string, double>> MetricsAsync(bool start)
    {
        if (start)
        {
            _windowThread.Clear();
            _watchingSince = Stopwatch.GetTimestamp();
            _probe ??= new Timer(_ => PostProbe(), null, ProbeEvery, Timeout.InfiniteTimeSpan);
            await _player.ClearCadenceAsync();
        }

        var stats = await _player.StatsAsync();
        using var process = Process.GetCurrentProcess();
        var sample = new ProcessSample(
            process.TotalProcessorTime.TotalMilliseconds,
            process.WorkingSet64,
            process.PrivateMemorySize64,
            process.PeakWorkingSet64,
            process.PeakPagedMemorySize64,
            GC.GetTotalMemory(forceFullCollection: false),
            GC.GetTotalAllocatedBytes(precise: false),
            GC.CollectionCount(0),
            GC.CollectionCount(1),
            GC.CollectionCount(2),
            process.Threads.Count,
            Environment.ProcessorCount);
        var watched = _watchingSince == 0 ? TimeSpan.Zero : Stopwatch.GetElapsedTime(_watchingSince);
        var visual = _visualizing && _visualStage is { } stage ? new VisualSample(stage.DrawMilliseconds, stage.Quality, stage.Canvas.Width, stage.Canvas.Height) : (VisualSample?)null;
        return PlayerMetrics.Read(sample, stats, _windowThread.Snapshot(watched), visual, _budget.Tier, _presenter?.PicturesDrawn);
    }

    /// <summary>One probe: posted now, it notes how late the window's thread runs it, then the next is posted a little later.</summary>
    private void PostProbe()
    {
        var posted = Stopwatch.GetTimestamp();
        var queued = !_closed && DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Normal, () =>
        {
            _windowThread.Answered(Stopwatch.GetElapsedTime(posted));
            _probe?.Change(ProbeEvery, Timeout.InfiniteTimeSpan);
        });
        if (!queued)
        {
            _probe?.Dispose();
            _probe = null;
        }
    }

    private async Task<AutomationReply> MeasureAsync(AutomationRequest request)
    {
        var started = Stopwatch.GetTimestamp();
        switch (request.Id)
        {
            case "library-scan" when request.Paths.Count == 1 && _library is { } library && _scanner is { } scanner:
                library.AddFolder(request.Paths[0]);
                scanner.Request();
                while (scanner.IsBusy)
                {
                    await Task.Delay(20);
                }

                return Measured(new Dictionary<string, double>
                {
                    ["scan-ms"] = Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                    ["entries"] = library.Entries.Count,
                });
            case "open:library":
                SetLibraryOpen(true);
                break;
            case "open:preferences":
                _ = ShowPreferencesAsync();
                break;
            case "open:subject-lock":
                OpenSubjectTools();
                break;
            case "open:effect-picker":
                _ = ShowVideoStyleSettingsAsync(effect: true);
                break;
            case "open:effects":
                _ = ShowEffectsAsync();
                break;
            default:
                return AutomationReply.Failed($"There is nothing called \"{request.Id}\" to measure.");
        }

        await ShownAsync();
        var took = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        _dialog?.Hide();
        if (request.Id == "open:library")
        {
            SetLibraryOpen(false);
        }
        else if (request.Id == "open:subject-lock" && _subjectOpen)
        {
            CloseSubjectTools();
        }

        await ShownAsync();
        return Measured(new Dictionary<string, double> { ["open-ms"] = took });
    }

    /// <summary>
    /// Completes once what was asked of the window has been built and drawn: after everything
    /// queued on its thread (the lowest priority runs last) and then the next frame it renders.
    /// </summary>
    private Task ShownAsync()
    {
        var shown = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        OnWindowThread(DispatcherQueuePriority.Low, () =>
        {
            EventHandler<object>? rendering = null;
            rendering = (_, _) =>
            {
                CompositionTarget.Rendering -= rendering;
                shown.TrySetResult();
            };
            CompositionTarget.Rendering += rendering;
        });
        return shown.Task;
    }
}
