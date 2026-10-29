using Rex.Media.Vision;

namespace Rex.Media.Tests.Vision;

public sealed partial class ModelCatalogueTests
{
    [Fact]
    public void TheCameraPackHasTheMatteTheHandsAndTheBodyAndIsSmall()
    {
        var models = ModelCatalogue.CameraPack;

        Assert.Single(models, model => model.Purpose == ModelPurpose.Matte);
        Assert.Single(models, model => model.Purpose == ModelPurpose.PalmDetection);
        Assert.Single(models, model => model.Purpose == ModelPurpose.HandLandmarks);
        Assert.Single(models, model => model.Purpose == ModelPurpose.PersonDetection);
        Assert.Single(models, model => model.Purpose == ModelPurpose.Pose);
        // About 32 MB: a download on a phone's tether, not an evening.
        Assert.InRange(models.Sum(model => model.Bytes), 30_000_000, 34_000_000);
        Assert.All(models, model => Assert.Equal("Apache-2.0", model.Licence));
        Assert.All(models, model => Assert.Contains("OpenCV model zoo", model.Authors, StringComparison.Ordinal));
        Assert.DoesNotContain(models, model => ModelCatalogue.SubjectLock.Contains(model));
        Assert.Equal(ModelCatalogue.All.Count, ModelCatalogue.SubjectLock.Count + models.Count);
    }

    [Fact]
    public void TheCameraModelsAreFoundByIdentifier()
    {
        Assert.Same(ModelCatalogue.HumanMatte, ModelCatalogue.Find("pphumanseg-2023mar"));
        Assert.Same(ModelCatalogue.PalmDetector, ModelCatalogue.Find("mediapipe-palm-2023feb"));
        Assert.Same(ModelCatalogue.HandLandmarks, ModelCatalogue.Find("mediapipe-hand-2023feb"));
        Assert.Same(ModelCatalogue.PersonDetector, ModelCatalogue.Find("mediapipe-person-2023mar"));
        Assert.Same(ModelCatalogue.Pose, ModelCatalogue.Find("mediapipe-pose-2023mar"));
    }
}
