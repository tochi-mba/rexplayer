using System.IO.Compression;
using Rex.Media.AppCore;
using Rex.Media.TestKit;

namespace Rex.Media.Tests.AppCore;

public sealed class DiagnosticsBundleTests
{
    private const string Home = @"D:\Profiles\Asake";

    [Fact]
    [Capability("TOOL-10")]
    public void TheBundleHoldsEachFileWithTheHomeFolderMasked()
    {
        using var zip = new MemoryStream();

        DiagnosticsBundle.Write(
            zip,
            [("rexplayer.log", @"Opened D:\Profiles\Asake\Music\Sungba.mp3 and d:/profiles/asake/x.flac"), ("settings.json", "{ }"), ("missing.txt", null)],
            Home);

        using var archive = new ZipArchive(new MemoryStream(zip.ToArray()), ZipArchiveMode.Read);
        Assert.Equal(["rexplayer.log", "settings.json"], archive.Entries.Select(entry => entry.FullName));
        using var log = new StreamReader(archive.GetEntry("rexplayer.log")!.Open());
        Assert.Equal(@"Opened %USERPROFILE%\Music\Sungba.mp3 and %USERPROFILE%/x.flac", log.ReadToEnd());
    }

    [Fact]
    public void WithoutAHomeFolderTheTextIsLeftAsItIs()
    {
        Assert.Equal("text", DiagnosticsBundle.Mask("text", null));
        Assert.Equal("text", DiagnosticsBundle.Mask("text", ""));
        Assert.Throws<ArgumentNullException>(() => DiagnosticsBundle.Mask(null!, Home));
        Assert.Throws<ArgumentNullException>(() => DiagnosticsBundle.Write(null!, [], Home));
        Assert.Throws<ArgumentNullException>(() => DiagnosticsBundle.Write(new MemoryStream(), null!, Home));
    }

    [Fact]
    public void TheBundleIsNamedForWhenItWasMade()
    {
        Assert.Equal("rexplayer-diagnostics-20261007-0930.zip", DiagnosticsBundle.FileName(new DateTimeOffset(2026, 10, 7, 9, 30, 15, TimeSpan.Zero)));
    }
}
