using System.Diagnostics;
using System.IO;
using System.Windows.Automation;
using Rex.Media.AppCore.Automation;

namespace Rex.PerfTour;

/// <summary>A step that never settled; its message names the step generically, never what the library shows.</summary>
public sealed class TourStepException(string message) : Exception(message);

/// <summary>
/// A running rexplayer window, attached to rather than launched: the tour drives someone's live
/// player, so nothing here starts, stops or closes the process. UI Automation gives the window
/// tree; the automation pipe gives commands, status and the player's own metrics.
/// </summary>
public sealed class AttachedApp
{
    public static readonly TimeSpan Patience = TimeSpan.FromSeconds(20);

    private AttachedApp(Process process, AutomationElement window, string pipeName)
    {
        Process = process;
        Window = window;
        PipeName = pipeName;
    }

    public Process Process { get; }

    public AutomationElement Window { get; }

    /// <summary>The pipe the live player serves: REXPLAYER_PIPE_NAME, or derived from the user's name.</summary>
    public string PipeName { get; }

    /// <summary>Whether the pipe answered a status request when the tour attached.</summary>
    public bool PipeAnswers { get; private set; }

    /// <summary>
    /// Attaches to the rexplayer with <paramref name="pid"/>, or to the one running when no pid is
    /// given; null when none is running. The window must be the player's own (it has the play
    /// button), so a helper window is never driven.
    /// </summary>
    public static AttachedApp? Attach(int? pid, string? pipeName = null)
    {
        var process = pid is { } id ? One(id) : Running();
        if (process is null || process.MainWindowHandle == 0)
        {
            return null;
        }

        var window = AutomationElement.FromHandle(process.MainWindowHandle);
        if (window.FindFirst(TreeScope.Descendants, ById("PlayPauseButton")) is null)
        {
            return null;
        }

        var app = new AttachedApp(process, window, pipeName ?? AutomationPipe.NameForCurrentUser);
        app.PipeAnswers = app.TrySend(new AutomationRequest { Command = "status" }) is { Ok: true };
        return app;
    }

    private static Process? One(int pid)
    {
        try
        {
            var process = Process.GetProcessById(pid);
            return process.ProcessName.Equals("rexplayer", StringComparison.OrdinalIgnoreCase) ? process : null;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static Process? Running() =>
        Process.GetProcessesByName("rexplayer").Where(process => process.MainWindowHandle != 0)
            .OrderByDescending(process => process.WorkingSet64).FirstOrDefault();

    private static PropertyCondition ById(string automationId) => new(AutomationElement.AutomationIdProperty, automationId);

    /// <summary>The control with this automation id, waiting for it to appear.</summary>
    public AutomationElement Find(string automationId) =>
        Until(() => TryFind(automationId), "the control " + automationId);

    public AutomationElement? TryFind(string automationId)
    {
        try
        {
            return Window.FindFirst(TreeScope.Descendants, ById(automationId));
        }
        catch (ElementNotAvailableException)
        {
            return null;
        }
    }

    /// <summary>Whether a control with this id is shown now (collapsed controls are not in the tree).</summary>
    public bool IsShown(string automationId)
    {
        try
        {
            return TryFind(automationId) is { } element && !element.Current.IsOffscreen;
        }
        catch (ElementNotAvailableException)
        {
            return false;
        }
    }

    /// <summary>The control's text; a control replaced while being read is found again.</summary>
    public string Text(string automationId) => Until(() =>
    {
        try
        {
            return TryFind(automationId)?.Current.Name;
        }
        catch (ElementNotAvailableException)
        {
            return null;
        }
    }, "the text of " + automationId);

    /// <summary>Presses a button or menu item; a radio item is chosen instead.</summary>
    public void Press(string automationId) => Press(Find(automationId));

    public static void Press(AutomationElement element)
    {
        if (element.TryGetCurrentPattern(InvokePattern.Pattern, out var invoke))
        {
            ((InvokePattern)invoke).Invoke();
            return;
        }

        if (element.TryGetCurrentPattern(SelectionItemPattern.Pattern, out var select))
        {
            ((SelectionItemPattern)select).Select();
            return;
        }

        ((TogglePattern)element.GetCurrentPattern(TogglePattern.Pattern)).Toggle();
    }

    /// <summary>Runs a player command (the command palette names) through the pipe.</summary>
    public bool Run(string command) =>
        TrySend(new AutomationRequest { Command = "run", Id = command }) is { Ok: true };

    /// <summary>What the player says it is doing now; null when the pipe gave no answer.</summary>
    public AutomationReply? Status() => TrySend(new AutomationRequest { Command = "status" });

    /// <summary>Asks the player to jump to <paramref name="seconds"/>; false when it could not.</summary>
    public bool Seek(double seconds) =>
        TrySend(new AutomationRequest { Command = "seek", To = seconds }) is { Ok: true };

    /// <summary>
    /// The player's own figures (memory, GC, window-thread hitches, frame cadence); with
    /// <paramref name="start"/>, the window-thread watch and the cadence start afresh first.
    /// Null when the running build does not answer metrics.
    /// </summary>
    public IReadOnlyDictionary<string, double>? Metrics(bool start = false) =>
        TrySend(new AutomationRequest { Command = "metrics", Id = start ? "start" : null }) is { Ok: true, Metrics: { } metrics } ? metrics : null;

    private AutomationReply? TrySend(AutomationRequest request)
    {
        try
        {
            return AutomationPipe.SendAsync(PipeName, request, TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Polls until <paramref name="probe"/> gives something, the only way to wait on another process's UI.</summary>
    public static T Until<T>(Func<T?> probe, string what, TimeSpan? limit = null)
        where T : class
    {
        var clock = Stopwatch.StartNew();
        while (true)
        {
            if (probe() is { } found)
            {
                return found;
            }

            if (clock.Elapsed >= (limit ?? Patience))
            {
                throw new TourStepException("Waited in vain for " + what + ".");
            }

            Thread.Sleep(50);
        }
    }

    /// <summary>Polls until <paramref name="condition"/> holds; throws, naming the step generically, when it never does.</summary>
    public static void For(Func<bool> condition, string what, TimeSpan? limit = null) =>
        Until(() => condition() ? what : null, what, limit);

    /// <summary>Whether <paramref name="condition"/> came true within <paramref name="limit"/>.</summary>
    public static bool Within(TimeSpan limit, Func<bool> condition)
    {
        var clock = Stopwatch.StartNew();
        while (!condition())
        {
            if (clock.Elapsed >= limit)
            {
                return false;
            }

            Thread.Sleep(50);
        }

        return true;
    }
}
