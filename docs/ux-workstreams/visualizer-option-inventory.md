# Verified source inventory: Windows visualizer settings and persistence keys

**Scope:** merged PR #482, checklist #482.1. This document describes the declared settings in the current source code on PR #488, **not** evidence of native rendering, keyboard/accessibility, transitions, hardware limits or finalized screenshot quality.

## Source of truth

- `src/Rex.Media.Settings/PlayerSettings.cs`: all enum-defined modes.
- `src/Rex.Media.AppCore/Player/VisualizerOptions.cs`: defaults, key names, labels, value kinds, numeric bounds and choice labels.
- `src/Rex.Media.AppCore/Visuals/VisualScene.cs`: whether the mode has a raster scene and `meter` control.
- `tests/Rex.Media.Tests/AppCore/VisualizerGroupsTests.cs` and `tests/Rex.Media.Tests/Audio/VisualizerOptionsTests.cs`: definitions/defaults/key namespaces and independent saved mode state.

All 42 enum modes are enumerated below. There are 146 *mode-specific* declared settings, plus common options for all modes and a beat meter for 37 raster-scene modes. `Off` is an enum value, not a playable visualizer. Each option's persisted key is exact and case-sensitive in the storage dictionary.

## Shared setting contract

Every mode `M` defines these keys in `PlayerSettings.VisualOptions`:

- `<m>.colors`: choice; default is `DefaultPalette(M)`, enumerated in the table and according to the ordered palettes `Accent=0, Pitch=1, Rainbow=2, Warm=3, Cool=4, OneColor=5, Neon=6, Fire=7, Aurora=8, Ocean=9, Sunset=10`.
- `<m>.color`: 24-bit RGB color; default `0xFF3B30`; `Rgb` masks to `0xFFFFFF`.
- `<m>.sensitivity`: number, default `1`, range `0.25–4`.
- Raster-scene modes *also* expose `<m>.meter`: toggle, default `0` (off).

Here `<m>` is the enum name lowercased invariantly, e.g. `vinyl`, `stardust`, `beatEdit` → `beatedit`. `VisualizerOptions.With` writes individual values using invariant round-trip number formatting; `Reset` removes only keys prefixed with that mode's lowercase name and a dot. Missing, nonfinite and nonnumeric values read as defaults; numerical values clamp to bounds; invalid choice indices fall back to defaults. Options from different modes are separately persisted. This is source behavior, not proof of every Windows settings-view interaction.

## All modes, shared keys, default palette and scene state

