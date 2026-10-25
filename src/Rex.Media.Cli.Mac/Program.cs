using System.Reflection;
using Rex.Media.AppCore.Cli;
using Rex.Media.AppCore.Platform;
using Rex.Media.Audio.CoreAudio;

// The composition root of rexplay on a Mac: the console, Ctrl+C, the Mac's audio output and
// VideoToolbox for H.264 and HEVC. Everything else is the command line in Rex.Media.AppCore, the
// very code Windows' rexplay runs, which is what keeps the two in step (ADR-0020). A window for
// pictures joins here when it arrives.
Console.OutputEncoding = System.Text.Encoding.UTF8;
using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cancellation.Cancel();
};

var roots = DataRoots.ForCurrentProcess();
var version = typeof(CliApplication).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "0.0.0";
var host = new CliHost
{
    Out = Console.Out,
    Error = Console.Error,
    Version = version,
    DefaultAudioSink = () => OperatingSystem.IsMacOS() ? new CoreAudioSink() : null,
    ExtraDecoders = [new Rex.Media.Codecs.Apple.AppleDecoderFactory()],
    DataRoot = roots.Root,
    Cancellation = cancellation.Token,
    SectionCache = new Rex.Media.AppCore.Player.SectionAnalysisCache(roots.SectionCache),
};

return CliApplication.Run(args, host);
