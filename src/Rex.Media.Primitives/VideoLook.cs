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
    TealOrange,
    GoldenFilm,
    BleachBypass,
    CrossProcess,
    Infrared,
    SlideFilm,
    FadedPrint,
    Noir,
    Moonlight,
    Cyberpunk,
    Desert,
    Forest,
    Pastel,
    Negative,
    Solarize,
    Posterize,
    Duotone,
    Cyanotype,
    Technicolor,
    SilverGelatin,
    Super8,
    Autumn,
    Candlelight,
    Overcast,
    Spring,
    Dusk,
    Acid,
    Ultraviolet,
    Comic,
    Chrome,
    Vivid,
}

/// <summary>Short, screen-reader-friendly names shared by the player menu and Preferences.</summary>
public static class VideoLooks
{
    public static IReadOnlyList<string> Names { get; } =
    [
        "Original", "Cinema", "Clear", "Sunset", "Arctic",
        "Monochrome", "Vintage", "Neon", "Night vision",
        "Teal & orange", "Golden film", "Bleach bypass", "Cross process", "Infrared",
        "Slide film", "Faded print", "Noir", "Moonlight", "Cyberpunk", "Desert", "Forest",
        "Pastel", "Negative", "Solarize", "Posterize", "Duotone", "Cyanotype",
        "Technicolor", "Silver gelatin", "Super 8", "Autumn", "Candlelight", "Overcast", "Spring",
        "Dusk", "Acid", "Ultraviolet", "Comic", "Chrome", "Vivid",
    ];

    public static string Name(VideoLook look) => Enum.IsDefined(look) ? Names[(int)look] : Names[0];

    /// <summary>Visual grouping for the look picker, as the effects have.</summary>
    public static string Category(VideoLook look) => look switch
    {
        VideoLook.Cinema or VideoLook.Vintage or VideoLook.GoldenFilm or VideoLook.BleachBypass or VideoLook.CrossProcess
            or VideoLook.SlideFilm or VideoLook.FadedPrint or VideoLook.Noir or VideoLook.Technicolor or VideoLook.SilverGelatin
            or VideoLook.Super8 => "Film",
        VideoLook.Sunset or VideoLook.Arctic or VideoLook.TealOrange or VideoLook.Moonlight or VideoLook.Desert
            or VideoLook.Forest or VideoLook.Pastel or VideoLook.Autumn or VideoLook.Candlelight or VideoLook.Overcast
            or VideoLook.Spring or VideoLook.Dusk => "Mood",
        VideoLook.Neon or VideoLook.NightVision or VideoLook.Infrared or VideoLook.Cyberpunk or VideoLook.Solarize
            or VideoLook.Posterize or VideoLook.Duotone or VideoLook.Cyanotype or VideoLook.Acid or VideoLook.Ultraviolet
            or VideoLook.Comic or VideoLook.Chrome => "Stylised",
        _ => "Essentials",
    };

    /// <summary>One plain sentence saying what the look does.</summary>
    public static string Description(VideoLook look) => look switch
    {
        VideoLook.Original => "The picture as decoded, with brightness and contrast controls.",
        VideoLook.Cinema => "A gentle filmic curve with slightly warm highlights.",
        VideoLook.Clear => "A little more contrast and colour, nothing else.",
        VideoLook.Sunset => "Warm golden tones throughout.",
        VideoLook.Arctic => "Cool blue tones throughout.",
        VideoLook.Monochrome => "Black and white from the brightness of the picture.",
        VideoLook.Vintage => "Faded sepia, like an old print.",
        VideoLook.Neon => "Strong saturation with a cool cast.",
        VideoLook.NightVision => "Green image-intensifier tones.",
        VideoLook.TealOrange => "Cool shadows and warm highlights, the blockbuster grade.",
        VideoLook.GoldenFilm => "Warm film stock with lifted blacks and glowing highlights.",
        VideoLook.BleachBypass => "Low saturation and hard contrast, as if the silver stayed in the film.",
        VideoLook.CrossProcess => "Slide film developed in the wrong chemistry: shifted curves, cyan shadows, yellow highlights.",
        VideoLook.Infrared => "False colour: greens turn red and reds turn gold.",
        VideoLook.SlideFilm => "Deep saturation and a strong curve, like projected slide film.",
        VideoLook.FadedPrint => "Lifted blacks, soft highlights and a magenta cast, like a print left in the sun.",
        VideoLook.Noir => "Hard black and white with crushed shadows.",
        VideoLook.Moonlight => "Day for night: deep blue with the highlights kept.",
        VideoLook.Cyberpunk => "Magenta shadows and cyan highlights with lifted saturation.",
        VideoLook.Desert => "Warm, dusty and muted, with the greens pulled back.",
        VideoLook.Forest => "Green shadows under soft warm light.",
        VideoLook.Pastel => "Lifted and airy with soft colour and low contrast.",
        VideoLook.Negative => "Every colour inverted.",
        VideoLook.Solarize => "Tones above the middle fold back down, as in an over-exposed darkroom print.",
        VideoLook.Posterize => "Five flat levels per channel.",
        VideoLook.Duotone => "Two inks from the brightness of the picture: purple shadows and orange highlights.",
        VideoLook.Cyanotype => "A sun print: Prussian blue shadows on paper-white highlights.",
        VideoLook.Technicolor => "Three-strip dye separation: each primary pushed apart, rich and clean.",
        VideoLook.SilverGelatin => "Warm-toned black and white with soft, open highlights, like a darkroom print.",
        VideoLook.Super8 => "Soft, warm and low in contrast with a green-magenta tilt, like home movie film.",
        VideoLook.Autumn => "Greens turn amber, reds deepen, the light warms.",
        VideoLook.Candlelight => "Dark and warm with orange highlights and the blue drained from the shadows.",
        VideoLook.Overcast => "Flat cool light with no deep shadows or bright highlights.",
        VideoLook.Spring => "Fresh lifted greens under bright, airy light.",
        VideoLook.Dusk => "Violet shadows, pink highlights and a little haze.",
        VideoLook.Acid => "Every hue turned a third of the way round the colour wheel.",
        VideoLook.Ultraviolet => "Violet shadows and glowing white highlights, like a black light.",
        VideoLook.Comic => "Hard contrast and strong colour in a few flat steps, like a printed page.",
        VideoLook.Chrome => "Polished metal: grey with a cool sheen and bright reflections.",
        VideoLook.Vivid => "More colour and a touch more contrast, nothing stylised.",
        _ => "",
    };
}