| Mode | Default palette | Raster scene; meter | Exact shared persistence keys |
| --- | --- | --- | --- |
| `Off` | Accent | No | `off.colors`, `off.color`, `off.sensitivity` |
| `Spectrum` | Accent | No | `spectrum.colors`, `spectrum.color`, `spectrum.sensitivity` |
| `Oscilloscope` | Accent | No | `oscilloscope.colors`, `oscilloscope.color`, `oscilloscope.sensitivity` |
| `Meters` | Accent | No | `meters.colors`, `meters.color`, `meters.sensitivity` |
| `Spectrogram` | Accent | No | `spectrogram.colors`, `spectrogram.color`, `spectrogram.sensitivity` |
| `Vinyl` | Sunset | Yes | `vinyl.colors`, `vinyl.color`, `vinyl.sensitivity`, `vinyl.meter` |
| `Halo` | Neon | Yes | `halo.colors`, `halo.color`, `halo.sensitivity`, `halo.meter` |
| `Mirror` | Pitch | Yes | `mirror.colors`, `mirror.color`, `mirror.sensitivity`, `mirror.meter` |
| `Aurora` | Aurora | Yes | `aurora.colors`, `aurora.color`, `aurora.sensitivity`, `aurora.meter` |
| `Embers` | Fire | Yes | `embers.colors`, `embers.color`, `embers.sensitivity`, `embers.meter` |
| `Ripples` | Ocean | Yes | `ripples.colors`, `ripples.color`, `ripples.sensitivity`, `ripples.meter` |
| `Strobe` | Pitch | Yes | `strobe.colors`, `strobe.color`, `strobe.sensitivity`, `strobe.meter` |
| `Silhouette` | Neon | Yes | `silhouette.colors`, `silhouette.color`, `silhouette.sensitivity`, `silhouette.meter` |
| `BeatEdit` | Neon | Yes | `beatedit.colors`, `beatedit.color`, `beatedit.sensitivity`, `beatedit.meter` |
| `Resonance` | Ocean | Yes | `resonance.colors`, `resonance.color`, `resonance.sensitivity`, `resonance.meter` |
| `Groove` | Neon | Yes | `groove.colors`, `groove.color`, `groove.sensitivity`, `groove.meter` |
| `Flight` | Sunset | Yes | `flight.colors`, `flight.color`, `flight.sensitivity`, `flight.meter` |
| `Cymatics` | Ocean | Yes | `cymatics.colors`, `cymatics.color`, `cymatics.sensitivity`, `cymatics.meter` |
| `Ridgelines` | Aurora | Yes | `ridgelines.colors`, `ridgelines.color`, `ridgelines.sensitivity`, `ridgelines.meter` |
| `Synthwave` | Sunset | Yes | `synthwave.colors`, `synthwave.color`, `synthwave.sensitivity`, `synthwave.meter` |
| `Fireworks` | Rainbow | Yes | `fireworks.colors`, `fireworks.color`, `fireworks.sensitivity`, `fireworks.meter` |
| `Lightning` | Cool | Yes | `lightning.colors`, `lightning.color`, `lightning.sensitivity`, `lightning.meter` |
| `Lava` | Fire | Yes | `lava.colors`, `lava.color`, `lava.sensitivity`, `lava.meter` |
| `Constellation` | Cool | Yes | `constellation.colors`, `constellation.color`, `constellation.sensitivity`, `constellation.meter` |
| `Galaxy` | Aurora | Yes | `galaxy.colors`, `galaxy.color`, `galaxy.sensitivity`, `galaxy.meter` |
| `Kaleidoscope` | Rainbow | Yes | `kaleidoscope.colors`, `kaleidoscope.color`, `kaleidoscope.sensitivity`, `kaleidoscope.meter` |
| `Murmuration` | Sunset | Yes | `murmuration.colors`, `murmuration.color`, `murmuration.sensitivity`, `murmuration.meter` |
| `Ink` | Neon | Yes | `ink.colors`, `ink.color`, `ink.sensitivity`, `ink.meter` |
| `Skyline` | Neon | Yes | `skyline.colors`, `skyline.color`, `skyline.sensitivity`, `skyline.meter` |
| `Mandala` | Sunset | Yes | `mandala.colors`, `mandala.color`, `mandala.sensitivity`, `mandala.meter` |
| `Clones` | Neon | Yes | `clones.colors`, `clones.color`, `clones.sensitivity`, `clones.meter` |
| `TimeWarp` | Aurora | Yes | `timewarp.colors`, `timewarp.color`, `timewarp.sensitivity`, `timewarp.meter` |
| `Aura` | Fire | Yes | `aura.colors`, `aura.color`, `aura.sensitivity`, `aura.meter` |
| `LedWall` | Rainbow | Yes | `ledwall.colors`, `ledwall.color`, `ledwall.sensitivity`, `ledwall.meter` |
| `Stardust` | Ocean | Yes | `stardust.colors`, `stardust.color`, `stardust.sensitivity`, `stardust.meter` |
| `HallOfMirrors` | Neon | Yes | `hallofmirrors.colors`, `hallofmirrors.color`, `hallofmirrors.sensitivity`, `hallofmirrors.meter` |
| `LightPainting` | Rainbow | Yes | `lightpainting.colors`, `lightpainting.color`, `lightpainting.sensitivity`, `lightpainting.meter` |
| `Scanner` | Cool | Yes | `scanner.colors`, `scanner.color`, `scanner.sensitivity`, `scanner.meter` |
| `Shatter` | Fire | Yes | `shatter.colors`, `shatter.color`, `shatter.sensitivity`, `shatter.meter` |
| `Spotlight` | Sunset | Yes | `spotlight.colors`, `spotlight.color`, `spotlight.sensitivity`, `spotlight.meter` |
| `InfinityMirror` | Aurora | Yes | `infinitymirror.colors`, `infinitymirror.color`, `infinitymirror.sensitivity`, `infinitymirror.meter` |
| `Water` | Ocean | Yes | `water.colors`, `water.color`, `water.sensitivity`, `water.meter` |

## All mode-specific settings

A missing mode in this section has no additional settings beyond its shared ones. Every row records the exact persisted key, UI label, setting kind, default and, when applicable, range or choice texts.

