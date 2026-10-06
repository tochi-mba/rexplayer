using System.Reflection;
using Rex.Media.AppCore.Cli;
using Rex.Media.Audio.Wasapi;
using Rex.Media.Codecs.MediaFoundation;

// The composition root of rexplay: the console, Ctrl+C, the Windows audio device and Windows' own
// decoders. Everything else is the command line in Rex.Media.AppCore, which the tests run
// in-process.
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
    ExtraDecoders = [new MfAudioDecoderFactory()],
    Cancellation = cancellation.Token,
};

return CliApplication.Run(args, host);
