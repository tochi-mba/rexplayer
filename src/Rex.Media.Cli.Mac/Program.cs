using System.Reflection;
using Rex.Media.AppCore;
using Rex.Media.AppCore.Cli;
using Rex.Media.AppCore.Platform;
using Rex.Media.Audio.CoreAudio;
using Rex.Media.Primitives;

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
    SystemReport = MacSystemReport,
};

return CliApplication.Run(args, host);

static SystemReport MacSystemReport()
{
    if (!OperatingSystem.IsMacOS())
    {
        return new SystemReport
        {
            Platform = "macOS",
            Windows = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
            Graphics = null,
        };
    }

    AudioFormat? output = null;
    try
    {
        using var sink = new CoreAudioSink();
        output = sink.Open(new AudioFormat(48_000, 2, SampleFormat.F32, ChannelLayout.Stereo));
    }
    catch (IOException)
    {
        // A computer with no selected output is still a valid report.
    }

    return new SystemReport
    {
        Platform = "macOS",
        Windows = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
        Graphics = "Metal",
        AudioOutput = output,
        Decoders =
        [
            new WindowsDecoders(CodecId.Aac, ["AudioToolbox"], []),
            new WindowsDecoders(CodecId.H264, ["VideoToolbox"], ["VideoToolbox"]),
            new WindowsDecoders(CodecId.Hevc, ["VideoToolbox"], ["VideoToolbox"]),
        ],
    };
}
