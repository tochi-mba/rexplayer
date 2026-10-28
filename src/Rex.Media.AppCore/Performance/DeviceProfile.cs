using System.Globalization;
using Rex.Media.Primitives;
using Rex.Media.Settings;

namespace Rex.Media.AppCore.Performance;

/// <summary>What draws the pictures: nothing known yet, the processor (software), graphics built into the processor, or a card of its own.</summary>
public enum GraphicsKind
{
    Unknown,
    Software,
    Integrated,
    Discrete,
}

/// <summary>How much the player asks of the computer: everything, a little less, or the least that still plays well.</summary>
public enum PerformanceTier
{
    BestQuality,
    Balanced,
    Fastest,
}

/// <summary>
/// What the computer is, measured by the window at launch and again when the power changes: the
/// processor threads this process may use, the memory it may use (a cap on the process counts),
/// what draws pictures, which codecs the graphics card decodes (null until known), and the power.
/// </summary>
public sealed record DeviceFacts
{
    public int Processors { get; init; } = 1;

    /// <summary>Bytes of memory the process may use; 0 when unknown.</summary>
    public long MemoryBytes { get; init; }

    public GraphicsKind Graphics { get; init; }

    /// <summary>The codecs the graphics card decodes, or null before they are known.</summary>
    public IReadOnlyCollection<CodecId>? HardwareDecoding { get; init; }

    public bool OnBattery { get; init; }

    /// <summary>Windows' energy saver (battery saver) is on.</summary>
    public bool EnergySaver { get; init; }

    /// <summary>Windows shows animations (Settings, Accessibility, Visual effects).</summary>
    public bool SystemAnimations { get; init; } = true;
}

/// <summary>
/// What a tier changes, as the window applies it. Every value on <see cref="PerformanceTier.BestQuality"/>
/// is what the player does with no tiers at all, so a capable computer loses nothing.
/// </summary>
public sealed record DeviceBudget
{
    public PerformanceTier Tier { get; init; }

    /// <summary>The longest side a software visualisation is drawn at, in pixels, before it is scaled up.</summary>
    public int VisualMaxSide { get; init; } = 1920;

    /// <summary>The shortest time between two frames of a visualisation, in milliseconds (15 allows sixty a second).</summary>
    public int VisualFrameMilliseconds { get; init; } = 15;

    /// <summary>The heaviest picture effects are left off, with a note saying why.</summary>
    public bool HeavyEffectsOff { get; init; }

    /// <summary>Subject Lock looks at every this-many-th sample across and down: 2 follows the subject at half size.</summary>
    public int SubjectStep { get; init; } = 1;

    /// <summary>The library may go on being transcribed while something plays, when the user allows it.</summary>
    public bool TranscribeWhilePlaying { get; init; } = true;

    /// <summary>Background transcription and episode section analysis yield while the library is open.</summary>
    public bool PauseBackgroundWorkWhileBrowsing { get; init; }

    /// <summary>A launch with nothing to open prepares only what its window needs, the rest when first used.</summary>
    public bool LeanLaunch { get; init; }

    /// <summary>The maximum width, in pixels, at which library pictures are decoded.</summary>
    public int LibraryPictureSide { get; init; } = 320;

    /// <summary>The longest side, in pixels, at which a page banner's artwork or backdrop is decoded.</summary>
    public int HeroPictureSide { get; init; } = 1600;

    /// <summary>The most decoded library pictures may take in memory, in bytes.</summary>
    public long PictureMemoryBytes { get; init; } = 160L * 1024 * 1024;

    /// <summary>What decoded pictures keep once the view changes: the pictures on screen, not every view visited.</summary>
    public long PictureMemoryIdleBytes => PictureMemoryBytes / 4;

    /// <summary>What decoded pictures keep while the library is closed.</summary>
    public long PictureMemoryFloorBytes => PictureMemoryBytes / 16;

    /// <summary>The timeline shows a preview picture under the pointer when the user allows it.</summary>
    public bool SeekPreview { get; init; } = true;

    /// <summary>The window's own animations play.</summary>
    public bool Animations { get; init; } = true;

    /// <summary>Video packets waiting for the decoder (see EngineOptions.VideoQueueCapacity).</summary>
    public int VideoQueueCapacity { get; init; } = 32;

    /// <summary>Audio packets waiting for the decoder (see EngineOptions.AudioQueueCapacity).</summary>
    public int AudioQueueCapacity { get; init; } = 64;

    /// <summary>Whether <paramref name="effect"/> may be shown on this tier.</summary>
    public bool Allows(VideoEffect effect) => !(HeavyEffectsOff && DeviceProfile.HeavyEffects.Contains(effect));
}

