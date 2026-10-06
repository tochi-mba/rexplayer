using System.Reflection;
using Rex.Media.AppCore.Cli;
using Rex.Media.Audio.Wasapi;
using Rex.Media.Codecs.MediaFoundation;
using Rex.Media.Interop.Windowing;
using Rex.Media.Video;
using Rex.Media.Video.D3D11;

// The composition root of rexplay: the console, Ctrl+C, the Windows audio device, Windows' own
// decoders and a Direct3D window for pictures. Everything else is the command line in
// Rex.Media.AppCore, which the tests run in-process.
using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cancellation.Cancel();
};

var version = typeof(CliApplication).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "0.0.0";
var host = new CliHost
{
    Out = Console.Out,
    Error = Console.Error,
    Version = version,
    DefaultAudioSink = () => new WasapiAudioSink(),
    ExtraDecoders = [new MfDecoderFactory()],
    VideoWindow = OpenWindow,
    Cancellation = cancellation.Token,
};

return CliApplication.Run(args, host);

// A window for pictures; closing it (or pressing Escape) stops playback as Ctrl+C does.
IVideoPresenter? OpenWindow(string title)
{
    if (!OperatingSystem.IsWindowsVersionAtLeast(10))
    {
        return null;
    }

    var window = VideoWindow.Open(title, 960, 540);
    window.Closed += (_, _) => cancellation.Cancel();
    return D3D11Presenter.ForWindow(window);
}
