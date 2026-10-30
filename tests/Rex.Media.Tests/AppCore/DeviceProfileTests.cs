using Rex.Media.AppCore.Performance;
using Rex.Media.Primitives;
using Rex.Media.Settings;
using Rex.Media.Vision;

namespace Rex.Media.Tests.AppCore;

public sealed class DeviceProfileTests
{
    private const long Gigabyte = 1024L * 1024 * 1024;

    /// <summary>The computer the baseline was measured on: eight threads, 32 GB, built-in graphics that decode.</summary>
    private static readonly DeviceFacts Capable = new()
    {
        Processors = 8,
        MemoryBytes = 32 * Gigabyte,
        Graphics = GraphicsKind.Integrated,
        HardwareDecoding = [CodecId.H264, CodecId.Hevc, CodecId.Vp9],
    };

    public static TheoryData<DeviceFacts, PerformanceTier> Computers() => new()
    {
        { Capable, PerformanceTier.BestQuality },
        { Capable with { Graphics = GraphicsKind.Discrete, MemoryBytes = 0, HardwareDecoding = null }, PerformanceTier.BestQuality },
        { Capable with { Processors = 4 }, PerformanceTier.Balanced },
        { Capable with { MemoryBytes = 6 * Gigabyte }, PerformanceTier.Balanced },
        { Capable with { MemoryBytes = 7 * Gigabyte }, PerformanceTier.BestQuality },
        { Capable with { Graphics = GraphicsKind.Software }, PerformanceTier.Fastest },
        { Capable with { Processors = 2 }, PerformanceTier.Fastest },
        { Capable with { MemoryBytes = 3 * Gigabyte }, PerformanceTier.Fastest },
        { Capable with { Processors = 4, HardwareDecoding = [] }, PerformanceTier.Fastest },
        { Capable with { Processors = 6, HardwareDecoding = [CodecId.Hevc] }, PerformanceTier.BestQuality },
        { Capable with { Processors = 4, HardwareDecoding = null }, PerformanceTier.Balanced },
    };

    [Theory]
    [MemberData(nameof(Computers))]
    public void TheComputerDecidesTheTier(DeviceFacts facts, PerformanceTier expected)
    {
        Assert.Equal(expected, DeviceProfile.Classify(facts));
        Assert.Equal(expected, DeviceProfile.Choose(PerformanceChoice.Automatic, facts));
    }

    [Theory]
    [InlineData(PerformanceTier.BestQuality, PerformanceTier.Balanced)]
    [InlineData(PerformanceTier.Balanced, PerformanceTier.Fastest)]
    [InlineData(PerformanceTier.Fastest, PerformanceTier.Fastest)]
    public void EnergySaverTakesAutomaticOneStepLighterAndBatteryAloneNothing(PerformanceTier plugged, PerformanceTier saving)
    {
        var facts = plugged switch
        {
            PerformanceTier.BestQuality => Capable,
            PerformanceTier.Balanced => Capable with { Processors = 4 },
            _ => Capable with { Processors = 2 },
        };

        Assert.Equal(plugged, DeviceProfile.Choose(PerformanceChoice.Automatic, facts with { OnBattery = true }));
        Assert.Equal(saving, DeviceProfile.Choose(PerformanceChoice.Automatic, facts with { OnBattery = true, EnergySaver = true }));
    }

    [Theory]
    [InlineData(PerformanceChoice.BestQuality, PerformanceTier.BestQuality)]
    [InlineData(PerformanceChoice.Balanced, PerformanceTier.Balanced)]
    [InlineData(PerformanceChoice.Fastest, PerformanceTier.Fastest)]
    public void AChosenTierIsUsedWhateverTheComputer(PerformanceChoice choice, PerformanceTier tier)
    {
        Assert.Equal(tier, DeviceProfile.Choose(choice, Capable with { Processors = 2, EnergySaver = true }));
        Assert.Equal(tier, DeviceProfile.Choose(choice, Capable));
    }

    [Fact]
    public void TheBestQualityChangesNothing()
    {
        var budget = DeviceProfile.For(PerformanceTier.BestQuality, Capable);

        Assert.Equal(new DeviceBudget(), budget);
        Assert.Empty(DeviceProfile.Changes(budget));
        Assert.All(Enum.GetValues<VideoEffect>(), effect => Assert.True(budget.Allows(effect)));
    }

    [Fact]
    public void BalancedDrawsLessAndKeepsEveryEffect()
    {
        var budget = DeviceProfile.For(PerformanceTier.Balanced, Capable);

        Assert.Equal((1280, 33, 24, 64), (budget.VisualMaxSide, budget.VisualFrameMilliseconds, budget.VideoQueueCapacity, budget.AudioQueueCapacity));
        Assert.Equal((false, 1, true, false, true, true), (budget.HeavyEffectsOff, budget.SubjectStep, budget.TranscribeWhilePlaying, budget.LeanLaunch, budget.SeekPreview, budget.Animations));
        Assert.True(budget.Allows(VideoEffect.GhostwireMotion));
        // Balanced says nothing about transcription: the quality chosen on the caption rail is used as it is.
        Assert.Equal(
            [
                "Music visualisations are drawn at up to 1280 pixels across and 30 pictures a second, then scaled to the window.",
                "Background transcription and intro or credit analysis pause while browsing the library.",
                "Fewer pictures wait to be decoded, so playing uses less memory.",
                "The camera effects follow your body every third picture rather than every other.",
            ],
            DeviceProfile.Changes(budget));
        Assert.Equal(PerceptionLevel.Light, budget.CameraPerception);
    }

