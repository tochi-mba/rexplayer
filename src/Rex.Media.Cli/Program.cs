using System.Reflection;
using Rex.Media.AppCore.Cli;
using Rex.Media.Audio.Wasapi;
using Rex.Media.AppCore;
using Rex.Media.Codecs.MediaFoundation;
using Rex.Media.Interop.Audio;
using Rex.Media.Interop.Graphics;
using Rex.Media.Primitives;
using Rex.Media.Interop.Windowing;
using Rex.Media.Video;
using Rex.Media.Video.D3D11;

// The composition root of rexplay: the console, Ctrl+C, the Windows audio device, Windows' own
// decoders and a Direct3D window for pictures. Everything else is the command line in
// Rex.Media.AppCore, which the tests run in-process.
// Codec names and file names are not all ASCII; the console's code page would mangle them.
Console.OutputEncoding = System.Text.Encoding.UTF8;
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
    ExtraDecoders = [new MfDecoderFactory(), new WicDecoderFactory()],
    VideoWindow = OpenWindow,
    SystemReport = Describe,
    Cancellation = cancellation.Token,
};

return CliApplication.Run(args, host);

// What this PC offers: Windows' decoders, the graphics adapter and the default audio output.
SystemReport Describe()
{
    string? graphics = null;
    var software = false;
    try
    {
        if (OperatingSystem.IsWindowsVersionAtLeast(10))
        {
            using var renderer = D3D11VideoRenderer.Create(software: false);
            (graphics, software) = (renderer.AdapterName, renderer.IsSoftware);
        }
    }
    catch (InvalidOperationException)
    {
        // No Direct3D at all: the report says so.
    }

    AudioFormat? output = null;
    using (var client = WasapiRenderClient.OpenDefault(TimeSpan.FromMilliseconds(100)))
    {
        if (client is not null)
        {
            output = new AudioFormat(client.Format.SampleRate, client.Format.Channels, SampleFormat.F32);
        }
    }

    return new SystemReport
    {
        Windows = Environment.OSVersion.Version.ToString(),
        Decoders = [.. MfDecoderFactory.Survey().Select(entry => new WindowsDecoders(entry.Codec, entry.Software, entry.Hardware))],
        Graphics = graphics,
        GraphicsInSoftware = software,
        AudioOutput = output,
    };
}

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
