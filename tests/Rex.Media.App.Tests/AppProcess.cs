using System.Diagnostics;
using System.Globalization;
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

    /// <summary>The tested app process, for compositor occlusion diagnostics.</summary>
    public int ProcessId => _process.Id;

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

    public string LogText
    {
        get
        {
            var path = Path.Combine(Root, "logs", "rexplayer.log");
            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    if (!File.Exists(path))
                    {
                        return "";
                    }

                    using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    using var reader = new StreamReader(stream);
                    return reader.ReadToEnd();
                }
                catch (IOException) when (attempt < 20)
                {
                    Thread.Sleep(50);
                }
            }
        }
    }

    /// <summary>The built app; the suite needs it built first (./dev.ps1 build or the CI step that does).</summary>
    public static string Executable => RepoPaths.Combine("src", "Rex.Media.App", "bin", RepoPaths.Configuration, "net10.0-windows10.0.22621.0", "win-x64", "rexplayer.exe");

    /// <summary>The version the app reports, from Directory.Build.props.</summary>
    public static string Version { get; } = System.Text.RegularExpressions.Regex.Match(File.ReadAllText(RepoPaths.Combine("Directory.Build.props")), "<Version>(.+?)</Version>").Groups[1].Value;

    /// <summary>
    /// Starts the app with <paramref name="arguments"/>, reusing <paramref name="root"/> when given.
    /// A new root starts as someone who has used rexplayer before (no welcome, nothing new to show)
    /// unless <paramref name="firstRun"/> asks for the very first start. <paramref name="extraSettings"/>
    /// adds JSON properties to a new root's settings, such as <c>"theme": "Dark"</c>, and
    /// <paramref name="environment"/> adds variables, such as REXPLAYER_FAKE_SPEECH.
    /// </summary>
    public static AppProcess Start(string[]? arguments = null, string? root = null, bool firstRun = false, string? extraSettings = null, IReadOnlyDictionary<string, string>? environment = null)
    {
        root ??= Path.Combine(Path.GetTempPath(), "rexplayer-ui-" + Guid.NewGuid().ToString("N"));
        var settings = Path.Combine(root, "settings.json");
        if (!firstRun && !File.Exists(settings))
        {
            Directory.CreateDirectory(root);
            File.WriteAllText(settings, $$"""{ "firstRunDone": true, "lastSeenVersion": "{{Version}}", "updateChecks": "Off"{{(extraSettings is null ? "" : ", " + extraSettings)}} }""");
        }

        // Decide before launching: reading a settings file during startup can race
        // the app writing a new window placement after its first layout.
        var keepSavedPlacement = File.Exists(settings)
            && Rex.Media.Settings.SettingsStore.Load(settings).Window is not null;
        var start = StartInfo(arguments, root);
        foreach (var (name, value) in environment ?? new Dictionary<string, string>())
        {
            start.Environment[name] = value;
        }

        var process = Process.Start(start)!;
        var window = Wait.Until(() =>
        {
            process.Refresh();
            if (process.HasExited)
            {
                var diagnostic = Path.Combine(root, "startup-error.txt");
                var reason = File.Exists(diagnostic) ? File.ReadAllText(diagnostic) : "(no startup exception was recorded)";
                Assert.Fail($"rexplayer exited with {process.ExitCode} before showing its window. Startup failure: {reason}");
            }
            if (process.MainWindowHandle == 0)
            {
                return null;
            }

            // Early on the main window can be a passing helper window: only the player's own will do.
            var candidate = AutomationElement.FromHandle(process.MainWindowHandle);
            return candidate.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, "PlayPauseButton")) is null ? null : candidate;
        });
        var app = new AppProcess(process, root, window);
        // Desktop screenshots must never include pixels outside the visible virtual
        // screen. Position the isolated test process before tests open modal popups,
        // since moving an already opened flyout can dismiss it.
        // Tests of persisted window placement must exercise the app's own
        // recovery logic, not the screenshot harness's positioning helper.
        if ((extraSettings is null || !extraSettings.Contains("\"window\"", StringComparison.Ordinal))
            && !keepSavedPlacement)
        {
            app.FitOnDesktop();
        }

        return app;
    }

    private void FitOnDesktop()
    {
        // Even an initially visible window can end up clipped when a test later
        // resizes it to 1000x760. Always anchor tests near the virtual desktop's
        // upper-left corner, not only after an already-clipped startup.
        var desktop = WindowPicture.DesktopBounds;
        const int Padding = 8;
        // Allow a fixed 1000x760 scene at (8,8) on a 1024x768 runner.
        // Keeping a top-left inset avoids losing the invisible resize border.
        var availableWidth = Math.Max(1, desktop.Width - Padding);
        var availableHeight = Math.Max(1, desktop.Height - Padding);
        var bounds = WindowPicture.Bounds(Handle);
        var transform = (TransformPattern)Window.GetCurrentPattern(TransformPattern.Pattern);
        if (bounds.Width > availableWidth || bounds.Height > availableHeight)
        {
            Assert.True(transform.Current.CanResize, "The app is too large for the capture desktop and cannot resize.");
            transform.Resize(Math.Min(bounds.Width, availableWidth), Math.Min(bounds.Height, availableHeight));
        }

        Assert.True(transform.Current.CanMove, "The app cannot be moved fully onto the capture desktop.");
        transform.Move(desktop.Left + Padding, desktop.Top + Padding);
        Wait.For(() => WindowPicture.IsFullyOnDesktop(Handle),
            "the test window to fit completely on the visible desktop");
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
        // Freeze time-driven Browse animation while keeping real WinUI and media fixtures.
        // Actual PNGs are still captured and compared by Screenshots.Match.
        start.Environment["REXPLAYER_UI_CAPTURE"] = "1";
        start.Environment["REXPLAYER_PIPE_NAME"] = "rexplayer-test-" + Path.GetFileName(root);
        start.Environment["REXPLAYER_STARTUP_ERROR_FILE"] = Path.Combine(root, "startup-error.txt");
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

    /// <summary>
    /// The control's text. A control WinUI replaces between finding it and reading it (the subtitle line
    /// as playback starts) is found again, so a read never fails on a stale element.
    /// </summary>
    public string Text(string automationId) => Wait.Until(() =>
    {
        try
        {
            return Find(automationId).Current.Name;
        }
        catch (ElementNotAvailableException)
        {
            return null;
        }
    });

    public string Title
    {
        get
        {
            _process.Refresh();
            return _process.MainWindowTitle;
        }
    }

    /// <summary>Runs a command through the player's automation pipe, as a script would (TOOL-03).</summary>
    public Rex.Media.AppCore.Automation.AutomationReply Run(string command) =>
        Send(new Rex.Media.AppCore.Automation.AutomationRequest { Command = "run", Id = command }, command);

    /// <summary>What the player says it is doing now: its state, title, position and length, in seconds.</summary>
    public Rex.Media.AppCore.Automation.AutomationReply Status() =>
        Send(new Rex.Media.AppCore.Automation.AutomationRequest { Command = "status" }, "status");

    /// <summary>
    /// Asks the window itself through the pipe (protocol 2's <c>ui.</c> commands), as the
    /// cross-platform window suite will; refused requests come back for the test to inspect.
    /// </summary>
    public Rex.Media.AppCore.Automation.AutomationReply Ui(Rex.Media.AppCore.Automation.AutomationRequest request) =>
        Rex.Media.AppCore.Automation.AutomationPipe.SendAsync("rexplayer-test-" + Path.GetFileName(Root), request, Patience).GetAwaiter().GetResult()
            ?? throw new InvalidOperationException("The player did not answer " + request.Command + ".");

    /// <summary>Makes the window's own code fail where Windows calls it back (REXPLAYER_FAKE_FAULTS=1 only).</summary>
    public void Fault(string where) =>
        Send(new Rex.Media.AppCore.Automation.AutomationRequest { Command = "fault", Id = where }, "the fault " + where);

    /// <summary>Picture requests (cards, banners, playlist lines) the window still has queued or loading.</summary>
    public int PicturesPending => Status().PicturesPending ?? 0;

    /// <summary>The playing position in seconds, as the player reports it.</summary>
    public double Position => Status().Position ?? 0;

    private Rex.Media.AppCore.Automation.AutomationReply Send(Rex.Media.AppCore.Automation.AutomationRequest request, string what)
    {
        var reply = Rex.Media.AppCore.Automation.AutomationPipe.SendAsync(
            "rexplayer-test-" + Path.GetFileName(Root), request, Patience).GetAwaiter().GetResult();
        Assert.True(reply is { Ok: true }, $"The player did not run {what}: {reply?.Error ?? "no answer"}");
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

    /// <summary>
    /// Opens the menus along <paramref name="path"/> (a menu bar item, then any submenus) until
    /// <paramref name="item"/> is on screen and enabled, and returns it. A flyout opens on the
    /// window's thread after Expand returns, and one still closing from the last choice swallows a
    /// new Expand (its menu still reads Expanded, or the opening flyout closes with the old one), so
    /// each level is expanded again, from a fresh look-up, until the next level really shows.
    /// </summary>
    public AutomationElement OpenMenu(string item, params string[] path) => OpenMenu(ById(item), item, true, path);

    /// <summary>
    /// <see cref="OpenMenu(string, string[])"/> for an item found by <paramref name="item"/> (a list
    /// item by its name, say), described as <paramref name="what"/> if it never shows. A long drop-down
    /// list keeps items scrolled out of view off screen, so <paramref name="itemOnScreen"/> false asks
    /// only that the item is in the open list.
    /// </summary>
    public AutomationElement OpenMenu(Condition item, string what, bool itemOnScreen, params string[] path)
    {
        var clock = Stopwatch.StartNew();
        var tried = new List<string>();
        while (true)
        {
            if (Reachable(item, itemOnScreen) is { } found)
            {
                return found;
            }

            for (var level = 0; level < path.Length; level++)
            {
                var (next, onScreen) = level + 1 < path.Length ? (ById(path[level + 1]), true) : (item, itemOnScreen);
                if (Reachable(next, onScreen) is not null)
                {
                    continue;
                }

                if (!TryExpand(path[level], next, onScreen, tried))
                {
                    break;
                }
            }

            Assert.True(clock.Elapsed < Patience,
                $"The menu {string.Join(" > ", path)} never offered {what}. Tried: {string.Join("; ", tried.TakeLast(12))}");
        }
    }

    /// <summary>Expands <paramref name="menu"/> and waits a moment for <paramref name="next"/> inside it; false if it did not come.</summary>
    private bool TryExpand(string menu, Condition next, bool onScreen, List<string> tried)
    {
        if (Reachable(ById(menu)) is not { } element)
        {
            tried.Add(menu + " not on screen yet");
            Thread.Sleep(50);
            return false;
        }

        try
        {
            var expander = (ExpandCollapsePattern)element.GetCurrentPattern(ExpandCollapsePattern.Pattern);
            var state = expander.Current.ExpandCollapseState;
            if (state == ExpandCollapseState.Expanded)
            {
                // Open yet without the next level: a flyout on its way out. Close it and start afresh.
                expander.Collapse();
            }

            expander.Expand();
            tried.Add($"{menu} was {state}, expanded");
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or InvalidOperationException)
        {
            // Replaced or disabled between the look-up and the call: look it up again.
            tried.Add($"{menu}: {ex.GetType().Name}");
            return false;
        }

        if (Wait.Within(TimeSpan.FromSeconds(2), () => Reachable(next, onScreen) is not null))
        {
            return true;
        }

        tried.Add(menu + " showed nothing");
        return false;
    }

    private static PropertyCondition ById(string automationId) => new(AutomationElement.AutomationIdProperty, automationId);

    /// <summary>The control matching <paramref name="condition"/> if it is in the tree, enabled and (unless <paramref name="onScreen"/> is false) on screen now; null otherwise (a stale element included).</summary>
    private AutomationElement? Reachable(Condition condition, bool onScreen = true)
    {
        try
        {
            var element = Window.FindFirst(TreeScope.Descendants, condition);
            return element is { Current.IsEnabled: true } && !(onScreen && element.Current.IsOffscreen) ? element : null;
        }
        catch (ElementNotAvailableException)
        {
            return null;
        }
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
        Assert.True(_process.ExitCode == 0, $"rexplayer closed with 0x{_process.ExitCode:X8}. Its log ends:" + LogTail
            + CrashReport(_process.Id, DateTime.UtcNow.AddMinutes(-5)));
        return _process.ExitCode;
    }

    /// <summary>
    /// What Windows recorded about this process crashing (Application Error, .NET Runtime and
    /// Windows Error Reporting events name the faulting module and the stowed exception), so a crash
    /// after the log's last line still says where it happened.
    /// </summary>
    private static string CrashReport(int processId, DateTime started)
    {
        try
        {
            var since = started.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);
            var query = new System.Diagnostics.Eventing.Reader.EventLogQuery("Application", System.Diagnostics.Eventing.Reader.PathType.LogName,
                $"*[System[(Provider[@Name='Application Error'] or Provider[@Name='.NET Runtime'] or Provider[@Name='Windows Error Reporting']) and TimeCreated[@SystemTime>='{since}']]]");
            using var reader = new System.Diagnostics.Eventing.Reader.EventLogReader(query);
            var found = new List<string>();
            for (var record = reader.ReadEvent(); record is not null; record = reader.ReadEvent())
            {
                using (record)
                {
                    var text = record.FormatDescription() ?? string.Join(" | ", record.Properties.Select(property => property.Value));
                    if (text.Contains("rexplayer", StringComparison.OrdinalIgnoreCase)
                        && (text.Contains(processId.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
                            || text.Contains(processId.ToString("x", CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase)))
                    {
                        found.Add($"{record.ProviderName}: {text}");
                    }
                }
            }

            return found.Count == 0 ? "" : Environment.NewLine + "Windows recorded:" + Environment.NewLine + string.Join(Environment.NewLine, found);
        }
        catch (Exception error) when (error is System.Diagnostics.Eventing.Reader.EventLogException or UnauthorizedAccessException or InvalidOperationException)
        {
            return Environment.NewLine + "The event log could not be read: " + error.Message;
        }
    }

    private string LogTail => Environment.NewLine + string.Join(Environment.NewLine, LogText.Split(Environment.NewLine).TakeLast(15));

    public void Dispose()
    {
        if (!_process.HasExited)
        {
            _process.Kill();
            _process.WaitForExit();
        }

        // Preserve a per-process log even if a test fails before it reaches
        // Screenshots.Match. CI publishes this with the screenshot artifact.
        try
        {
            var directory = RepoPaths.Combine("artifacts", "screenshots", Screenshots.Lane, "logs");
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, Path.GetFileName(Root) + ".log"), LogText);
        }
        catch (IOException)
        {
            // A locked log must not hide the test failure being diagnosed.
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

    /// <summary>Whether <paramref name="condition"/> came true within <paramref name="limit"/>, for a step that is retried when it did not.</summary>
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

    public static void For(Func<bool> condition, string what, Func<string>? diagnostic = null)
    {
        var clock = Stopwatch.StartNew();
        while (!condition())
        {
            if (clock.Elapsed >= AppProcess.Patience)
            {
                // Resolve extra context only after a timeout, not on every UI poll.
                Assert.Fail($"Waited in vain for {what}.{(diagnostic is null ? "" : " " + diagnostic())}");
            }

            Thread.Sleep(50);
        }
    }
}
