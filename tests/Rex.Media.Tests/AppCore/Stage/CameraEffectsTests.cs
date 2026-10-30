using Rex.Media.AppCore.Player;
using Rex.Media.AppCore.Visuals;
using Rex.Media.AppCore.Visuals.Stage;
using Rex.Media.Settings;

namespace Rex.Media.Tests.AppCore.Stage;

public sealed class CameraEffectsTests
{
    public static TheoryData<VisualizerChoice, Type, VisualizerChoice> OnStage() => new()
    {
        { VisualizerChoice.InkBody, typeof(InkBodyEffect), VisualizerChoice.Ink },
        { VisualizerChoice.NeonSkeleton, typeof(NeonSkeletonEffect), VisualizerChoice.Silhouette },
        { VisualizerChoice.EchoClones, typeof(EchoClonesEffect), VisualizerChoice.Clones },
    };

    [Theory]
    [MemberData(nameof(OnStage))]
    public void EachGraphicsCardEffectIsMadeAndHasASoftwareStandIn(VisualizerChoice choice, Type effect, VisualizerChoice standIn)
    {
        Assert.True(CameraEffects.OnStage(choice));
        Assert.IsType(effect, CameraEffects.Make(choice));
        Assert.Equal(standIn, CameraEffects.StandIn(choice));
        // The software stage draws the stand-in, so a Mac or a failed graphics card still shows something.
        Assert.True(VisualScene.Draws(choice));
        Assert.Equal(VisualScene.For(standIn)!.GetType(), VisualScene.For(choice)!.GetType());
        // The graphics-card effects have no beat meter; that is the software stage's.
        Assert.DoesNotContain(VisualizerOptions.For(choice), option => option.Key == VisualizerOptions.Meter);
        Assert.Equal(VisualPalette.Neon, VisualizerOptions.DefaultPalette(choice));
        Assert.Contains(VisualizerGroups.All.Single(group => group.Heading == "With the camera").Choices, entry => entry.Choice == choice);
    }

    [Fact]
    public void EveryOtherVisualisationIsItsOwnAndNotOnTheStage()
    {
        foreach (var choice in Enum.GetValues<VisualizerChoice>().Where(choice => !CameraEffects.OnStage(choice)))
        {
            Assert.Null(CameraEffects.Make(choice));
            Assert.Equal(choice, CameraEffects.StandIn(choice));
        }
    }

    [Fact]
    public void TheStandInIsDrawnWithItsOwnSettings()
    {
        var stage = new VisualStage(32, 18, new Random(1));
        var options = new Dictionary<string, string> { ["ink.drops"] = "2" };
        var sound = new float[VisualStage.SoundSize];

        Assert.True(stage.Draw(VisualizerChoice.InkBody, options, sound, sound, 48_000, TimeSpan.FromSeconds(1 / 30.0), TestContext.Current.CancellationToken));
        Assert.Equal(VisualizerChoice.Ink, stage.Context.Choice);
        Assert.Equal(2, stage.Context.Number("drops"));
    }
}
