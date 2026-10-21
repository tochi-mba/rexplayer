using Rex.Media.Vision;

namespace Rex.Media.Tests.Vision;

public sealed class ModelDownloadTextTests
{
    private static readonly VisionModel Big = TestModels.Model("big", new ModelFile("a", new Uri("https://models.example/a"), 30_000_000, new string('0', 64)));
    private static readonly VisionModel Small = TestModels.Model("small", new ModelFile("b", new Uri("https://models.example/b"), 10_000_000, new string('1', 64)));
    private static readonly VisionModel[] Both = [Big, Small];

    [Theory]
    [InlineData(69_444_670, "69.4 MB")]
    [InlineData(1_000_000, "1.0 MB")]
    [InlineData(999_499, "999 KB")]
    [InlineData(-5, "0 KB")]
    public void SizesAreDecimal(long bytes, string expected)
    {
        Assert.Equal(expected, ModelDownloadText.Size(bytes));
    }

    [Fact]
    public void TheContentsNameEachModelItsSizeLicenceAndJob()
    {
        var lines = ModelDownloadText.Contents(ModelCatalogue.SubjectLock);

        Assert.Equal("EfficientSAM-Ti, 41.4 MB, Apache-2.0 licence: selects the object you click (Yunyang Xiong et al., EfficientSAM, CVPR 2024)", lines[0]);
        Assert.Equal("MI-GAN 512 (Places2), 28.1 MB, MIT licence: fills the space a removed object leaves (Andranik Sargsyan et al., MI-GAN, ICCV 2023)", lines[1]);
        Assert.Equal("Downloads 69.4 MB from the model authors' own pages when you choose Download.", ModelDownloadText.Total(ModelCatalogue.SubjectLock));
        Assert.DoesNotContain((char)0x2014, ModelDownloadText.Note + string.Concat(lines));
        Assert.DoesNotContain('·', ModelDownloadText.Note + string.Concat(lines));
    }

    [Fact]
    public void NothingInstalledOffersTheDownload()
    {
        var view = ModelDownloadText.Describe(Both, [new(InstallPhase.NotInstalled), new(InstallPhase.NotInstalled)]);

        Assert.Equal(new ModelDownloadView("Not installed.", "Download", true, false, false, false, 0, false), view);
    }

    [Fact]
    public void ADownloadShowsProgressAcrossTheModels()
    {
        var view = ModelDownloadText.Describe(Both, [new(InstallPhase.Installed), new(InstallPhase.Downloading, 5_000_000, 10_000_000)]);

        Assert.Equal(new ModelDownloadView("Downloading 35.0 MB of 40.0 MB...", "Download", false, true, false, true, 87.5, false), view);
        Assert.Equal(0, ModelDownloadText.Describe(Both, [new(InstallPhase.Downloading, 0, 30_000_000), new(InstallPhase.NotInstalled)]).ProgressPercent);
    }

    [Fact]
    public void CheckingIsShownWhileHashesAreCompared()
    {
        var view = ModelDownloadText.Describe(Both, [new(InstallPhase.Verifying, 30_000_000, 30_000_000), new(InstallPhase.NotInstalled)]);

        Assert.Equal("Checking the downloaded files...", view.Status);
        Assert.Equal(75, view.ProgressPercent);
    }

    [Fact]
    public void AFailureSaysWhyAndOffersAnotherTry()
    {
        var view = ModelDownloadText.Describe(Both, [new(InstallPhase.Installed), new(InstallPhase.Failed, 0, 0, "The connection failed.")]);

        Assert.Equal(new ModelDownloadView("The download did not finish. The connection failed.", "Try again", true, false, true, false, 0, true), view);
    }

    [Fact]
    public void InstalledModelsCanBeRemoved()
    {
        var view = ModelDownloadText.Describe(Both, [new(InstallPhase.Installed), new(InstallPhase.Installed)]);

        Assert.Equal((false, true, 100.0), (view.CanDownload, view.CanRemove, view.ProgressPercent));
        Assert.StartsWith("Installed.", view.Status, StringComparison.Ordinal);
    }

    [Fact]
    public void AnUpdateIsOfferedAndTheOldFilesCanBeRemoved()
    {
        var view = ModelDownloadText.Describe(Both, [new(InstallPhase.UpdateAvailable), new(InstallPhase.Installed)]);

        Assert.Equal(("Update", true, true), (view.DownloadLabel, view.CanDownload, view.CanRemove));
    }

    [Fact]
    public void APartInstallFetchesTheRest()
    {
        var view = ModelDownloadText.Describe(Both, [new(InstallPhase.Installed), new(InstallPhase.NotInstalled)]);

        Assert.Equal(("Partly installed. Download fetches the rest.", true, true), (view.Status, view.CanDownload, view.CanRemove));
    }

    [Fact]
    public void EveryModelNeedsOneStatus()
    {
        Assert.Throws<ArgumentNullException>(() => ModelDownloadText.Describe(null!, []));
        Assert.Throws<ArgumentNullException>(() => ModelDownloadText.Describe(Both, null!));
        Assert.Throws<ArgumentException>(() => ModelDownloadText.Describe([], []));
        Assert.Throws<ArgumentException>(() => ModelDownloadText.Describe(Both, [new(InstallPhase.Installed)]));
    }
}
