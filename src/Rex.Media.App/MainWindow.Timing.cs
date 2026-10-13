using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media;

namespace Rex.Media.App;

/// <summary>
/// The launch's timing lines from the window's side: the first frame the window draws, and the
/// first moment its thread is free for the user once that frame is up.
/// </summary>
public sealed partial class MainWindow
{
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
}