    [Fact]
    public void FastestDoesTheLeastThatStillPlaysWell()
    {
        var budget = DeviceProfile.For(PerformanceTier.Fastest, Capable);

        Assert.Equal((854, 40, 12, 32, 2, 240, 960), (budget.VisualMaxSide, budget.VisualFrameMilliseconds, budget.VideoQueueCapacity, budget.AudioQueueCapacity, budget.SubjectStep, budget.LibraryPictureSide, budget.HeroPictureSide));
        Assert.Equal((true, false, true, false, false), (budget.HeavyEffectsOff, budget.TranscribeWhilePlaying, budget.LeanLaunch, budget.SeekPreview, budget.Animations));
        Assert.False(budget.Allows(VideoEffect.NeonEdges));
        Assert.False(budget.Allows(VideoEffect.TopographicContours));
        Assert.False(budget.Allows(VideoEffect.ZoomBurst));
        Assert.True(budget.Allows(VideoEffect.Halftone));
        var changes = DeviceProfile.Changes(budget);
        Assert.Equal(12, changes.Count);
        Assert.Equal(PerceptionLevel.HandWritten, budget.CameraPerception);
        Assert.Contains("The camera effects use rexplayer's own outline and motion; no downloaded camera model runs.", changes);
        Assert.Equal(35, DeviceProfile.HeavyEffects.Count);
        // A count, not a wall of 35 names: choosing one of them says so.
        Assert.Contains("The 35 heaviest picture effects stay off. Choosing one says so.", changes);
        // The user's transcription quality is used as chosen; only when the library is transcribed changes.
        Assert.Contains("The library is transcribed only while nothing plays.", changes);
        Assert.Contains("Background transcription and intro or credit analysis pause while browsing the library.", changes);
        Assert.Contains("Subject Lock follows its subject in a half-size copy of each picture.", changes);
        Assert.Contains("The timeline shows no preview picture under the pointer.", changes);
        Assert.Contains("The window's animations are off.", changes);
        Assert.Contains("Starting with nothing to play prepares only the window; video and sound are made ready when first used.", changes);
        Assert.Contains("Library pictures are decoded up to 240 pixels across to use less memory.", changes);
        Assert.Contains("Page banners are decoded up to 960 pixels across to use less memory.", changes);
        Assert.All(changes, change => Assert.DoesNotContain((char)0x2014, change));
    }

    [Theory]
    [InlineData(PerformanceTier.BestQuality, false)]
    [InlineData(PerformanceTier.Balanced, true)]
    [InlineData(PerformanceTier.Fastest, true)]
    public void BrowsingThrottleFollowsTheChosenBudget(PerformanceTier tier, bool yields)
    {
        var budget = DeviceProfile.For(tier, Capable);
        Assert.Equal(yields, budget.PauseBackgroundWorkWhileBrowsing);
        Assert.Equal(yields, DeviceProfile.Changes(budget).Contains("Background transcription and intro or credit analysis pause while browsing the library."));
    }

    [Fact]
    public void PerformanceModesReduceResourceBudgetsMonotonically()
    {
        var best = DeviceProfile.For(PerformanceTier.BestQuality, Capable);
        var balanced = DeviceProfile.For(PerformanceTier.Balanced, Capable);
        var fastest = DeviceProfile.For(PerformanceTier.Fastest, Capable);

        Assert.True(best.VisualMaxSide >= balanced.VisualMaxSide && balanced.VisualMaxSide >= fastest.VisualMaxSide);
        Assert.True(best.VisualFrameMilliseconds <= balanced.VisualFrameMilliseconds && balanced.VisualFrameMilliseconds <= fastest.VisualFrameMilliseconds);
        Assert.True(best.VideoQueueCapacity >= balanced.VideoQueueCapacity && balanced.VideoQueueCapacity >= fastest.VideoQueueCapacity);
        Assert.True(best.AudioQueueCapacity >= balanced.AudioQueueCapacity && balanced.AudioQueueCapacity >= fastest.AudioQueueCapacity);
        Assert.True(best.LibraryPictureSide >= balanced.LibraryPictureSide && balanced.LibraryPictureSide >= fastest.LibraryPictureSide);
        Assert.True(best.HeroPictureSide >= balanced.HeroPictureSide && balanced.HeroPictureSide >= fastest.HeroPictureSide);
        Assert.True(best.SubjectStep <= balanced.SubjectStep && balanced.SubjectStep <= fastest.SubjectStep);
        Assert.True(best.Allows(VideoEffect.NeonEdges));
        Assert.True(balanced.Allows(VideoEffect.NeonEdges));
        Assert.False(fastest.Allows(VideoEffect.NeonEdges));
    }

