# 2026-10-05: playback to the Windows default device

Capabilities proved: AU-01

Machine: Windows 11 Pro 26200, built-in audio, shared-mode mix format 48 kHz float stereo.

Run: `rexplay play tone.wav --volume 10` with a 2-second, 44.1 kHz, 16-bit stereo 440 Hz tone, then
`rexplay play tone.wav --volume 10 --start 1.5 --json`.

Observed: the tone was heard for its full length and stopped cleanly. The JSON run reported 24 000
samples played at 48 kHz for the 0.5 seconds after the start time, confirming the 44.1 to 48 kHz
resample, and `finished: true`.
