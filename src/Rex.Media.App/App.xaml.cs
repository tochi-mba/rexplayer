using Microsoft.UI.Xaml;
using Rex.Media.AppCore.Automation;
using Rex.Media.AppCore.Platform;
using Rex.Media.AppCore.Shell;
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
    private PlayerSettings? _startupSettings;

    public App()
    {
        // Register handlers before loading settings, caches and shaders.
        UnhandledException += (_, e) =>
        {
            // A failure on the window's thread (an event handler, the rest of an awaited call) is
            // written down and the player carries on: one broken file or click must not close it.
            Log.Critical("app", e.Exception.ToString());
            e.Handled = true;
        };

        // Off the window's thread too: the log is the only trace a crash on an engine thread leaves.
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log.Critical("app", e.ExceptionObject is Exception ex ? ex.ToString() : "An unknown failure.");

        // A failed task nobody waited for would otherwise vanish without a trace.
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Log.Error("app", "A background task failed: " + e.Exception);
            e.SetObserved();
        };

        Milestones.Reach("app");

        // Compiling the picture shaders takes seconds; they are kept between runs.
        Rex.Media.Interop.Graphics.D3D11VideoRenderer.CompiledShaders = new Rex.Media.Video.CompiledShaderCache(Roots.ShaderCache).GetOrCompile;

        // A launch that will hand its files to a running player has nothing to warm up.
        if (!AutomationPipe.IsServed(AutomationPipe.NameForCurrentUser))
        {
            // Fastest and a weak Automatic profile warm only what an empty launch shows. Read the
            // settings here so an explicit mode governs startup too, then reuse the same read when
            // the window is created.
            _startupSettings = SettingsStore.Load(SettingsPath);
            var facts = new Rex.Media.AppCore.Performance.DeviceFacts
            {
                Processors = Environment.ProcessorCount,
                MemoryBytes = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes,
                Graphics = SoftwareGraphics ? Rex.Media.AppCore.Performance.GraphicsKind.Software : Rex.Media.AppCore.Performance.GraphicsKind.Unknown,
                HardwareDecoding = GraphicsCardDecoding ? null : [],
            };
            // One read of the energy saver status, so a chosen mode gives way to Automatic at startup too.
            var choice = Rex.Media.AppCore.Performance.EnergySaverMode.Start(_startupSettings.Performance, _startupSettings.AutomaticWhenEnergySaver).PowerChanged(Power.EnergySaver).Next.Effective;
            var tier = Rex.Media.AppCore.Performance.DeviceProfile.Choose(choice, facts);
            var lean = Rex.Media.AppCore.Performance.DeviceProfile.For(tier, facts).LeanLaunch;
            Warmup = LaunchWarmup.Start(Environment.GetCommandLineArgs().Length > 1, Environment.GetEnvironmentVariable("REXPLAYER_FAKE_AUDIO") != "1", lean);
        }

        InitializeComponent();

    }

    /// <summary>
    /// Where settings, logs and everything else rexplayer keeps live: REXPLAYER_ROOT when set
    /// (tests point it at a scratch folder), else the platform's folder for an application's data.
    /// </summary>
    public static DataRoots Roots { get; } = DataRoots.ForCurrentProcess();

    public static string DataRoot => Roots.Root;

    /// <summary>When this launch started, which says whether its files belong with another launch's.</summary>
    public static DateTimeOffset LaunchedAt { get; } = new(System.Diagnostics.Process.GetCurrentProcess().StartTime);

    public static string SettingsPath => Roots.SettingsPath;

    /// <summary>The folder the log is written in, which Help, Open the log folder shows.</summary>
    public static string LogFolder => Roots.LogFolder;

    /// <summary>
    /// The log, in detail: every command, every item and its tracks, every decoder chosen or refused,
    /// so a problem can be diagnosed from the file alone. Files roll over at 8 MB; the last five are kept.
    /// Lines are written behind, so the window's thread never waits for the disk to log.
    /// </summary>
    public static RexLog Log { get; } = new(new RexLogOptions { Directory = LogFolder, FileName = "rexplayer.log", Minimum = LogLevel.Debug, MaxBytes = 8 * 1024 * 1024, KeepFiles = 5, WriteBehind = true });

    /// <summary>
    /// The timing lines of the log: how long since the process started each moment of the launch
    /// and of opening media came. REXPLAYER_EXIT_AFTER names one ("first-frame", or "first-frame*2"
    /// for the second) to exit at, which is how scripts/measure-startup.ps1 measures a launch.
    /// </summary>
    public static Milestones Milestones { get; } = new(Log, LaunchedAt, Environment.GetEnvironmentVariable("REXPLAYER_EXIT_AFTER"), () => Environment.Exit(0));

    /// <summary>
    /// Draws pictures in software, as a computer without a usable graphics card does, when
    /// REXPLAYER_GRAPHICS=software: how scripts/measure-startup.ps1 -LowEnd imitates a weak computer.
    /// </summary>
    public static bool SoftwareGraphics { get; } = string.Equals(Environment.GetEnvironmentVariable("REXPLAYER_GRAPHICS"), "software", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Whether video may be decoded on the graphics card: not when REXPLAYER_HARDWARE_DECODING=off,
    /// which imitates a graphics card with no video decoding (scripts/measure-startup.ps1 -LowEnd).
    /// </summary>
    public static bool GraphicsCardDecoding { get; } = !string.Equals(Environment.GetEnvironmentVariable("REXPLAYER_HARDWARE_DECODING"), "off", StringComparison.OrdinalIgnoreCase);

    /// <summary>The battery and Windows' energy saver, as Windows reports them (or as REXPLAYER_FAKE_ENERGY_SAVER forces the latter).</summary>
    public static Rex.Media.AppCore.Performance.IPowerSource Power { get; } = WindowsPowerSource.Create();

    /// <summary>The slow parts of this launch, started in the background before the window is built; null when it hands over.</summary>
    internal static LaunchWarmup? Warmup { get; private set; }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            await LaunchAsync();
        }
        catch (Exception ex)
        {
            // Nothing was shown, so there is nothing to carry on with: the log says why, and the
            // process ends instead of staying behind without a window.
            Log.Critical("app", "rexplayer could not start: " + ex);
            Exit();
        }
    }

    private async Task LaunchAsync()
    {
        Log.Info("app", $"rexplayer {MainWindow.Version} starting on {System.Runtime.InteropServices.RuntimeInformation.OSDescription} ({System.Runtime.InteropServices.RuntimeInformation.OSArchitecture}), .NET {Environment.Version}, data in {DataRoot}.");
        Milestones.Reach("launched");
        Log.Debug("app", "Command line: " + string.Join(" ", Environment.GetCommandLineArgs().Skip(1).Select(arg => "\"" + arg + "\"")));
        var (launchFiles, launchCommand) = JumpListPlan.ParseLaunch(Environment.GetCommandLineArgs().Skip(1).ToArray());
        var files = launchFiles.Select(Located).ToList();
        var settings = _startupSettings ?? SettingsStore.Load(SettingsPath);
        _startupSettings = null;
        var pipe = AutomationPipe.NameForCurrentUser;

        // Only a player that is there can be handed to: with none, there is nothing to wait for.
        if (settings.SingleInstance && AutomationPipe.IsServed(pipe) && await HandOverAsync(pipe, files, settings, launchCommand))
        {
            Exit();
            return;
        }

        Milestones.Reach("handed-over-checked");
        var closing = new CancellationTokenSource();
        var answering = new TaskCompletionSource<MainWindow>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            // Served before the window is built, so a launch a moment after this one finds the pipe
            // and hands its files over; they are answered once the window has opened its own.
            MainWindow.Forget(AutomationPipe.ServeAsync(pipe, async request => await (await answering.Task).AnswerWhenReadyAsync(request), closing.Token));
        }
        catch (IOException ex)
        {
            // Another launch began serving after this one looked; or one window was turned off.
            if (settings.SingleInstance && await HandOverAsync(pipe, files, settings, launchCommand))
            {
                Exit();
                return;
            }

            Log.Info("app", "Another player answers the automation pipe: " + ex.Message);
        }

        var window = new MainWindow(files, settings, launchCommand);
        window.Closed += (_, _) => closing.Cancel();
        answering.SetResult(window);
        window.Activate();
        Milestones.Reach("activated");
    }

    /// <summary>Gives the files to the player already running; false when it does not take them.</summary>
    private static async Task<bool> HandOverAsync(string pipe, List<string> files, PlayerSettings settings, string? command)
    {
        if (OperatingSystem.IsWindows())
        {
            Foreground.AllowAnyProcess();
        }

        AutomationReply? reply;
        try
        {
            // A jump list task first brings the window forward (an open with nothing to open does), so
            // the dialog it may show is in front, then runs.
            reply = await AutomationPipe.SendAsync(pipe, new AutomationRequest { Command = "open", Paths = files, Enqueue = settings.EnqueueFromSecondLaunch, LaunchedAt = LaunchedAt }, FindRunningPlayer);
            if (reply is { Ok: true } && command is not null)
            {
                reply = await AutomationPipe.SendAsync(pipe, new AutomationRequest { Command = "run", Id = command }, FindRunningPlayer);
            }
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or OperationCanceledException or UnauthorizedAccessException or InvalidOperationException)
        {
            // The other player went away or does not answer: this one opens its own window.
            Log.Info("app", "The running player did not take the files: " + ex.Message);
            return false;
        }

        if (reply is not { Ok: true })
        {
            return false;
        }

        Log.Info("app", "Handed over to the player that was already running.");
        return true;
    }

    /// <summary>A path given relative to where the launch happened, made absolute so another process can open it.</summary>
    private static string Located(string argument) =>
        Uri.TryCreate(argument, UriKind.Absolute, out var uri) && !uri.IsFile ? argument : Path.GetFullPath(argument);
}
