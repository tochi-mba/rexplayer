namespace Rex.Media.Primitives;

/// <summary>
/// Real-time picture styles applied by the video renderer to decoded pixels, without changing
/// the media file or its embedded colour metadata. Original reproduces the decoder's output.
/// </summary>
public enum VideoLook
{
    Original,
    Cinema,
    Clear,
    Sunset,
    Arctic,
    Monochrome,
    Vintage,
    Neon,
    NightVision,
}

/// <summary>Short, screen-reader-friendly names shared by the player menu and Preferences.</summary>
public static class VideoLooks
{
    public static IReadOnlyList<string> Names { get; } =
    [
        "Original", "Cinema", "Clear", "Sunset", "Arctic",
        "Monochrome", "Vintage", "Neon", "Night vision",
    ];

    public static string Name(VideoLook look) => Enum.IsDefined(look) ? Names[(int)look] : Names[0];
}
