namespace Rex.Media.Settings;

/// <summary>What the picture area shows while sound without pictures plays (AU-18).</summary>
public enum VisualizerChoice
{
    Off,
    Spectrum,
    Oscilloscope,
    Meters,
    Spectrogram,

    /// <summary>A spinning record whose groove is the sound itself, the cover as its label.</summary>
    Vinyl,

    /// <summary>The spectrum radiating from a circle that swells with the bass.</summary>
    Halo,

    /// <summary>The waveform mirrored about the middle, as sound is often pictured.</summary>
    Mirror,

    /// <summary>The spectrum as a ridge of light under a soft glow.</summary>
    Aurora,

    /// <summary>Sparks the music throws up, rising and fading.</summary>
    Embers,

    /// <summary>Rings that burst outward on every beat.</summary>
    Ripples,

    /// <summary>The whole stage flashing colour with the music.</summary>
    Strobe,

    /// <summary>The listener, seen by the camera, drawn in light that moves with the music.</summary>
    Silhouette,

    /// <summary>Live camera or cover art cut, graded and moved to the music.</summary>
    BeatEdit,

    /// <summary>Continuous, spectrally driven light filaments and drum pressure waves.</summary>
    Resonance,

    /// <summary>The last seconds of the music scrolling by: its spectrum, the beat grid and every drum hit.</summary>
    Groove,

    /// <summary>A flight down a tunnel of light rings that pass on the beat.</summary>
    Flight,

    /// <summary>Sand gathering into the figures a vibrating plate sings, the mode set by the pitch.</summary>
    Cymatics,

    /// <summary>The spectrum laid down as ridges, each new one at the front, the old climbing away.</summary>
    Ridgelines,

    /// <summary>A drive over a neon grid toward a striped sun, the spectrum raising the mountains.</summary>
    Synthwave,

    /// <summary>Shells launched by the kick and burst by the snare.</summary>
    Fireworks,

    /// <summary>A storm whose bolts strike on the kick and crack on the snare.</summary>
    Lightning,

    /// <summary>Blobs that swell with their part of the spectrum and melt together on the kick.</summary>
    Lava,

    /// <summary>Each beat lights the star of its pitch, drawing the tune as a constellation.</summary>
    Constellation,

    /// <summary>A spiral galaxy turning on the beat, its arms lit by the spectrum.</summary>
    Galaxy,

    /// <summary>Shards of the spectrum in turning mirrors that click round on the beat.</summary>
    Kaleidoscope,

    /// <summary>A flock wheeling as one, led by the melody and drawn tight by the kick.</summary>
    Murmuration,

    /// <summary>Drops of ink let fall by the drums and carried in a current the bass stirs.</summary>
    Ink,

    /// <summary>A night city whose towers are the spectrum and whose searchlights cross on the beat.</summary>
    Skyline,

    /// <summary>Rings of petals the spectrum opens, turning a step on each beat.</summary>
    Mandala,

    /// <summary>The listener's outline frozen on every beat, the clones drifting away behind them.</summary>
    Clones,

    /// <summary>The camera as a slit-scan mirror, each part of the picture a different moment, deepened by the bass.</summary>
    TimeWarp,

    /// <summary>A glow round the listener that breathes with the bass, with shockwaves on each kick.</summary>
    Aura,

    /// <summary>The camera's picture on a wall of lights, each column gated by its part of the spectrum.</summary>
    LedWall,

    /// <summary>The listener made of motes that blast apart on each kick and re-form between.</summary>
    Stardust,

    /// <summary>The camera's picture folded into mirrors that turn on the beat.</summary>
    HallOfMirrors,

    /// <summary>Strokes of light wherever the listener's outline moves, flaring on the kick.</summary>
    LightPainting,

    /// <summary>A bar of light that sweeps the picture once a beat, the picture fading behind it.</summary>
    Scanner,

    /// <summary>The picture in tiles that the kick blasts apart and that mend between beats.</summary>
    Shatter,

    /// <summary>Spotlights that follow the listener and change colour on the kick.</summary>
    Spotlight,

    /// <summary>The picture inside itself, level by level, each a beat further back.</summary>
    InfinityMirror,

    /// <summary>The picture bent through ripples each kick drops at the listener.</summary>
    Water,

    /// <summary>The listener pouring ink into a fluid their movement stirs (on the graphics card, ADR-0022).</summary>
    InkBody,
    /// <summary>The listener's hands and body as lines of light, plasma between the fingertips (on the graphics card).</summary>
    NeonSkeleton,
    /// <summary>The listener's last moments cascading behind them as coloured outlines on the beat (on the graphics card).</summary>
    EchoClones,
}

/// <summary>Whose sound the visualisations listen to (AU-25).</summary>
public enum ListenTo
{
    /// <summary>What rexplayer plays.</summary>
    Player,

    /// <summary>Everything this computer plays through its default output.</summary>
    Everything,

    /// <summary>One app (and the processes it started), remembered by its program's name.</summary>
    App,
}
