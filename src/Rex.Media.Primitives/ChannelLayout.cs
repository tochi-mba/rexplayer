namespace Rex.Media.Primitives;

/// <summary>
/// Speaker positions as the bits of a WAVE_FORMAT_EXTENSIBLE channel mask. Channels in a frame are
/// always in ascending bit order of their layout, which is also the order WASAPI expects.
/// </summary>
[Flags]
public enum ChannelLayout : uint
{
    None = 0,
    FrontLeft = 0x1,
    FrontRight = 0x2,
    FrontCenter = 0x4,
    LowFrequency = 0x8,
    BackLeft = 0x10,
    BackRight = 0x20,
    FrontLeftOfCenter = 0x40,
    FrontRightOfCenter = 0x80,
    BackCenter = 0x100,
    SideLeft = 0x200,
    SideRight = 0x400,
    TopCenter = 0x800,
    TopFrontLeft = 0x1000,
    TopFrontCenter = 0x2000,
    TopFrontRight = 0x4000,
    TopBackLeft = 0x8000,
    TopBackCenter = 0x10000,
    TopBackRight = 0x20000,

    Mono = FrontCenter,
    Stereo = FrontLeft | FrontRight,
    Surround = FrontLeft | FrontRight | FrontCenter,
    Quad = FrontLeft | FrontRight | BackLeft | BackRight,
    Surround50 = FrontLeft | FrontRight | FrontCenter | SideLeft | SideRight,
    Surround51 = FrontLeft | FrontRight | FrontCenter | LowFrequency | SideLeft | SideRight,
    Surround51Back = FrontLeft | FrontRight | FrontCenter | LowFrequency | BackLeft | BackRight,
    Surround61 = FrontLeft | FrontRight | FrontCenter | LowFrequency | BackCenter | SideLeft | SideRight,
    Surround71 = FrontLeft | FrontRight | FrontCenter | LowFrequency | BackLeft | BackRight | SideLeft | SideRight,
}

public static class ChannelLayouts
{
    /// <summary>The layout a stream with <paramref name="channels"/> channels and no mask is assumed to have.</summary>
    public static ChannelLayout Default(int channels) => channels switch
    {
        1 => ChannelLayout.Mono,
        2 => ChannelLayout.Stereo,
        3 => ChannelLayout.Surround,
        4 => ChannelLayout.Quad,
        5 => ChannelLayout.Surround50,
        6 => ChannelLayout.Surround51,
        7 => ChannelLayout.Surround61,
        8 => ChannelLayout.Surround71,
        _ => ChannelLayout.None,
    };

    public static int ChannelCount(this ChannelLayout layout) => System.Numerics.BitOperations.PopCount((uint)layout);

    /// <summary>The speaker of each channel, in frame order.</summary>
    public static ChannelLayout[] Speakers(this ChannelLayout layout)
    {
        var speakers = new ChannelLayout[layout.ChannelCount()];
        var index = 0;
        for (var bit = 0; bit < 32; bit++)
        {
            var speaker = (ChannelLayout)(1u << bit);
            if ((layout & speaker) != 0)
            {
                speakers[index++] = speaker;
            }
        }

        return speakers;
    }

    /// <summary>"Stereo", "5.1", or a count when the mask is unusual.</summary>
    public static string Describe(this ChannelLayout layout, int channels) => layout switch
    {
        ChannelLayout.Mono => "Mono",
        ChannelLayout.Stereo => "Stereo",
        ChannelLayout.Surround => "3.0",
        ChannelLayout.Quad => "Quadraphonic",
        ChannelLayout.Surround50 => "5.0",
        ChannelLayout.Surround51 or ChannelLayout.Surround51Back => "5.1",
        ChannelLayout.Surround61 => "6.1",
        ChannelLayout.Surround71 => "7.1",
        _ => channels == 1 ? "1 channel" : channels.ToString(System.Globalization.CultureInfo.InvariantCulture) + " channels",
    };
}
