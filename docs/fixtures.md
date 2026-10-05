# Fixtures

Every media file the tests use is either built in code by the test kit or committed here with a
description of how it was made. No downloaded media is used.

## Built in code

| Builder | Makes |
|---|---|
| `WavBuilder` | WAV files of any format tag, RIFF/RIFX/RF64, extensible formats, LIST/INFO tags, unfinished recordings |
| `AiffBuilder` | AIFF and AIFF-C files with any compression type and text chunks |
| `Pcm.SineWav` | A sine wave as 16-bit WAV, for spectral checks |
| `Pcm.RampWav` | A WAV whose samples count up, so any sample's position can be checked exactly |

## Committed

| File | Contents | How it was made |
|---|---|---|
| `tests/fixtures/smoke/tone.wav` | 1 second, 440 Hz sine at a quarter of full scale, 8 kHz, mono, 16-bit | Written sample by sample by a PowerShell loop: `round(0.25 * 32767 * sin(2π · 440 · i / 8000))` |
