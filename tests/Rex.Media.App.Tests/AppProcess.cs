using System.Diagnostics;
using System.IO;
using System.Windows.Automation;
using Rex.Media.TestKit;

namespace Rex.Media.App.Tests;

/// <summary>
/// The real rexplayer.exe, started for one test with a scratch data folder (REXPLAYER_ROOT) and no
/// audible sound (REXPLAYER_FAKE_AUDIO), and closed (or killed) afterwards. Everything is driven
/// through UI Automation, which needs no keyboard focus, so a test never types into another window.
/// Every wait is bounded by <see cref="Patience"/>, so a hung window fails one test, not the run.
/// </summary>
internal sealed class AppProcess : IDisposable
{
    public static readonly TimeSpan Patience = TimeSpan.FromSeconds(20);

    private readonly Process _process;

    private AppProcess(Process process, string root, AutomationElement window)
    {
        _process = process;
        Root = root;
        Window = window;
    }

    /// <summary>The scratch folder the app keeps its settings and log in.</summary>
    public string Root { get; }

    public AutomationElement Window { get; }

    public string SettingsPath => Path.Combine(Root, "settings.json");

    public string LogText => File.Exists(Path.Combine(Root, "logs", "rexplayer.log"))
        ? File.ReadAllText(Path.Combine(Root, "logs", "rexplayer.log"))
        : "";

    /// <summary>The built app; the suite needs it built first (./dev.ps1 build or the CI step that does).</summary>
    public static string Executable => RepoPaths.Combine("src", "Rex.Media.App", "bin", RepoPaths.Configuration, "net10.0-windows10.0.22621.0", "win-x64", "rexplayer.exe");

    /// <summary>Starts the app with <paramref name="arguments"/>, reusing <paramref name="root"/> when given.</summary>
    public static AppProcess Start(string[]? arguments = null, string? root = null)
    {
        root ??= Path.Combine(Path.GetTempPath(), "rexplayer-ui-" + Guid.NewGuid().ToString("N"));
        var process = Process.Start(StartInfo(arguments, root))!;
        var window = Wait.Until(() =>
        {
            process.Refresh();
            Assert.False(process.HasExited, $"rexplayer exited with {(process.HasExited ? process.ExitCode : 0)} before showing its window.");
            if (process.MainWindowHandle == 0)
            {
                return null;
            }

            // Early on the main window can be a passing helper window: only the player's own will do.
            var candidate = AutomationElement.FromHandle(process.MainWindowHandle);
            return candidate.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, "PlayPauseButton")) is null ? null : candidate;
        });
        return new AppProcess(process, root, window);
    }

    /// <summary>
    /// Launches the app and waits for it to exit by itself, as a second launch that hands its files
    /// to the running player does; returns its exit code.
    /// </summary>
    public static int Launch(string[] arguments, string root)
    {
        using var process = Process.Start(StartInfo(arguments, root))!;
        Assert.True(process.WaitForExit(Patience), "The second launch kept running instead of handing over.");
        return process.ExitCode;
    }

    /// <summary>
    /// The app's start: its data in <paramref name="root"/>, no audible sound, and a pipe named for
    /// the root, so a test's player can never take files from (or give them to) any other player.
    /// </summary>
    private static ProcessStartInfo StartInfo(string[]? arguments, string root)
    {
        Assert.True(File.Exists(Executable), $"Build the app first: {Executable} is missing.");
        Directory.CreateDirectory(root);
        var start = new ProcessStartInfo(Executable) { UseShellExecute = false };
        foreach (var argument in arguments ?? [])
        {
            start.ArgumentList.Add(argument);
        }

        start.Environment["REXPLAYER_ROOT"] = root;
        start.Environment["REXPLAYER_FAKE_AUDIO"] = "1";
        start.Environment["REXPLAYER_PIPE_NAME"] = "rexplayer-test-" + Path.GetFileName(root);
        return start;
    }

    /// <summary>The control with this automation id, waiting for it to appear.</summary>
    public AutomationElement Find(string automationId) =>
        Wait.Until(() => Window.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, automationId)));

    /// <summary>Whether a control with this id is shown now (collapsed controls are not in the tree).</summary>
    public bool IsShown(string automationId) =>
        Window.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, automationId)) is { } element && !element.Current.IsOffscreen;

    public string Text(string automationId) => Find(automationId).Current.Name;

    public string Title
    {
        get
        {
            _process.Refresh();
            return _process.MainWindowTitle;
        }
    }

    public void Press(string automationId) => ((InvokePattern)Find(automationId).GetCurrentPattern(InvokePattern.Pattern)).Invoke();

    public void Toggle(string automationId) => ((TogglePattern)Find(automationId).GetCurrentPattern(TogglePattern.Pattern)).Toggle();

    public void SetValue(string automationId, double value) => ((RangeValuePattern)Find(automationId).GetCurrentPattern(RangeValuePattern.Pattern)).SetValue(value);

    /// <summary>Closes the window as its close button does (WM_CLOSE) and waits for the process to end.</summary>
    public int Close()
    {
        _process.CloseMainWindow();
        Assert.True(_process.WaitForExit(Patience), "rexplayer did not close. Its log ends:" + Environment.NewLine + string.Join(Environment.NewLine, LogText.Split(Environment.NewLine).TakeLast(15)));
        return _process.ExitCode;
    }

    public void Dispose()
    {
        if (!_process.HasExited)
        {
            _process.Kill();
            _process.WaitForExit();
        }

        _process.Dispose();
    }
}

/// <summary>Polling for a window's state, the only way to wait on another process's UI.</summary>
internal static class Wait
{
    public static T Until<T>(Func<T?> probe)
        where T : class
    {
        var clock = Stopwatch.StartNew();
        while (true)
        {
            if (probe() is { } found)
            {
                return found;
            }

            Assert.True(clock.Elapsed < AppProcess.Patience, "The window never got there.");
            Thread.Sleep(50);
        }
    }

    public static void For(Func<bool> condition, string what)
    {
        var clock = Stopwatch.StartNew();
        while (!condition())
        {
            Assert.True(clock.Elapsed < AppProcess.Patience, $"Waited in vain for {what}.");
            Thread.Sleep(50);
        }
    }
}
