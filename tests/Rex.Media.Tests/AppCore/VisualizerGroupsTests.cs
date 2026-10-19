using Rex.Media.AppCore.Player;
using Rex.Media.Settings;
using Rex.Media.TestKit;

namespace Rex.Media.Tests.AppCore;

/// <summary>The visualisations as Preferences groups them (AU-18).</summary>
public sealed class VisualizerGroupsTests
{
    [Fact]
    [Capability("AU-18")]
    public void EveryVisualisationIsListedOnceUnderAGroup()
    {
        var listed = VisualizerGroups.All.SelectMany(group => group.Choices).Select(entry => entry.Choice).Prepend(VisualizerChoice.Off).ToList();

        Assert.Equal(Enum.GetValues<VisualizerChoice>().Order(), listed.Order());
        Assert.All(VisualizerGroups.All, group => Assert.NotEmpty(group.Choices));
        Assert.All(VisualizerGroups.All.SelectMany(group => group.Choices), entry => Assert.False(string.IsNullOrWhiteSpace(entry.Label)));
        Assert.Equal("Nothing", VisualizerGroups.Nothing);
    }

    [Fact]
    [Capability("AU-18")]
    public void TheCameraVisualisationsHaveTheirOwnGroup()
    {
        var camera = VisualizerGroups.All.Single(group => group.Heading == "With the camera").Choices.Select(entry => entry.Choice).ToHashSet();

        Assert.Subset(camera, new HashSet<VisualizerChoice> { VisualizerChoice.Silhouette, VisualizerChoice.Clones, VisualizerChoice.Water, VisualizerChoice.InfinityMirror });
        Assert.All(VisualizerGroups.All.Where(group => group.Heading != "With the camera").SelectMany(group => group.Choices),
            entry => Assert.DoesNotContain(entry.Choice, camera));
    }

    [Fact]
    [Capability("AU-18")]
    public void SavedChoicesKeepTheirValues()
    {
        // Grouping changes only the order shown; the saved enum keeps every value where it was.
        Assert.Equal(0, (int)VisualizerChoice.Off);
        Assert.Equal(12, (int)VisualizerChoice.Silhouette);
        Assert.Equal(41, (int)VisualizerChoice.Water);
    }
}
