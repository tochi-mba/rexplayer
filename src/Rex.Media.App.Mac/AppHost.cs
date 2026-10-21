using Rex.Media.AppCore.Automation;
using Rex.Media.AppCore.Platform;
using Rex.Media.Diagnostics;

namespace Rex.Media.App.Mac;

/// <summary>
/// The app's life on a Mac: where its data lives (the same files as on Windows, under
/// Application Support), its log, the one window, files handed over by Finder and the Dock, and
/// the automation pipe scripts and later launches use (TOOL-03). What the player decides is in
/// Rex.Media.AppCore, as on Windows.
/// </summary>
[Register("AppDelegate")]
public sealed class AppHost : NSApplicationDelegate
{
    private readonly CancellationTokenSource _closing = new();
    private readonly List<string> _early = [];
    private MainWindow? _window;

    public static DataRoots Roots { get; } = DataRoots.ForCurrentProcess();

    public static RexLog Log { get; } = new(new RexLogOptions { Directory = Roots.LogFolder, FileName = "rexplayer.log", Minimum = LogLevel.Debug, MaxBytes = 8 * 1024 * 1024, KeepFiles = 5 });

    public override void DidFinishLaunching(NSNotification notification)
    {
        Log.Info("app", $"rexplayer starting on {System.Runtime.InteropServices.RuntimeInformation.OSDescription} ({System.Runtime.InteropServices.RuntimeInformation.OSArchitecture}), .NET {Environment.Version}, data in {Roots.Root}.");
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log.Critical("app", e.ExceptionObject is Exception ex ? ex.ToString() : "An unknown failure.");
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Log.Error("app", "A background task failed: " + e.Exception);
            e.SetObserved();
        };

        _window = new MainWindow(Roots, Log);
        _window.Show(_early);
        _early.Clear();
        try
        {
            _ = AutomationPipe.ServeAsync(AutomationPipe.NameForCurrentUser, _window.AnswerAsync, _closing.Token);
        }
        catch (IOException ex)
        {
            Log.Info("app", "Another player answers the automation pipe: " + ex.Message);
        }
    }

    /// <summary>Files opened from Finder or dropped on the Dock icon, before or after the window exists.</summary>
    public override void OpenFiles(NSApplication sender, string[] filenames)
    {
        if (_window is null)
        {
            _early.AddRange(filenames);
        }
        else
        {
            _window.OpenHandedOver(filenames);
        }

        sender.ReplyToOpenOrPrint(NSApplicationDelegateReply.Success);
    }

    public override bool ApplicationShouldTerminateAfterLastWindowClosed(NSApplication sender) => true;

    public override void WillTerminate(NSNotification notification)
    {
        _closing.Cancel();
        _window?.Dispose();
        Log.Info("app", "rexplayer closed.");
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _closing.Dispose();
        }

        base.Dispose(disposing);
    }
}
