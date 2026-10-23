# Visualizer option and persistence inventory

Source snapshot: `VisualizerChoice` from `src/Rex.Media.Settings/PlayerSettings.cs`, option declarations in `src/Rex.Media.AppCore/Player/VisualizerOptions.cs`, and scene registrations in `src/Rex.Media.AppCore/Visuals/VisualScene.cs`. Generated from code for PR #488 on 2026-10-23, not from screenshots or assumed behavior. **This is an implementation inventory, not a native UX verification.**

## Shared settings and persistence rule

- All 42 modes, including Off, define `colors` (Choice, default palette below), `color` (Color, default decimal 16726832 / hexadecimal 0xFF3B30, RGB readers mask to 24 bits), and `sensitivity` (Number, default 1, range 0.25 to 4).
- Raster-backed modes (marked **yes** below) additionally define `meter` (Toggle, default 0).
- Persist each option under `<lowercase mode name>.<option key>` in `PlayerSettings.VisualOptions` (e.g. `vinyl.speed`). A mode's reset removes only keys with that mode's prefix.
- Unset values read as the defaults below. Number values clamp to their declared ranges; invalid choices fall back to the default; invalid, nonfinite or unreadable stored text falls back to its declared default.
- When a visualizer is switched, its independently stored options remain available; changing mode must not rewrite another mode's settings.

## Mode-specific inventory

Each `key:Kind(default)` below has a persisted key formed by prefixing the lowercase mode name and a dot. A dash in the last column means no extra option beyond the shared settings (and conditional meter).

