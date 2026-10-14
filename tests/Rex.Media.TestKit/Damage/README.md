# Damaged-file corpus

Builders here take a healthy file made by the TestKit builders (`HealthyMedia`) and damage it the way
real files are damaged (`DamageCorpus`, `StructuralDamage`): truncated downloads, flipped bits, zeroed
or garbage runs, junk in front, two files run together, the wrong extension, lost headers, broken boxes,
elements, pages, blocks and frames. Every case carries the least share of the healthy file it should
still play.

`DamageRunner` reads a file on one thread (demuxer and rexplayer's own decoders, pictures checked by
`CheckedVideoDecoderFactory`) with exact allocation counts and a deadline; `DamagedSession` in the pure
suite plays it through a whole media session. `DamagedFileTests` asserts that the player never crashes,
never hangs, keeps its memory bounded and salvages at least each case's share.

- `REXPLAYER_DAMAGE_LONG=1` runs 60-second files with twelve seeds instead of 8-second files with one.
- `REXPLAYER_DAMAGE_REPORT=<file>` appends the results table to that file.
- `REXPLAYER_DAMAGE_MEDIA=<folder>` (Windows adapter suite) damages copies of the videos in that folder,
  in a temporary folder, and plays them through Windows' own decoders. Nothing is copied into the
  repository. Hosted CI skips these external-footage cases unless the variable is provided; the
  synthetic corpus runs regardless.
