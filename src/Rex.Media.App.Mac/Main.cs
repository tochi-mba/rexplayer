using Rex.Media.AppCore.Automation;
using Rex.Media.AppCore.Platform;
using Rex.Media.Settings;

namespace Rex.Media.App.Mac;

/// <summary>rexplayer.app's entry point: AppKit's run loop, with <see cref="AppHost"/> doing the rest.</summary>
public static class Program
{
    public static void Main(string[] args)
    {
        if (HandOverToRunningPlayer(args))
        {
            return;
        }

        NSApplication.Init();
        NSApplication.SharedApplication.Delegate = new AppHost();
        NSApplication.Main(args);
    }

    private static bool HandOverToRunningPlayer(IReadOnlyList<string> arguments)
    {
        var options = arguments.Where(argument => argument.StartsWith("--", StringComparison.Ordinal)).ToHashSet(StringComparer.Ordinal);
        var settings = SettingsStore.Load(DataRoots.ForCurrentProcess().SettingsPath);
        if (!settings.SingleInstance || options.Contains("--new-instance"))
        {
            return false;
        }

        var pipe = AutomationPipe.NameForCurrentUser;
        if (!AutomationPipe.IsServed(pipe))
        {
            return false;
        }

        var paths = arguments
            .Where(argument => !argument.StartsWith('-'))
            .Select(Path.GetFullPath)
            .ToList();
        try
        {
            var reply = AutomationPipe.SendAsync(pipe, new AutomationRequest
            {
                Command = "open",
                Paths = paths,
                Enqueue = options.Contains("--enqueue") || settings.EnqueueFromSecondLaunch,
                LaunchedAt = DateTimeOffset.Now,
            }, TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();
            return reply?.Ok == true;
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or OperationCanceledException or UnauthorizedAccessException)
        {
            AppHost.Log.Info("app", "The running player did not take this launch: " + ex.Message);
            return false;
        }
    }
}