/// <summary>
/// Classifies the computer into a <see cref="PerformanceTier"/> from <see cref="DeviceFacts"/>, and
/// says what each tier changes (<see cref="DeviceBudget"/>) in numbers for the window and in words
/// for Preferences. Automatic never takes quality from a capable computer: only few processor
/// threads, little memory, drawing in software, or a graphics card that cannot decode ordinary
/// video lower the tier, and Windows' energy saver lowers it one step. Battery alone changes
/// nothing about the picture. A tier the user chooses is used as it is.
/// </summary>
public static class DeviceProfile
{
    private const long Gigabyte = 1024L * 1024 * 1024;
    private const long EnergySaverPictureMemory = 64L * 1024 * 1024;

    /// <summary>
    /// The picture effects measured above 100 ms per full-HD frame in the supplied Windows CI
    /// measurements. At that cost they are far beyond a real-time frame budget, so Fastest leaves
    /// them off; lower-cost effects remain available.
    /// </summary>
    public static IReadOnlySet<VideoEffect> HeavyEffects { get; } = new HashSet<VideoEffect>
    {
        VideoEffect.GhostwireMotion, VideoEffect.ChromaticContours, VideoEffect.Fireflies, VideoEffect.LumenWeave,
        VideoEffect.Ghostwire, VideoEffect.GhostwireMask, VideoEffect.InkTrace, VideoEffect.NeonEdges,
        VideoEffect.Bokeh, VideoEffect.FlowField, VideoEffect.LensBloom, VideoEffect.Confetti,
        VideoEffect.OilPaint, VideoEffect.TopographicContours, VideoEffect.ZoomBurst, VideoEffect.BrickBuild,
        VideoEffect.SnowSettle, VideoEffect.Pinball, VideoEffect.Bubbles, VideoEffect.StainedGlass,
        VideoEffect.StippleEngrave, VideoEffect.RainGlass, VideoEffect.Shatter, VideoEffect.PixelSort,
        VideoEffect.Parallax, VideoEffect.Hologram, VideoEffect.Aurora, VideoEffect.ElectricEdges,
        VideoEffect.ScanSweep, VideoEffect.CelShade, VideoEffect.SonarPing, VideoEffect.HeatHaze,
        VideoEffect.DepthHaze, VideoEffect.Torchlight, VideoEffect.CubeMosaic,
    };

    /// <summary>The software rasteriser's vendor id: a graphics device in name only.</summary>
    public const uint SoftwareVendor = 0x1414;

    /// <summary>
    /// What draws the pictures, from the adapter's own description: the software rasteriser, a card
    /// with more than half a gigabyte of its own memory, or graphics built into the processor (which
    /// report only a small reserve as their own).
    /// </summary>
    public static GraphicsKind GraphicsFrom(bool software, uint vendorId, long dedicatedBytes) =>
        software || vendorId == SoftwareVendor ? GraphicsKind.Software
        : dedicatedBytes > Gigabyte / 2 ? GraphicsKind.Discrete
        : GraphicsKind.Integrated;

    /// <summary>The tier the facts alone call for, before power and the user's choice.</summary>
    public static PerformanceTier Classify(DeviceFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        var memory = facts.MemoryBytes;
        var noVideoDecoding = facts.HardwareDecoding is { } decoding && !decoding.Contains(CodecId.H264);
        if (facts.Graphics == GraphicsKind.Software || facts.Processors <= 2 || (memory > 0 && memory < (7 * Gigabyte / 2))
            || (noVideoDecoding && facts.Processors <= 4))
        {
            return PerformanceTier.Fastest;
        }

        return facts.Processors <= 4 || (memory > 0 && memory < 7 * Gigabyte) ? PerformanceTier.Balanced : PerformanceTier.BestQuality;
    }

    /// <summary>The tier to use: the user's choice, or for Automatic the facts' tier, a step lower while energy saver is on.</summary>
    public static PerformanceTier Choose(PerformanceChoice choice, DeviceFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        return choice switch
        {
            PerformanceChoice.BestQuality => PerformanceTier.BestQuality,
            PerformanceChoice.Balanced => PerformanceTier.Balanced,
            PerformanceChoice.Fastest => PerformanceTier.Fastest,
            _ => Automatic(facts),
        };
    }

    private static PerformanceTier Automatic(DeviceFacts facts)
    {
        var tier = Classify(facts);
        return facts.EnergySaver && tier < PerformanceTier.Fastest ? tier + 1 : tier;
    }

