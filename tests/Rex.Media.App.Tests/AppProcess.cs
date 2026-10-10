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

    /// <summary>The window's handle, for reading what it shows.</summary>
    public nint Handle
    {
        get
        {
            _process.Refresh();
            return _process.MainWindowHandle;
        }
    }

    public string SettingsPath => Path.Combine(Root, "settings.json");

    /// <summary>The settings as saved now, read again if the app is just replacing the file.</summary>
    public Rex.Media.Settings.PlayerSettings SavedSettings
    {
        get
        {
            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    return Rex.Media.Settings.SettingsStore.Load(SettingsPath);
                }
                catch (IOException) when (attempt < 20)
                {
                    Thread.Sleep(50);
                }
            }
        }
    }

    public string LogText => File.Exists(Path.Combine(Root, "logs", "rexplayer.log"))
        ? File.ReadAllText(Path.Combine(Root, "logs", "rexplayer.log"))
        : "";

    /// <summary>The built app; the suite needs it built first (./dev.ps1 build or the CI step that does).</summary>
    public static string Executable => RepoPaths.Combine("src", "Rex.Media.App", "bin", RepoPaths.Configuration, "net10.0-windows10.0.22621.0", "win-x64", "rexplayer.exe");

    /// <summary>The version the app reports, from Directory.Build.props.</summary>
    public static string Version { get; } = System.Text.RegularExpressions.Regex.Match(File.ReadAllText(RepoPaths.Combine("Directory.Build.props")), "<Version>(.+?)</Version>").Groups[1].Value;

    /// <summary>
    /// Starts the app with <paramref name="arguments"/>, reusing <paramref name="root"/> when given.
    /// A new root starts as someone who has used rexplayer before (no welcome, nothing new to show)
    /// unless <paramref name="firstRun"/> asks for the very first start.
    /// </summary>
    public static AppProcess Start(string[]? arguments = null, string? root = null, bool firstRun = false)
    {
        root ??= Path.Combine(Path.GetTempPath(), "rexplayer-ui-" + Guid.NewGuid().ToString("N"));
        var settings = Path.Combine(root, "settings.json");
        if (!firstRun && !File.Exists(settings))
        {
            Directory.CreateDirectory(root);
            File.WriteAllText(settings, $$"""{ "firstRunDone": true, "lastSeenVersion": "{{Version}}", "updateChecks": "Off" }""");
        }

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
        if (!process.WaitForExit(Patience))
        {
            // Never left running: a stray window would hold the build's files and confuse later tests.
            process.Kill();
            process.WaitForExit();
            Assert.Fail("The second launch kept running instead of handing over.");
        }

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
    public bool IsShown(string automationId)
    {
        try
        {
            var element = Window.FindFirst(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.AutomationIdProperty, automationId));
            return element is not null && !element.Current.IsOffscreen;
        }
        catch (ElementNotAvailableException)
        {
            // WinUI can replace visualizer controls after FindFirst and before IsOffscreen.
            // Treat a stale element as absent so a polling wait can query the new tree.
            return false;
        }
    }

    public string Text(string automationId) => Find(automationId).Current.Name;

    public string Title
    {
        get
        {
            _process.Refresh();
            return _process.MainWindowTitle;
        }
    }

    /// <summary>Runs a command through the player's automation pipe, as a script would (TOOL-03).</summary>
    public Rex.Media.AppCore.Automation.AutomationReply Run(string command)
    {
        var reply = Rex.Media.AppCore.Automation.AutomationPipe.SendAsync(
            "rexplayer-test-" + Path.GetFileName(Root),
            new Rex.Media.AppCore.Automation.AutomationRequest { Command = "run", Id = command },
            Patience).GetAwaiter().GetResult();
        Assert.True(reply is { Ok: true }, $"The player did not run {command}: {reply?.Error ?? "no answer"}");
        return reply!;
    }

    /// <summary>Presses a button or menu item; one of a set of choices (a radio item) is chosen or ticked instead.</summary>
    public void Press(string automationId)
    {
        var element = Find(automationId);
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

    public void Toggle(string automationId) => ((TogglePattern)Find(automationId).GetCurrentPattern(TogglePattern.Pattern)).Toggle();

    public void SetValue(string automationId, double value) => ((RangeValuePattern)Find(automationId).GetCurrentPattern(RangeValuePattern.Pattern)).SetValue(value);

    /// <summary>Closes the window as its close button does (WM_CLOSE) and waits for the process to end.</summary>
    public int Close()
    {
        // A window busy at that moment (finishing a dialog, say) can miss the first request.
        _process.CloseMainWindow();
        if (!_process.WaitForExit(TimeSpan.FromSeconds(5)))
        {
            _process.Refresh();
            _process.CloseMainWindow();
        }

        Assert.True(_process.WaitForExit(Patience), "rexplayer did not close. Its log ends:" + LogTail);

        // A crash on the way out says what the log saw last.
        Assert.True(_process.ExitCode == 0, $"rexplayer closed with 0x{_process.ExitCode:X8}. Its log ends:" + LogTail);
        return _process.ExitCode;
    }

    private string LogTail => Environment.NewLine + string.Join(Environment.NewLine, LogText.Split(Environment.NewLine).TakeLast(15));

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
