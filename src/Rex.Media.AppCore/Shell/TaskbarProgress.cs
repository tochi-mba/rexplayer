namespace Rex.Media.AppCore.Shell;

/// <summary>How the taskbar button's progress shows (the taskbar's own states).</summary>
public enum TaskbarProgressState
{
    None,
    Normal,
    Paused,
}

/// <summary>The taskbar button's progress, in whole percent so it changes at most a hundred times.</summary>
public readonly record struct TaskbarProgressView(TaskbarProgressState State, int Percent)
{
    public static TaskbarProgressView Off { get; } = new(TaskbarProgressState.None, 0);
}

/// <summary>
/// The taskbar button's progress is for long work going on in the background, never for the
/// position in what plays: a bar creeping along under every song would say nothing the window does
/// not, and would hide the moments the bar means something. Two kinds of work earn it: hearing the
/// speech of a file (and fetching its model first), which the user asked for and may wait on from
/// another window; and downloading a new version, shown paused when the download is held. Hearing
/// speech comes first when both run: it is what the user is waiting for. A check for updates, which
/// is brief and unasked, shows nothing.
/// </summary>
public static class TaskbarProgress
{
    /// <param name="hearing">How much of the speech has been heard, 0 to 1; null when none is being heard.</param>
    /// <param name="update">How much of a new version has been downloaded, 0 to 1; null when none is downloading.</param>
    /// <param name="updateHeld">Whether that download is paused.</param>
    public static TaskbarProgressView Choose(double? hearing, double? update, bool updateHeld) =>
        hearing is { } heard ? new(TaskbarProgressState.Normal, Percent(heard))
        : update is { } downloaded ? new(updateHeld ? TaskbarProgressState.Paused : TaskbarProgressState.Normal, Percent(downloaded))
        : TaskbarProgressView.Off;

    private static int Percent(double share) => double.IsFinite(share) ? (int)Math.Round(Math.Clamp(share, 0, 1) * 100) : 0;
}
