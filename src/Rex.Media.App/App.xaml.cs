using Microsoft.UI.Xaml;
using Rex.Media.AppCore.Automation;
using Rex.Media.Diagnostics;
using Rex.Media.Interop.Windowing;
using Rex.Media.Settings;

namespace Rex.Media.App;

/// <summary>
/// rexplayer's entry point: finds where its data lives, opens the log, and either hands the command
/// line to a player that is already running (one window, TOOL-06) or shows its own window and
/// listens on the automation pipe for later launches and scripts (TOOL-03). Everything the window
/// does is in <see cref="MainWindow"/>; what it decides is in Rex.Media.AppCore.
/// </summary>
public partial class App : Application
{
    private static readonly TimeSpan FindRunningPlayer = TimeSpan.FromMilliseconds(300);

    public App()
    {
        InitializeComponent();
        UnhandledException += (_, e) => Log.Critical("app", ExceptionDiagnostics.Summary(e.Exception));

        // Off the window's thread too: the log is the only trace a crash on an engine thread leaves.
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log.Critical("app", e.ExceptionObject is Exception ex ? ex.ToString() : "An unknown failure.");

        // A failed task nobody waited for would otherwise vanish without a trace.
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Log.Error("app", "A background task failed: " + e.Exception);
            e.SetObserved();
        };
    }

    /// <summary>
    /// Where settings and logs live: REXPLAYER_ROOT when set (tests point it at a scratch folder),
    /// else %LocalAppData%\REX\rexplayer.
    /// </summary>
    public static string DataRoot { get; } = Environment.GetEnvironmentVariable("REXPLAYER_ROOT") is { Length: > 0 } root
        ? root
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "REX", "rexplayer");

    /// <summary>When this launch started, which says whether its files belong with another launch's.</summary>
    public static DateTimeOffset LaunchedAt { get; } = new(System.Diagnostics.Process.GetCurrentProcess().StartTime);

    public static string SettingsPath { get; } = Path.Combine(DataRoot, "settings.json");

    /// <summary>The folder the log is written in, which Help, Open the log folder shows.</summary>
    public static string LogFolder { get; } = Path.Combine(DataRoot, "logs");

    /// <summary>
    /// The log, in detail: every command, every item and its tracks, every decoder chosen or refused,
    /// so a problem can be diagnosed from the file alone. Files roll over at 8 MB; the last five are kept.
    /// </summary>
    public static RexLog Log { get; } = new(new RexLogOptions { Directory = LogFolder, FileName = "rexplayer.log", Minimum = LogLevel.Debug, MaxBytes = 8 * 1024 * 1024, KeepFiles = 5 });

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        Log.Info("app", $"rexplayer {MainWindow.Version} starting on {System.Runtime.InteropServices.RuntimeInformation.OSDescription} ({System.Runtime.InteropServices.RuntimeInformation.OSArchitecture}), .NET {Environment.Version}, data in {DataRoot}.");
        Log.Debug("app", "Command line: " + string.Join(" ", Environment.GetCommandLineArgs().Skip(1).Select(arg => "\"" + arg + "\"")));
        var files = Environment.GetCommandLineArgs().Skip(1).Select(Located).ToList();
        var settings = SettingsStore.Load(SettingsPath);
        var pipe = AutomationPipe.NameForCurrentUser;
        if (settings.SingleInstance)
        {
            if (OperatingSystem.IsWindows())
            {
                Foreground.AllowAnyProcess();
            }

            var reply = await AutomationPipe.SendAsync(pipe, new AutomationRequest { Command = "open", Paths = files, Enqueue = settings.EnqueueFromSecondLaunch, LaunchedAt = LaunchedAt }, FindRunningPlayer);
            if (reply is { Ok: true })
            {
                Log.Info("app", "Handed over to the player that was already running.");
                Exit();
                return;
            }
        }

        var window = new MainWindow(files);
        var closing = new CancellationTokenSource();
        window.Closed += (_, _) => closing.Cancel();
        window.Activate();
        try
        {
            _ = AutomationPipe.ServeAsync(pipe, window.AnswerAsync, closing.Token);
        }
        catch (IOException ex)
        {
            // Another player owns the pipe (one window was turned off): this one simply has no pipe.
            Log.Info("app", "Another player answers the automation pipe: " + ex.Message);
        }
    }

    /// <summary>A path given relative to where the launch happened, made absolute so another process can open it.</summary>
    private static string Located(string argument) =>
        Uri.TryCreate(argument, UriKind.Absolute, out var uri) && !uri.IsFile ? argument : Path.GetFullPath(argument);
}