| Persisted key | UI label | Kind, default and bounds/choices |
| --- | --- | --- |
| `spectrum.bars` | Bars | Number, default 48, range 12–128 |
| `spectrum.peaks` | Show the peaks | Toggle, default 1 |
| `spectrum.mirrored` | Mirror about the middle | Toggle, default 0 |
| `oscilloscope.thickness` | Line thickness | Number, default 2, range 1–8 |
| `vinyl.speed` | Turns at | Choice, default 0, choices: 0=33 1/3 rpm / 1=45 rpm / 2=78 rpm |
| `vinyl.depth` | How brightly the music lights the groove | Number, default 1, range 0.25–2.5 |
| `vinyl.arm` | Show the tone-arm | Toggle, default 1 |
| `vinyl.label` | The cover as the label | Toggle, default 1 |
| `vinyl.sheen` | The lamp's sheen on the vinyl | Toggle, default 1 |
| `vinyl.wave` | The sound as a ring round the record | Toggle, default 1 |
| `vinyl.strobe` | Strobe dots on the platter that flash on the beat | Toggle, default 1 |
| `vinyl.glow` | Glow | Number, default 0.7, range 0–2 |
| `halo.rays` | Rays | Number, default 48, range 12–128 |
| `halo.thickness` | Ray thickness | Number, default 4, range 1–12 |
| `halo.spin` | Turn slowly | Toggle, default 0 |
| `halo.trails` | Light trails | Number, default 0.72, range 0–0.95 |
| `halo.burst` | Sparks on the beat | Toggle, default 1 |
| `mirror.columns` | Columns | Number, default 96, range 24–192 |
| `mirror.thickness` | Wave thickness | Number, default 2, range 1–8 |
| `mirror.trails` | Light trails | Number, default 0.7, range 0–0.95 |
| `mirror.stereo` | Split left and right channels | Toggle, default 1 |
| `aurora.curtains` | Light curtains | Number, default 3, range 1–6 |
| `aurora.height` | Curtain height | Number, default 1, range 0.25–2 |
| `aurora.drift` | How fast they drift | Number, default 1, range 0–3 |
| `aurora.stars` | Stars | Toggle, default 1 |
| `embers.amount` | How many sparks | Number, default 90, range 20–300 |
| `embers.speed` | How fast they rise | Number, default 1, range 0.25–3 |
| `embers.wind` | Sideways drift | Number, default 0.3, range -2–2 |
| `embers.smoke` | How long their smoke lingers | Number, default 0.6, range 0–0.9 |
| `embers.burst` | Burst on beats | Toggle, default 1 |
| `ripples.thickness` | Ring thickness | Number, default 2.5, range 1–8 |
| `ripples.rings` | Rings in each drop | Number, default 3, range 1–4 |
| `ripples.lifetime` | Ring lifetime | Number, default 1.5, range 0.4–4 |
| `ripples.where` | Ripple origin | Choice, default 0, choices: 0=Centre / 1=Across the stage / 2=Follow the pitch |
| `ripples.drops` | On each | Choice, default 0, choices: 0=Beat / 1=Strong beat / 2=Drop |
| `strobe.flashes` | At most this many flashes a second | Number, default 3, range 1–10 |
| `strobe.fade` | How long a flash takes to fade (seconds) | Number, default 0.4, range 0.1–2 |
| `strobe.pattern` | Flash shape | Choice, default 0, choices: 0=Whole stage / 1=Halves that trade places / 2=Bars that step along / 3=Rays from the middle / 4=Chequerboard |
| `strobe.step` | Colour | Choice, default 0, choices: 0=Follows the pitch / 1=Steps through the colours each flash |
| `strobe.glow` | Background glow | Number, default 0.25, range 0–1 |
| `silhouette.style` | Draw me as | Choice, default 0, choices: 0=A glowing outline / 1=Filled with the music / 2=Sparks / 3=Motion echoes / 4=Split neon |
| `silhouette.mirror` | Mirror the camera, like a mirror | Toggle, default 1 |
| `silhouette.threshold` | Foreground sensitivity (lower detects more detail) | Number, default 0.09, range 0.03–0.35 |
| `silhouette.background` | Room behind me | Choice, default 0, choices: 0=Hidden / 1=Dimly visible |
| `beatedit.style` | Edit style | Choice, default 0, choices: 0=Velocity / 1=Glitch / 2=Hype / 3=Dreamy / 4=Everything |
| `beatedit.source` | Picture source | Choice, default 0, choices: 0=Camera, then cover / 1=Cover art |
| `beatedit.follow` | Keep people centred (best with a clear background) | Toggle, default 1 |
| `beatedit.intensity` | Edit intensity | Number, default 1, range 0.2–2 |
| `beatedit.cuts` | Change grade every | Choice, default 1, choices: 0=Beat / 1=2 beats / 2=4 beats / 3=8 beats |
| `beatedit.mirror` | Mirror the camera | Toggle, default 1 |
| `beatedit.flash` | White flashes on strong beats | Toggle, default 1 |
| `beatedit.flashes` | At most this many flashes a second | Number, default 3, range 1–3 |
| `beatedit.grain` | Film grain | Toggle, default 1 |
| `beatedit.bars` | Cinematic bars | Toggle, default 0 |
| `resonance.density` | Luminous filaments | Number, default 7, range 3–12 |
| `resonance.depth` | Spectral movement | Number, default 1.1, range 0.3–2 |
| `resonance.flow` | Fluid flow | Number, default 0.8, range 0.1–2 |
| `resonance.contrast` | Light intensity | Number, default 1.15, range 0.5–1.8 |
| `resonance.trails` | Light trails | Number, default 0.45, range 0–0.9 |
| `resonance.mirror` | Reflect frequencies across the screen | Toggle, default 1 |
| `resonance.rings` | Pressure waves on drum hits | Toggle, default 1 |
| `groove.seconds` | Seconds shown | Number, default 4, range 2–10 |
| `groove.spectrum` | The spectrum underneath | Toggle, default 1 |
| `groove.grid` | The beat grid | Toggle, default 1 |
| `groove.lanes` | The kick, snare and hi-hat lanes | Toggle, default 1 |
| `flight.ahead` | Beats each ring takes to reach you | Number, default 8, range 2–16 |
| `flight.trails` | Motion blur | Number, default 0.55, range 0–0.9 |
| `flight.spectrum` | Rings shaped by the spectrum | Toggle, default 1 |
| `flight.stars` | Stars streaming past | Toggle, default 1 |
| `cymatics.detail` | How fine the figures get | Number, default 1, range 0.5–1.5 |
| `cymatics.plate` | Plate | Choice, default 0, choices: 0=Square / 1=Round |
| `cymatics.glow` | Glow | Number, default 1, range 0.3–2 |
| `ridgelines.lines` | Ridges | Number, default 36, range 12–72 |
| `ridgelines.height` | Ridge height | Number, default 1, range 0.4–2 |
| `synthwave.speed` | Grid lines a beat | Number, default 2, range 1–4 |
| `synthwave.sun` | The sun | Toggle, default 1 |
| `synthwave.mountains` | The mountains | Toggle, default 1 |
| `fireworks.gravity` | Gravity | Number, default 1, range 0.4–2 |
| `fireworks.trails` | Trails | Number, default 0.75, range 0–0.92 |
| `lightning.branches` | Forks in each bolt | Number, default 3, range 0–6 |
| `lightning.flash` | How brightly the sky flashes | Number, default 0.6, range 0–1 |
| `lightning.rain` | Rain | Toggle, default 1 |
| `lava.blobs` | Blobs | Number, default 7, range 3–12 |
| `lava.glow` | Glow | Number, default 1, range 0–2 |
| `lava.merge` | Gather on the kick | Toggle, default 1 |
| `constellation.stars` | Stars | Number, default 70, range 30–160 |
| `constellation.memory` | How long the lines last (seconds) | Number, default 8, range 2–20 |
| `galaxy.arms` | Arms | Number, default 3, range 2–6 |
| `galaxy.stars` | Stars | Number, default 2000, range 500–4000 |
| `galaxy.spin` | How far it turns | Number, default 1, range 0–2.5 |
| `kaleidoscope.mirrors` | Mirrors | Number, default 6, range 3–12 |
| `kaleidoscope.shards` | Shards | Number, default 6, range 3–10 |
| `kaleidoscope.turn` | Turn and change mirrors with the music | Toggle, default 1 |
| `murmuration.birds` | Birds | Number, default 700, range 200–1500 |
| `murmuration.tightness` | How tight the flock keeps | Number, default 1, range 0.3–2 |
| `murmuration.sky` | Dusk sky (else glowing birds in the dark) | Toggle, default 1 |
| `ink.drops` | Drop size | Number, default 1, range 0.5–2 |
| `ink.current` | Current | Number, default 1, range 0–2.5 |
| `ink.fade` | How long the ink lasts | Number, default 0.985, range 0.9–0.997 |
| `skyline.buildings` | Towers | Number, default 24, range 10–48 |
| `skyline.lights` | Searchlights | Toggle, default 1 |
| `skyline.windows` | Lit windows | Toggle, default 1 |
| `mandala.petals` | Petals | Number, default 8, range 4–16 |
| `mandala.layers` | Rings | Number, default 3, range 1–5 |
| `mandala.step` | Change the petals each bar | Toggle, default 1 |
| `clones.clones` | Clones kept | Number, default 6, range 3–12 |
| `clones.drift` | How far they drift | Number, default 1, range 0–2 |
| `timewarp.depth` | Frames back | Number, default 30, range 10–60 |
| `timewarp.direction` | Time runs | Choice, default 0, choices: 0=Down the picture / 1=Across it / 2=Out from the middle |
| `timewarp.tint` | Colour the past | Number, default 0.35, range 0–1 |
| `aura.glow` | Glow | Number, default 1, range 0.3–2 |
| `aura.rings` | Shockwaves on the kick | Toggle, default 1 |
| `aura.heat` | Draw me as heat (else a dark figure) | Toggle, default 1 |
| `ledwall.tiles` | Lights across | Number, default 48, range 16–96 |
| `ledwall.gate` | How much the spectrum gates the lights | Number, default 0.65, range 0–1 |
| `ledwall.colour` | Colours | Choice, default 0, choices: 0=By brightness / 1=Across the wall |
| `stardust.particles` | Motes | Number, default 2500, range 500–5000 |
| `stardust.blast` | How hard the kick blasts them | Number, default 1, range 0.3–2 |
| `stardust.trails` | Trails | Number, default 0.55, range 0–0.9 |
| `hallofmirrors.mirrors` | Mirrors | Number, default 6, range 2–12 |
| `hallofmirrors.zoom` | How far the bass draws it in | Number, default 1, range 0–2 |
| `hallofmirrors.turn` | Turn and change mirrors with the music | Toggle, default 1 |
| `hallofmirrors.colour` | Colours | Choice, default 0, choices: 0=The palette / 1=The camera's own |
| `lightpainting.brush` | Brush size | Number, default 1, range 0.4–3 |
| `lightpainting.fade` | How long the strokes last | Number, default 0.94, range 0.8–0.985 |
| `lightpainting.body` | Show my body faintly | Toggle, default 1 |
| `scanner.direction` | Sweeps | Choice, default 0, choices: 0=Down / 1=Across |
| `scanner.width` | Bar width | Number, default 1, range 0.3–3 |
| `scanner.hold` | How long the trace holds | Number, default 0.93, range 0.8–0.985 |
| `scanner.colour` | Colours | Choice, default 0, choices: 0=The palette / 1=The camera's own |
| `shatter.tiles` | Tiles across | Number, default 14, range 6–28 |
| `shatter.blast` | How hard the kick blasts them | Number, default 1, range 0.3–2 |
| `shatter.spin` | Tiles spin | Toggle, default 1 |
| `shatter.colour` | Colours | Choice, default 0, choices: 0=The palette / 1=The camera's own |
| `spotlight.lights` | Lights | Number, default 3, range 1–5 |
| `spotlight.beam` | Beam width | Number, default 1, range 0.4–2.5 |
| `spotlight.haze` | Haze | Number, default 1, range 0–2 |
| `spotlight.colour` | Colours | Choice, default 0, choices: 0=The palette / 1=The camera's own |
| `infinitymirror.levels` | Levels | Number, default 5, range 2–9 |
| `infinitymirror.shrink` | How much smaller each level is | Number, default 0.72, range 0.5–0.9 |
| `infinitymirror.turn` | Turn a step on each beat | Toggle, default 1 |
| `infinitymirror.colour` | Colours | Choice, default 0, choices: 0=The palette / 1=The camera's own |
| `water.depth` | How deeply the ripples bend the picture | Number, default 1, range 0.2–3 |
| `water.lifetime` | Ripple lifetime | Number, default 2, range 0.5–5 |
| `water.shimmer` | Shimmer on the hi-hats | Toggle, default 1 |
| `water.colour` | Colours | Choice, default 0, choices: 0=The palette / 1=The camera's own |

## Remaining acceptance outside this inventory

- [ ] Prove actual mode-switch behavior across compatible/incompatible settings (task #482.2).
- [ ] Validate analysis threading, memory/CPU/cancellation bounds, and pause/seek/device/resize/minimize/shutdown transitions (tasks #482.3, #482.4, #482.6).
- [ ] Verify WinUI keyboard and assistive-technology access and photograph actual high-DPI/high-contrast UI states (tasks #482.5, #482.7, #482.8).
- [ ] Complete native/pure/accessibility/screenshot/performance/current-head CI gates before merging this follow-up (tasks #482.9, #482.10).

**Do not mistake this source inventory for hardware/visual acceptance.**
