# 2026-10-14: performance, on this computer and on an imitation of a weak one

Capabilities proved: none new; this records the measurements behind the device tiers and the speed
work, taken with `scripts/measure-startup.ps1` (`./dev.ps1 measure`) and the performance suite.

Machine: Intel Core i7-10510U (4 cores, 8 threads), 32 GB, Intel UHD Graphics, Windows 11 Pro
26200, on mains power.

Conditions: other builds and test runs were going on throughout, and the processor was held at its
lowest clock (801 MHz, 34 % of its maximum) the whole time, so every figure is far slower than the
same computer when quiet, and noisy. To compare fairly the build before this work and the build
after it were measured turn about, scenario by scenario, so both saw the same load; each scenario
records how busy the rest of the computer was just before it ran. Read the changes, not the
absolute numbers, and treat a change of less than about 15 % in a timing as noise.

Before is `main` at 196f56a with only the measuring added (the player's figures over the
automation pipe and the frame cadence, which change nothing it does); after is this branch.

## The parts, measured in one process (before and after, the same load)

A benchmark built twice, once against each build's assemblies, run three times turn about; the
medians:

| Measure | Before | After |
|---|---:|---:|
| Software decoding of full-HD H.264 through Windows, bytes allocated per picture | 3 157 019 | 193 |
| The same for 10-bit HEVC | 6 313 925 | 192 |
| Collections of the youngest generation while decoding 200 H.264 pictures | 30 | 1 |
| The same for 200 10-bit HEVC pictures | 42 | 0 |
| Bytes allocated per Direct3D draw of a picture | 209 | 9 |
| Time the logging thread spends on a log line | 631 us | 1.1 us |
| Converting a decoded full-HD picture to BGRA for Subject Lock | 154 ms | 116 ms |
| The same at half size (the Fastest tier) | | 40 ms |

## The whole player on this computer

FILL-NORMAL

## The whole player on the imitation of a weak computer

Two processor cores (not hyperthreads of each other), below-normal priority, at most 2 GB of
memory for each process (a job object), pictures drawn in software (Direct3D's software
rasteriser) and no decoding on the graphics card. The player's device profile sees the same
limits and chooses Fastest by itself, so the after column shows the tiers at work.

FILL-LOWEND

## The performance suite

FILL-PERF
