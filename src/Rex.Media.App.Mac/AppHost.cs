using Rex.Media.AppCore.Automation;
using Rex.Media.AppCore.Platform;
using Rex.Media.Diagnostics;
using Rex.Media.Settings;

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
    private NSObject? _sleepSubscription;
    private NSObject? _wakeSubscription;

    public static DataRoots Roots { get; } = DataRoots.ForCurrentProcess();

    public static RexLog Log { get; } = new(new RexLogOptions { Directory = Roots.LogFolder, FileName = "rexplayer.log", Minimum = LogLevel.Debug, MaxBytes = 8 * 1024 * 1024, KeepFiles = 5 });

    public override void DidFinishLaunching(NSNotification notification)
    {
        ConfigureCrashDumps(SettingsStore.Load(Roots.SettingsPath).LocalCrashDumps);
        Log.Info("app", $"rexplayer starting on {System.Runtime.InteropServices.RuntimeInformation.OSDescription} ({System.Runtime.InteropServices.RuntimeInformation.OSArchitecture}), .NET {Environment.Version}, data in {Roots.Root}.");
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log.Critical("app", e.ExceptionObject is Exception ex ? ex.ToString() : "An unknown failure.");
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Log.Error("app", "A background task failed: " + e.Exception);
            e.SetObserved();
        };

        _window = new MainWindow(Roots, Log);
        _sleepSubscription = NSWorkspace.Notifications.ObserveWillSleep((_, _) => _window?.OnMacWillSleep());
        _wakeSubscription = NSWorkspace.Notifications.ObserveDidWake((_, _) => _window?.OnMacDidWake());
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

    internal static void ConfigureCrashDumps(bool enabled)
    {
        if (enabled)
        {
            var folder = Path.Combine(Roots.Root, "Crash Dumps");
            Directory.CreateDirectory(folder);
            Environment.SetEnvironmentVariable("DOTNET_DbgEnableMiniDump", "1");
            Environment.SetEnvironmentVariable("DOTNET_DbgMiniDumpType", "2");
            Environment.SetEnvironmentVariable("DOTNET_DbgMiniDumpName",
                Path.Combine(folder, "rexplayer.%p.dmp"));
        }
        else
        {
            Environment.SetEnvironmentVariable("DOTNET_DbgEnableMiniDump", null);
            Environment.SetEnvironmentVariable("DOTNET_DbgMiniDumpType", null);
            Environment.SetEnvironmentVariable("DOTNET_DbgMiniDumpName", null);
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

    public override NSMenu ApplicationDockMenu(NSApplication sender)
    {
        // Build on demand so the Dock always sees the latest list and transport state.
        var menu = new NSMenu("rexplayer");
        if (_window is not { } window)
        {
            return menu;
        }

        menu.AddItem(new NSMenuItem("Play / pause", (_, _) => window.ExecuteDockCommand(
            Rex.Media.AppCore.Commands.CommandCatalog.PlayPause)));
        menu.AddItem(new NSMenuItem("Previous", (_, _) => window.ExecuteDockCommand(
            Rex.Media.AppCore.Commands.CommandCatalog.Previous)));
        menu.AddItem(new NSMenuItem("Next", (_, _) => window.ExecuteDockCommand(
            Rex.Media.AppCore.Commands.CommandCatalog.Next)));
        menu.AddItem(NSMenuItem.SeparatorItem);
        menu.AddItem(new NSMenuItem("Open file…", (_, _) => window.ExecuteDockCommand(
            Rex.Media.AppCore.Commands.CommandCatalog.OpenFile)));
        menu.AddItem(new NSMenuItem("Open location…", (_, _) => window.ExecuteDockCommand(
            Rex.Media.AppCore.Commands.CommandCatalog.OpenLocation)));
        var recent = window.DockRecentMedia.Take(10).ToArray();
        if (recent.Length > 0)
        {
            var sub = new NSMenu("Recent media");
            foreach (var path in recent)
            {
                var location = path;
                sub.AddItem(new NSMenuItem(Path.GetFileName(location), (_, _) => window.OpenDockRecent(location)));
            }

            menu.AddItem(new NSMenuItem("Recent media") { Submenu = sub });
        }

        return menu;
    }

    public override bool ApplicationShouldTerminateAfterLastWindowClosed(NSApplication sender) => true;

    public override void WillTerminate(NSNotification notification)
    {
        _closing.Cancel();
        _sleepSubscription?.Dispose();
        _wakeSubscription?.Dispose();
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
