using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media;
using Rex.Media.AppCore.Automation;
using Rex.Media.AppCore.Player;

namespace Rex.Media.App;

/// <summary>
/// The launch and what follows it, from the window's side: the timing lines for the first frame
/// the window draws and the first moment its thread is free; holding requests from other launches
/// until the window has opened what it was started with; and reading ahead what plays next.
/// </summary>
public sealed partial class MainWindow
{
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly MediaPrefetch _prefetch = new();

    /// <summary>
    /// Answers a request from the pipe once the window has a picture panel and has opened its own
    /// files, so files handed over by a launch moments later join them rather than overtake them.
    /// </summary>
    internal async Task<AutomationReply> AnswerWhenReadyAsync(AutomationRequest request)
    {
        await _ready.Task.ConfigureAwait(false);
        return await AnswerAsync(request).ConfigureAwait(false);
    }

    private void WireLaunch()
    {
        MarkFirstPaint();
        _player.Changed += (_, _) => PrefetchWhatFollows();
    }

    private void MarkFirstPaint()
    {
        EventHandler<object>? rendering = null;
        rendering = (_, _) =>
        {
            CompositionTarget.Rendering -= rendering;
            App.Milestones.Reach("first-paint");

            // Queued behind everything the launch has already asked of the window's thread.
            DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () => App.Milestones.Reach("interactive"));
        };
        CompositionTarget.Rendering += rendering;
    }

    /// <summary>Reads ahead, in the background, the next episode and the next item once they are known.</summary>
    private void PrefetchWhatFollows()
    {
        foreach (var path in _prefetch.Choose([_player.NextEpisode, _player.Playlist.PeekNext(automatic: true)?.Location]))
        {
            _ = Task.Run(() => _prefetch.Read(path));
        }
    }
}
