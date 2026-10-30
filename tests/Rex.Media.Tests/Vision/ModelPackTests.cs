using Rex.Media.Vision;

namespace Rex.Media.Tests.Vision;

public sealed class ModelPackTests
{
    private static List<InstallStatus> All(IReadOnlyList<VisionModel> models, InstallPhase phase) =>
        [.. models.Select(_ => new InstallStatus(phase, 0, 0, ""))];

    [Fact]
    public void SubjectLockKeepsItsWords()
    {
        var pack = ModelPack.SubjectLock;
        Assert.Equal(ModelDownloadText.Heading, pack.Heading);
        Assert.Equal(ModelDownloadText.Note, pack.Note);
        Assert.Same(ModelCatalogue.SubjectLock[0], pack.Models[0]);
        Assert.Equal(ModelDownloadText.Total(ModelCatalogue.SubjectLock), ModelDownloadText.Total(pack));
        Assert.Equal("Installed. Subject Lock uses these models when you select and remove.",
            ModelDownloadText.Describe(pack, All(pack.Models, InstallPhase.Installed)).Status);
    }

    [Fact]
    public void TheCameraPackSaysWhatItAddsAndWhereItComesFrom()
    {
        var pack = ModelPack.Camera;
        Assert.Equal("Camera effects pack (optional download)", pack.Heading);
        Assert.Contains("nothing the camera sees is sent anywhere or kept", pack.Note, StringComparison.Ordinal);
        Assert.Equal(ModelCatalogue.CameraPack, pack.Models);
        Assert.StartsWith("Downloads 31.7 MB from OpenCV's model zoo", ModelDownloadText.Total(pack), StringComparison.Ordinal);
        Assert.Equal(pack.Installed, ModelDownloadText.Describe(pack, All(pack.Models, InstallPhase.Installed)).Status);
        var update = ModelDownloadText.Describe(pack, All(pack.Models, InstallPhase.UpdateAvailable));
        Assert.Equal(pack.Update, update.Status);
        Assert.Equal("Update", update.DownloadLabel);
        var lines = ModelDownloadText.Contents(pack.Models);
        Assert.Contains(lines, line => line.Contains("finds where you are in the camera's picture", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains("finds your hands", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains("follows each finger", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains("finds your body", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains("follows your joints", StringComparison.Ordinal));
        Assert.All([pack.Note, pack.Installed, pack.Update, .. lines], text => Assert.DoesNotContain((char)0x2014, text));
    }

    [Fact]
    public void ASetMadeForAPackDescribesItselfInItsWords()
    {
        var store = new ModelStore(Path.Combine(Path.GetTempPath(), "rexplayer-pack-" + Guid.NewGuid().ToString("N")));
        var camera = new ModelSet(store, ModelPack.Camera);
        Assert.Same(ModelPack.Camera, camera.Pack);
        Assert.Equal(ModelCatalogue.CameraPack, camera.Models);
        Assert.Equal("Not installed.", camera.View.Status);

        var subject = new ModelSet(store, ModelCatalogue.SubjectLock);
        Assert.Same(ModelPack.SubjectLock, subject.Pack);
        Assert.Throws<ArgumentNullException>(() => new ModelSet(store, (ModelPack)null!));
        Assert.Throws<ArgumentNullException>(() => ModelDownloadText.Total((ModelPack)null!));
        Assert.Throws<ArgumentNullException>(() => ModelDownloadText.Describe((ModelPack)null!, []));
        Assert.Throws<ArgumentNullException>(() => ModelDownloadText.Describe(ModelPack.Camera with { Models = null! }, []));
    }
}