| Mode | Palette default | Raster scene / meter | Mode-specific setting keys, kinds, defaults |
| --- | --- | --- | --- |
| Off | Accent | no | - |
| Spectrum | Accent | no | `bars:Number(48)`, `peaks:Toggle(1)`, `mirrored:Toggle(0)` |
| Oscilloscope | Accent | no | `thickness:Number(2)` |
| Meters | Accent | no | - |
| Spectrogram | Accent | no | - |
| Vinyl | Sunset | yes | `speed:Choice(0)`, `depth:Number(1)`, `arm:Toggle(1)`, `label:Toggle(1)`, `sheen:Toggle(1)`, `wave:Toggle(1)`, `strobe:Toggle(1)`, `glow:Number(0.7)` |
| Halo | Neon | yes | `rays:Number(48)`, `thickness:Number(4)`, `spin:Toggle(0)`, `trails:Number(0.72)`, `burst:Toggle(1)` |
| Mirror | Pitch | yes | `columns:Number(96)`, `thickness:Number(2)`, `trails:Number(0.7)`, `stereo:Toggle(1)` |
| Aurora | Aurora | yes | `curtains:Number(3)`, `height:Number(1)`, `drift:Number(1)`, `stars:Toggle(1)` |
| Embers | Fire | yes | `amount:Number(90)`, `speed:Number(1)`, `wind:Number(0.3)`, `smoke:Number(0.6)`, `burst:Toggle(1)` |
| Ripples | Ocean | yes | `thickness:Number(2.5)`, `rings:Number(3)`, `lifetime:Number(1.5)`, `where:Choice(0)`, `drops:Choice(0)` |
| Strobe | Pitch | yes | `flashes:Number(3)`, `fade:Number(0.4)`, `pattern:Choice(0)`, `step:Choice(0)`, `glow:Number(0.25)` |
| Silhouette | Neon | yes | `style:Choice(0)`, `mirror:Toggle(1)`, `threshold:Number(0.09)`, `background:Choice(0)` |
| BeatEdit | Neon | yes | `style:Choice(0)`, `source:Choice(0)`, `follow:Toggle(1)`, `intensity:Number(1)`, `cuts:Choice(1)`, `mirror:Toggle(1)`, `flash:Toggle(1)`, `flashes:Number(3)`, `grain:Toggle(1)`, `bars:Toggle(0)` |
| Resonance | Ocean | yes | `density:Number(7)`, `depth:Number(1.1)`, `flow:Number(0.8)`, `contrast:Number(1.15)`, `trails:Number(0.45)`, `mirror:Toggle(1)`, `rings:Toggle(1)` |
| Groove | Neon | yes | `seconds:Number(4)`, `spectrum:Toggle(1)`, `grid:Toggle(1)`, `lanes:Toggle(1)` |
| Flight | Sunset | yes | `ahead:Number(8)`, `trails:Number(0.55)`, `spectrum:Toggle(1)`, `stars:Toggle(1)` |
| Cymatics | Ocean | yes | `detail:Number(1)`, `plate:Choice(0)`, `glow:Number(1)` |
| Ridgelines | Aurora | yes | `lines:Number(36)`, `height:Number(1)` |
| Synthwave | Sunset | yes | `speed:Number(2)`, `sun:Toggle(1)`, `mountains:Toggle(1)` |
| Fireworks | Rainbow | yes | `gravity:Number(1)`, `trails:Number(0.75)` |
| Lightning | Cool | yes | `branches:Number(3)`, `flash:Number(0.6)`, `rain:Toggle(1)` |
| Lava | Fire | yes | `blobs:Number(7)`, `glow:Number(1)`, `merge:Toggle(1)` |
| Constellation | Cool | yes | `stars:Number(70)`, `memory:Number(8)` |
| Galaxy | Aurora | yes | `arms:Number(3)`, `stars:Number(2000)`, `spin:Number(1)` |
| Kaleidoscope | Rainbow | yes | `mirrors:Number(6)`, `shards:Number(6)`, `turn:Toggle(1)` |
| Murmuration | Sunset | yes | `birds:Number(700)`, `tightness:Number(1)`, `sky:Toggle(1)` |
| Ink | Neon | yes | `drops:Number(1)`, `current:Number(1)`, `fade:Number(0.985)` |
| Skyline | Neon | yes | `buildings:Number(24)`, `lights:Toggle(1)`, `windows:Toggle(1)` |
| Mandala | Sunset | yes | `petals:Number(8)`, `layers:Number(3)`, `step:Toggle(1)` |
| Clones | Neon | yes | `clones:Number(6)`, `drift:Number(1)` |
| TimeWarp | Aurora | yes | `depth:Number(30)`, `direction:Choice(0)`, `tint:Number(0.35)` |
| Aura | Fire | yes | `glow:Number(1)`, `rings:Toggle(1)`, `heat:Toggle(1)` |
| LedWall | Rainbow | yes | `tiles:Number(48)`, `gate:Number(0.65)`, `colour:Choice(0)` |
| Stardust | Ocean | yes | `particles:Number(2500)`, `blast:Number(1)`, `trails:Number(0.55)` |
| HallOfMirrors | Neon | yes | `mirrors:Number(6)`, `zoom:Number(1)`, `turn:Toggle(1)`, `colour:Choice(0)` |
| LightPainting | Rainbow | yes | `brush:Number(1)`, `fade:Number(0.94)`, `body:Toggle(1)` |
| Scanner | Cool | yes | `direction:Choice(0)`, `width:Number(1)`, `hold:Number(0.93)`, `colour:Choice(0)` |
| Shatter | Fire | yes | `tiles:Number(14)`, `blast:Number(1)`, `spin:Toggle(1)`, `colour:Choice(0)` |
| Spotlight | Sunset | yes | `lights:Number(3)`, `beam:Number(1)`, `haze:Number(1)`, `colour:Choice(0)` |
| InfinityMirror | Aurora | yes | `levels:Number(5)`, `shrink:Number(0.72)`, `turn:Toggle(1)`, `colour:Choice(0)` |
| Water | Ocean | yes | `depth:Number(1)`, `lifetime:Number(2)`, `shimmer:Toggle(1)`, `colour:Choice(0)` |

This inventory intentionally does **not** assert that visualizer transitions, screen-reader controls, high contrast, performance, or camera failure cases are verified. Those remain separate tasks in `merged-pr-followup-audit.md`.