    /// <summary>What <paramref name="tier"/> changes; with Windows' animations off, the window's are off on every tier.</summary>
    public static DeviceBudget For(PerformanceTier tier, DeviceFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        var budget = tier switch
        {
            PerformanceTier.Balanced => new DeviceBudget
            {
                Tier = tier,
                VisualMaxSide = 1280,
                VisualFrameMilliseconds = 33,
                PauseBackgroundWorkWhileBrowsing = true,
                VideoQueueCapacity = 24,
                PictureMemoryBytes = 96L * 1024 * 1024,
            },
            PerformanceTier.Fastest => new DeviceBudget
            {
                Tier = tier,
                VisualMaxSide = 854,
                VisualFrameMilliseconds = 40,
                HeavyEffectsOff = true,
                SubjectStep = 2,
                TranscribeWhilePlaying = false,
                PauseBackgroundWorkWhileBrowsing = true,
                LeanLaunch = true,
                LibraryPictureSide = 240,
                HeroPictureSide = 960,
                PictureMemoryBytes = 32L * 1024 * 1024,
                SeekPreview = false,
                Animations = false,
                VideoQueueCapacity = 12,
                AudioQueueCapacity = 32,
            },
            _ => new DeviceBudget { Tier = PerformanceTier.BestQuality },
        };
        // Energy Saver holds picture memory down whatever mode was chosen.
        if (facts.EnergySaver && budget.PictureMemoryBytes > EnergySaverPictureMemory)
        {
            budget = budget with { PictureMemoryBytes = EnergySaverPictureMemory };
        }

        return facts.SystemAnimations ? budget : budget with { Animations = false };
    }

    /// <summary>The tier's name as Preferences shows it.</summary>
    public static string Name(PerformanceTier tier) => tier switch
    {
        PerformanceTier.Balanced => "Balanced",
        PerformanceTier.Fastest => "Fastest",
        _ => "Best quality",
    };

    /// <summary>One plain sentence on what was chosen and why, for Preferences.</summary>
    public static string Describe(PerformanceChoice choice, DeviceFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        var tier = Choose(choice, facts);
        if (choice != PerformanceChoice.Automatic)
        {
            return $"{Name(tier)}, as chosen.";
        }

        var memory = facts.MemoryBytes > 0 ? string.Create(CultureInfo.InvariantCulture, $", {facts.MemoryBytes / (double)Gigabyte:0.#} GB of memory") : "";
        var graphics = facts.Graphics switch
        {
            GraphicsKind.Software => ", pictures drawn in software",
            GraphicsKind.Integrated => ", graphics built into the processor",
            GraphicsKind.Discrete => ", a graphics card",
            _ => "",
        };
        var decoding = facts.HardwareDecoding is { } codecs && !codecs.Contains(CodecId.H264) ? ", no video decoding on the graphics card" : "";
        var saver = facts.EnergySaver && Classify(facts) < PerformanceTier.Fastest ? " Energy saver is on, so it is one step lighter until it goes off." : "";
        return $"Automatic: {Name(tier)} for this computer ({facts.Processors} processor threads{memory}{graphics}{decoding}).{saver}";
    }

    /// <summary>What the tier changes, a sentence each, for Preferences; empty for the best quality, which changes nothing.</summary>
    public static IReadOnlyList<string> Changes(DeviceBudget budget)
    {
        ArgumentNullException.ThrowIfNull(budget);
        var changes = new List<string>();
        if (budget.VisualMaxSide < 1920 || budget.VisualFrameMilliseconds > 15)
        {
            changes.Add(string.Create(CultureInfo.InvariantCulture, $"Music visualisations are drawn at up to {budget.VisualMaxSide} pixels across and {1000 / budget.VisualFrameMilliseconds} pictures a second, then scaled to the window."));
        }

        if (budget.HeavyEffectsOff)
        {
            changes.Add(string.Create(CultureInfo.InvariantCulture, $"The {HeavyEffects.Count} heaviest picture effects stay off. Choosing one says so."));
        }

        if (budget.SubjectStep > 1)
        {
            changes.Add("Subject Lock follows its subject in a half-size copy of each picture.");
        }

        if (!budget.TranscribeWhilePlaying)
        {
            changes.Add("The library is transcribed only while nothing plays.");
        }

        if (budget.PauseBackgroundWorkWhileBrowsing)
        {
            changes.Add("Background transcription and intro or credit analysis pause while browsing the library.");
        }

        if (budget.LeanLaunch)
        {
            changes.Add("Starting with nothing to play prepares only the window; video and sound are made ready when first used.");
        }

        if (budget.LibraryPictureSide < 320)
        {
            changes.Add($"Library pictures are decoded up to {budget.LibraryPictureSide} pixels across to use less memory.");
        }

        if (budget.HeroPictureSide < 1600)
        {
            changes.Add($"Page banners are decoded up to {budget.HeroPictureSide} pixels across to use less memory.");
        }

        if (!budget.SeekPreview)
        {
            changes.Add("The timeline shows no preview picture under the pointer.");
        }

        if (!budget.Animations)
        {
            changes.Add("The window's animations are off.");
        }

        if (budget.VideoQueueCapacity < 32)
        {
            changes.Add("Fewer pictures wait to be decoded, so playing uses less memory.");
        }

        return changes;
    }
}
