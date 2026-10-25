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
    public void SwitchingVisualizerModesRestoresTheirOwnChoicesAndDefaultsWithoutCrossModeLeakage()
    {
        IReadOnlyDictionary<string, string> saved = new Dictionary<string, string>();
        saved = VisualizerOptions.With(saved, VisualizerChoice.Halo, "rays", 72);
        saved = VisualizerOptions.With(saved, VisualizerChoice.Halo, "thickness", 9);
        saved = VisualizerOptions.With(saved, VisualizerChoice.Halo, VisualizerOptions.Sensitivity, 3.5);
        saved = VisualizerOptions.With(saved, VisualizerChoice.Mirror, "thickness", 3);
        saved = VisualizerOptions.With(saved, VisualizerChoice.Spectrum, "bars", 96);
        saved = VisualizerOptions.With(saved, VisualizerChoice.Spectrum, VisualizerOptions.Sensitivity, 0.5);

        // Each switch selects a different namespace. Identically named compatible
        // setting kinds preserve their own saved values, but do not silently leak
        // values with incompatible bounds to the newly selected renderer.
        foreach (var selected in new[] { VisualizerChoice.Halo, VisualizerChoice.Mirror,
            VisualizerChoice.Spectrum, VisualizerChoice.Halo, VisualizerChoice.Mirror })
        {
            var settings = (new PlayerSettings { Visualizer = selected, VisualOptions = saved }).Normalize();
            Assert.Equal(selected, settings.Visualizer);
            Assert.Equal(72, VisualizerOptions.Number(settings.VisualOptions, VisualizerChoice.Halo, "rays"));
            Assert.Equal(9, VisualizerOptions.Number(settings.VisualOptions, VisualizerChoice.Halo, "thickness"));
            Assert.Equal(3, VisualizerOptions.Number(settings.VisualOptions, VisualizerChoice.Mirror, "thickness"));
            Assert.Equal(96, VisualizerOptions.Number(settings.VisualOptions, VisualizerChoice.Spectrum, "bars"));
            Assert.Equal(0.5, VisualizerOptions.Number(settings.VisualOptions, VisualizerChoice.Spectrum, VisualizerOptions.Sensitivity));
            Assert.Equal(3.5, VisualizerOptions.Number(settings.VisualOptions, VisualizerChoice.Halo, VisualizerOptions.Sensitivity));
            Assert.Equal(1, VisualizerOptions.Number(settings.VisualOptions, VisualizerChoice.Mirror, VisualizerOptions.Sensitivity));
        }

        var withoutHalo = VisualizerOptions.Reset(saved, VisualizerChoice.Halo);
        Assert.Equal(48, VisualizerOptions.Number(withoutHalo, VisualizerChoice.Halo, "rays"));
        Assert.Equal(4, VisualizerOptions.Number(withoutHalo, VisualizerChoice.Halo, "thickness"));
        Assert.Equal(1, VisualizerOptions.Number(withoutHalo, VisualizerChoice.Halo, VisualizerOptions.Sensitivity));
        Assert.Equal(3, VisualizerOptions.Number(withoutHalo, VisualizerChoice.Mirror, "thickness"));
        Assert.Equal(96, VisualizerOptions.Number(withoutHalo, VisualizerChoice.Spectrum, "bars"));
        Assert.Equal(0.5, VisualizerOptions.Number(withoutHalo, VisualizerChoice.Spectrum, VisualizerOptions.Sensitivity));
    }

    [Fact]
    [Capability("AU-18")]
    public void EachModeOptionHasAValidDefaultAndAStableDistinctPersistenceKey()
    {
        var empty = new Dictionary<string, string>();
        var persistedKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var mode in Enum.GetValues<VisualizerChoice>())
        {
            var options = VisualizerOptions.For(mode);
            foreach (var option in options)
            {
                var key = $"{mode.ToString().ToLowerInvariant()}.{option.Key}";
                Assert.True(persistedKeys.Add(key), $"Duplicate visualizer setting: {key}");
                switch (option.Kind)
                {
                    case VisualOptionKind.Number:
                        Assert.InRange(option.Default, option.Minimum, option.Maximum);
                        Assert.Equal(option.Default, VisualizerOptions.Number(empty, mode, option.Key));
                        break;
                    case VisualOptionKind.Toggle:
                        Assert.True(option.Default is 0 or 1);
                        Assert.Equal(option.Default == 1, VisualizerOptions.Toggle(empty, mode, option.Key));
                        break;
                    case VisualOptionKind.Choice:
                        Assert.NotNull(option.Choices);
                        Assert.InRange(option.Default, 0, option.Choices.Count - 1);
                        Assert.Equal((int)option.Default, VisualizerOptions.Choice(empty, mode, option.Key));
                        break;
                    case VisualOptionKind.Color:
                        Assert.InRange(option.Default, 0, 0xFFFFFF);
                        Assert.Equal((int)option.Default, VisualizerOptions.Rgb(empty, mode, option.Key));
                        break;
                }

                var saved = VisualizerOptions.With(empty, mode, option.Key, option.Default);
                Assert.Contains(key, saved.Keys);
                Assert.DoesNotContain(key, empty.Keys);
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