    [Theory]
    [InlineData(GraphicsKind.Discrete, true)]
    [InlineData(GraphicsKind.Integrated, false)]
    [InlineData(GraphicsKind.Software, false)]
    [InlineData(GraphicsKind.Unknown, false)]
    public void TheCameraModelsUseOnlyAGraphicsCardOfItsOwn(GraphicsKind graphics, bool onGpu)
    {
        foreach (var tier in Enum.GetValues<PerformanceTier>())
        {
            var budget = DeviceProfile.For(tier, Capable with { Graphics = graphics });
            Assert.Equal(onGpu, budget.CameraModelsOnGpu);
            // Where the models run is a fact of the computer, not something taken away: it is never listed as a change.
            Assert.DoesNotContain(DeviceProfile.Changes(budget), change => change.Contains("graphics", StringComparison.Ordinal));
        }

        Assert.Equal(PerceptionLevel.Full, DeviceProfile.For(PerformanceTier.BestQuality, Capable with { Graphics = graphics }).CameraPerception);
    }

    [Fact]
    public void WindowsWithoutAnimationsTurnsTheWindowsOffOnEveryTier()
    {
        Assert.False(DeviceProfile.For(PerformanceTier.BestQuality, Capable with { SystemAnimations = false }).Animations);
        Assert.Equal(["The window's animations are off."], DeviceProfile.Changes(DeviceProfile.For(PerformanceTier.BestQuality, Capable with { SystemAnimations = false })));
    }

    [Fact]
    public void PreferencesSaysWhatWasChosenAndWhy()
    {
        Assert.Equal("Automatic: Best quality for this computer (8 processor threads, 32 GB of memory, graphics built into the processor).", DeviceProfile.Describe(PerformanceChoice.Automatic, Capable));
        Assert.Equal(
            "Automatic: Fastest for this computer (2 processor threads, 1.5 GB of memory, pictures drawn in software, no video decoding on the graphics card).",
            DeviceProfile.Describe(PerformanceChoice.Automatic, new DeviceFacts { Processors = 2, MemoryBytes = 3 * Gigabyte / 2, Graphics = GraphicsKind.Software, HardwareDecoding = [] }));
        Assert.Equal(
            "Automatic: Balanced for this computer (8 processor threads, a graphics card). Energy saver is on, so it is one step lighter until it goes off.",
            DeviceProfile.Describe(PerformanceChoice.Automatic, new DeviceFacts { Processors = 8, Graphics = GraphicsKind.Discrete, EnergySaver = true }));
        Assert.Equal("Automatic: Fastest for this computer (1 processor threads).", DeviceProfile.Describe(PerformanceChoice.Automatic, new DeviceFacts { EnergySaver = true }));
        Assert.Equal("Balanced, as chosen.", DeviceProfile.Describe(PerformanceChoice.Balanced, Capable));
        Assert.Equal(["Best quality", "Balanced", "Fastest"], Enum.GetValues<PerformanceTier>().Select(DeviceProfile.Name));
    }

    [Fact]
    public void AnUnknownChoiceInTheSettingsFileIsAutomatic()
    {
        Assert.Equal(PerformanceChoice.Automatic, new PlayerSettings { Performance = (PerformanceChoice)42 }.Normalize().Performance);
        Assert.Equal(PerformanceChoice.Fastest, new PlayerSettings { Performance = PerformanceChoice.Fastest }.Normalize().Performance);
    }

    [Theory]
    [InlineData(true, 0x8086u, 0L, GraphicsKind.Software)]
    [InlineData(false, 0x1414u, 0L, GraphicsKind.Software)]
    [InlineData(false, 0x8086u, 128L << 20, GraphicsKind.Integrated)]
    [InlineData(false, 0x1002u, 512L << 20, GraphicsKind.Integrated)]
    [InlineData(false, 0x10DEu, 4L << 30, GraphicsKind.Discrete)]
    public void TheAdapterSaysWhatDrawsThePictures(bool software, uint vendor, long dedicated, GraphicsKind kind)
    {
        Assert.Equal(kind, DeviceProfile.GraphicsFrom(software, vendor, dedicated));
    }

    [Fact]
    public void FactsMustBeGiven()
    {
        Assert.Throws<ArgumentNullException>(() => DeviceProfile.Classify(null!));
        Assert.Throws<ArgumentNullException>(() => DeviceProfile.Choose(PerformanceChoice.Automatic, null!));
        Assert.Throws<ArgumentNullException>(() => DeviceProfile.For(PerformanceTier.Fastest, null!));
        Assert.Throws<ArgumentNullException>(() => DeviceProfile.Describe(PerformanceChoice.Automatic, null!));
        Assert.Throws<ArgumentNullException>(() => DeviceProfile.Changes(null!));
    }
}
