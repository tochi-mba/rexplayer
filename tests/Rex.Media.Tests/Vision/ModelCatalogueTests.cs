using System.Text.RegularExpressions;
using Rex.Media.Vision;

namespace Rex.Media.Tests.Vision;

public sealed partial class ModelCatalogueTests
{
    [Fact]
    public void EveryModelIsPermissiveAndPinnedToARevisionAndAHash()
    {
        Assert.NotEmpty(ModelCatalogue.All);
        Assert.Equal(ModelCatalogue.All.Count, ModelCatalogue.All.Select(model => model.Id).Distinct(StringComparer.Ordinal).Count());
        foreach (var model in ModelCatalogue.All)
        {
            Assert.True(ModelCatalogue.IsPermissive(model.Licence), model.Id);
            Assert.Equal("https", model.LicenceUrl.Scheme);
            Assert.NotEmpty(model.Input);
            Assert.NotEmpty(model.Authors);
            Assert.NotEmpty(model.Files);
            foreach (var file in model.Files)
            {
                Assert.Matches(Sha256(), file.Sha256);
                Assert.Equal("https", file.Url.Scheme);
                // A branch name could move under a pinned hash; a commit cannot.
                Assert.Matches(Revision(), file.Url.AbsoluteUri);
                Assert.EndsWith("/" + file.Name, file.Url.AbsolutePath, StringComparison.Ordinal);
                Assert.True(file.Bytes > 0);
            }

            Assert.Equal(model.Files.Sum(file => file.Bytes), model.Bytes);
        }
    }

    [Fact]
    public void TheSubjectLockDownloadHasOneModelPerJobAndIsSmall()
    {
        var models = ModelCatalogue.SubjectLock;

        Assert.Single(models, model => model.Purpose == ModelPurpose.PromptSegmentation);
        Assert.Single(models, model => model.Purpose == ModelPurpose.Inpainting);
        Assert.True(models.Sum(model => model.Bytes) < 100_000_000);
    }

    [Theory]
    [InlineData("Apache-2.0", true)]
    [InlineData("MIT", true)]
    [InlineData("BSD-3-Clause", true)]
    [InlineData("BSD-2-Clause", true)]
    [InlineData("AGPL-3.0", false)]
    [InlineData("GPL-3.0", false)]
    [InlineData("CC-BY-NC-4.0", false)]
    [InlineData("mit", false)]
    public void OnlyPermissiveLicencesAreAllowed(string licence, bool allowed)
    {
        Assert.Equal(allowed, ModelCatalogue.IsPermissive(licence));
    }

    [Fact]
    public void ModelsAreFoundByIdentifier()
    {
        Assert.Same(ModelCatalogue.EfficientSamTiny, ModelCatalogue.Find("efficientsam-ti"));
        Assert.Same(ModelCatalogue.MiGan, ModelCatalogue.Find("migan-512-places2"));
        Assert.Null(ModelCatalogue.Find("nothing"));
    }

    [GeneratedRegex("^[0-9a-f]{64}$")]
    private static partial Regex Sha256();

    [GeneratedRegex("/[0-9a-f]{40}/")]
    private static partial Regex Revision();
}
