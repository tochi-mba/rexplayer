using System.Security.Cryptography;
using Rex.Media.AppCore.Player;
using Rex.Media.AppCore.Updates;
using Rex.Media.Engine;
using Rex.Media.IO;
using Rex.Media.Settings;
using Rex.Media.TestKit;
using static Rex.Media.Tests.AppCore.ControllerHarness;

namespace Rex.Media.Tests.AppCore;

public sealed class UpdateAndResumeTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "rexplayer-resume-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    private static string Release(string tag, bool prerelease = false, string? installer = null) => $$"""
        {
          "tag_name": "{{tag}}",
          "prerelease": {{(prerelease ? "true" : "false")}},
          "html_url": "https://github.com/tochi-mba/rexplayer/releases/tag/{{tag}}",
          "assets": [
            { "name": "{{installer ?? $"rexplayer-Setup-{tag.TrimStart('v')}.exe"}}", "browser_download_url": "https://example.com/setup.exe" },
            { "name": "rexplayer-Setup-{{tag.TrimStart('v')}}.exe.sha256", "browser_download_url": "https://example.com/setup.exe.sha256" },
            { "name": "notes.txt", "browser_download_url": "not a link" }
          ]
        }
        """;

    [Theory]
    [InlineData(UpdateCadence.Off, null, false)]
    [InlineData(UpdateCadence.Daily, null, true)]
    [InlineData(UpdateCadence.Daily, 23.0, false)]
    [InlineData(UpdateCadence.Daily, 24.0, true)]
    [InlineData(UpdateCadence.Weekly, 24.0 * 6, false)]
    [InlineData(UpdateCadence.Weekly, 24.0 * 7, true)]
    [InlineData(UpdateCadence.Weekly, null, true)]
    public void AChecksIsDueOnlyWhenTheCadenceSays(UpdateCadence cadence, double? hoursAgo, bool due)
    {
        Assert.Equal(due, UpdateCheck.IsDue(cadence, hoursAgo is { } h ? Now.AddHours(-h) : null, Now));
    }

    [Fact]
    [Capability("TOOL-09")]
    public void ANewerReleaseWithItsInstallerAndChecksumIsOffered()
    {
        var offer = UpdateCheck.Offer(Release("v0.4.0"), "0.3.1");

        Assert.Equal(new Version(0, 4, 0), offer!.Version);
        Assert.Equal("rexplayer-Setup-0.4.0.exe", offer.InstallerName);
        Assert.Equal("https://example.com/setup.exe", offer.Installer.ToString());
        Assert.Equal("https://example.com/setup.exe.sha256", offer.Checksum.ToString());
        Assert.Equal("https://github.com/tochi-mba/rexplayer/releases/tag/v0.4.0", offer.ReleasePage.ToString());
        Assert.Equal("https://api.github.com/repos/tochi-mba/rexplayer/releases/latest", UpdateCheck.LatestRelease.ToString());
    }

    [Theory]
    [InlineData("v0.3.1", "0.3.1")]
    [InlineData("v0.3.0", "0.3.1")]
    [InlineData("v0.4.0", "not a version")]
    [InlineData("v0.4", "0.3.1")]
    public void NothingIsOfferedThatIsNotNewer(string tag, string current)
    {
        Assert.Null(UpdateCheck.Offer(Release(tag), current));
    }

    [Fact]
    public void ReleasesThatCannotBeTrustedAreNotOffered()
    {
        Assert.Null(UpdateCheck.Offer(Release("v0.4.0", prerelease: true), "0.3.1"));
        Assert.Null(UpdateCheck.Offer(Release("v0.4.0", installer: "something-else.exe"), "0.3.1"));
        Assert.Null(UpdateCheck.Offer("{ not json", "0.3.1"));
        Assert.Null(UpdateCheck.Offer("[]", "0.3.1"));
        Assert.Null(UpdateCheck.Offer("""{ "tag_name": 4 }""", "0.3.1"));
        Assert.Null(UpdateCheck.Offer("""{ "tag_name": "v0.4.0" }""", "0.3.1"));
        Assert.Null(UpdateCheck.Offer("""{ "tag_name": "v0.4.0", "assets": [ { "name": 5, "browser_download_url": "https://x" } ] }""", "0.3.1"));
        Assert.Equal(
            "https://github.com/tochi-mba/rexplayer/releases/latest",
            UpdateCheck.Offer(Release("v0.4.0").Replace("\"html_url\": \"https://github.com/tochi-mba/rexplayer/releases/tag/v0.4.0\",", "", StringComparison.Ordinal), "0.3.1")!.ReleasePage.ToString());
        Assert.Throws<ArgumentNullException>(() => UpdateCheck.Offer(null!, "0.3.1"));
    }

    [Fact]
    public void OnlyAnInstallerMatchingItsPublishedChecksumIsRun()
    {
        var bytes = new byte[] { 1, 2, 3, 4 };
        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

        Assert.True(UpdateCheck.Verify(new MemoryStream(bytes), $"{hash}  rexplayer-Setup-0.4.0.exe\n", "rexplayer-Setup-0.4.0.exe"));
        Assert.True(UpdateCheck.Verify(new MemoryStream(bytes), $"{hash} *rexplayer-Setup-0.4.0.exe", "rexplayer-Setup-0.4.0.exe"));
        Assert.True(UpdateCheck.Verify(new MemoryStream(bytes), hash.ToUpperInvariant(), "rexplayer-Setup-0.4.0.exe"));
        Assert.False(UpdateCheck.Verify(new MemoryStream([9]), hash, "rexplayer-Setup-0.4.0.exe"));
        Assert.False(UpdateCheck.Verify(new MemoryStream(bytes), $"{hash}  other.exe", "rexplayer-Setup-0.4.0.exe"));
        Assert.False(UpdateCheck.Verify(new MemoryStream(bytes), "short", "rexplayer-Setup-0.4.0.exe"));
        Assert.Throws<ArgumentNullException>(() => UpdateCheck.Verify(null!, hash, "x"));
        Assert.Throws<ArgumentNullException>(() => UpdateCheck.Verify(new MemoryStream(), null!, "x"));
    }

    [Fact]
    [Capability("WIN-14")]
    public void TheResumeMarkerHoldsWhatWasPlayingUntilItIsCleared()
    {
        var path = Path.Combine(_folder, "resume.json");
        Assert.Null(ResumeMarker.Read(path));

        ResumeMarker.Write(path, new ResumePoint(@"C:\Music\Sungba.mp3", "Sungba", 83.5));
        ResumeMarker.Write(path, new ResumePoint(@"C:\Music\Sungba.mp3", "Sungba", 90));
        Assert.Equal(new ResumePoint(@"C:\Music\Sungba.mp3", "Sungba", 90), ResumeMarker.Read(path));

        ResumeMarker.Clear(path);
        Assert.Null(ResumeMarker.Read(path));
        Assert.False(File.Exists(path + AtomicFile.BackupSuffix));

        File.WriteAllText(path, "{ broken");
        Assert.Null(ResumeMarker.Read(path));
        File.WriteAllText(path, """{ "location": "", "title": "x", "positionSeconds": 1 }""");
        Assert.Null(ResumeMarker.Read(path));
        Assert.Throws<ArgumentNullException>(() => ResumeMarker.Write(path, null!));
    }

    [Fact]
    public void ResumingPlaysTheItemFromWhereItWas()
    {
        using var harness = new ControllerHarness(autoPlay: false);
        harness.Files["a.wav"] = Count(0, 8000 * 10);
        harness.Files["b.wav"] = Count(0, 400);
        harness.Controller.Open(["b.wav"]);

        harness.Controller.Resume("a.wav", TimeSpan.FromSeconds(4));
        harness.PumpUntil(c => c.State == SessionState.Ready && c.Item?.Title == "a");

        Assert.Equal(TimeSpan.FromSeconds(4), harness.Controller.Position);
        Assert.Equal(["a"], harness.Controller.Playlist.Items.Select(item => item.Title));
    }
}
