using Microsoft.UI.Xaml;
using Rex.Media.Diagnostics;

namespace Rex.Media.App;

/// <summary>
/// rexplayer's entry point: finds where its data lives, opens the log, and shows the main window with
/// whatever the command line named. Everything the window does is in <see cref="MainWindow"/>; what it
/// decides is in Rex.Media.AppCore, where the tests reach it.
/// </summary>
public partial class App : Application
{
    private MainWindow? _window;

    public App()
    {
        InitializeComponent();
        UnhandledException += (_, e) => Log.Critical("app", ExceptionDiagnostics.Summary(e.Exception));

        // Off the window's thread too: the log is the only trace a crash on an engine thread leaves.
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log.Critical("app", e.ExceptionObject is Exception ex ? ex.ToString() : "An unknown failure.");
    }

    /// <summary>
    /// Where settings and logs live: REXPLAYER_ROOT when set (tests point it at a scratch folder),
    /// else %LocalAppData%\REX\rexplayer.
    /// </summary>
    public static string DataRoot { get; } = Environment.GetEnvironmentVariable("REXPLAYER_ROOT") is { Length: > 0 } root
        ? root
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "REX", "rexplayer");

    public static RexLog Log { get; } = new(new RexLogOptions { Directory = Path.Combine(DataRoot, "logs"), FileName = "rexplayer.log" });

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        Log.Info("app", $"rexplayer {MainWindow.Version} starting.");
        _window = new MainWindow([.. Environment.GetCommandLineArgs().Skip(1)]);
        _window.Activate();
    }
}
