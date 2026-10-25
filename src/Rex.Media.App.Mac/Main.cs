namespace Rex.Media.App.Mac;

/// <summary>rexplayer.app's entry point: AppKit's run loop, with <see cref="AppHost"/> doing the rest.</summary>
public static class Program
{
    public static void Main(string[] args)
    {
        NSApplication.Init();
        NSApplication.SharedApplication.Delegate = new AppHost();
        NSApplication.Main(args);
    }
}
