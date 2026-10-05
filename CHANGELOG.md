# Changelog

Every version of rexplayer that reaches users, newest first. A version is published by the CI
pipeline the moment its number reaches `main`.

## 0.1.0 - 2026-10-05

### Added

- **The engine.** A media session with a single-threaded command mailbox, a ten-state life cycle,
  sample-accurate seeking that interrupts whatever the pipeline was doing, pause, resume and stop.
- **WAV, RF64 and AIFF.** Every PCM storage format, big- and little-endian, plus G.711 A-law and
  µ-law, with tags read from LIST/INFO and AIFF text chunks.
- **Windows audio.** Playback to the default device through WASAPI, with rexplayer's own resampler
  and channel mixer converting to whatever the device mixes at.
- **The command line.** `rexplay play`, `probe`, `version`, `help` and `agent capabilities`, with
  one-line JSON output for scripts and agents.
