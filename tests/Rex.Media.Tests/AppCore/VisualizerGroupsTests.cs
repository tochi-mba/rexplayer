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
    public void EveryVisualizerOptionSurvivesIndependentSelectionAndReset()
    {
        IReadOnlyDictionary<string, string> saved = new Dictionary<string, string>();
        foreach (var choice in Enum.GetValues<VisualizerChoice>())
        {
            foreach (var option in VisualizerOptions.For(choice))
            {
                var chosen = option.Kind switch
                {
                    VisualOptionKind.Toggle => option.Default == 0 ? 1 : 0,
                    VisualOptionKind.Choice => option.Choices!.Count - 1,
                    VisualOptionKind.Color => 0x123456,
                    _ => option.Maximum,
                };
                saved = VisualizerOptions.With(saved, choice, option.Key, chosen);
                var restored = option.Kind switch
                {
                    VisualOptionKind.Toggle => VisualizerOptions.Toggle(saved, choice, option.Key) ? 1d : 0d,
                    VisualOptionKind.Choice => VisualizerOptions.Choice(saved, choice, option.Key),
                    VisualOptionKind.Color => VisualizerOptions.Rgb(saved, choice, option.Key),
                    _ => VisualizerOptions.Number(saved, choice, option.Key),
                };
                Assert.Equal(chosen, restored);
            }
        }

        // Resetting one renderer cannot erase other modes' persisted choices.
        var withoutSpectrum = VisualizerOptions.Reset(saved, VisualizerChoice.Spectrum);
        foreach (var choice in Enum.GetValues<VisualizerChoice>().Where(choice => choice != VisualizerChoice.Spectrum))
        {
            foreach (var option in VisualizerOptions.For(choice))
            {
                var before = VisualizerOptions.Number(saved, choice, option.Key);
                var after = VisualizerOptions.Number(withoutSpectrum, choice, option.Key);
                Assert.Equal(before, after);
            }
        }
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
