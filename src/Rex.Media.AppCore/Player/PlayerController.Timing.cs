using Rex.Media.Diagnostics;

namespace Rex.Media.AppCore.Player;

public sealed partial class PlayerController
{
    /// <summary>
    /// Where the controller marks the moment each item is asked to start (a "start" timing line),
    /// so the time from a click to the first picture can be read from the log; null marks nothing.
    /// </summary>
    public Milestones? Milestones { get; set; }

    private void Reach(string milestone) => Milestones?.Reach(milestone);
}
