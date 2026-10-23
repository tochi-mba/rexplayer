# Testing

A test here proves a feature from what came out, not from the absence of an exception.

## Oracles

| Oracle | How it works | Used for |
|---|---|---|
| Exact samples | A ramp fixture stores each sample's own index; whatever reaches the recording sink must be the right indices in order | Playback, precise seeks, start times, trimming |
| Spectral | A sine fixture plus a Goertzel measurement reads a tone's amplitude at one frequency | Resampling, volume, decoding, aliasing |
| Events | The session's event stream is recorded and waited on | States, seeks, failures, end of stream |
| Independent read-back | A capture file written by the WAV sink is read back by the demuxer and decoder | Sinks and sample conversion |
| Machine contract | The published `rexplay.exe` must print one JSON line with `protocolVersion: 1` | The command line |

## Suites

| Suite | Runs where | Command |
|---|---|---|
| `Rex.Media.Tests` | Every push, Windows and Linux | `./dev.ps1 test` |
| Coverage gate | Every push | `./dev.ps1 gate` |
| `Rex.Media.Windows.Tests` | Every push, Windows | `./dev.ps1 adapters` |
| Performance (the `Perf` trait in `Rex.Media.Windows.Tests`) | Every push, Windows, as its own job | `./dev.ps1 perf` |
| `Rex.Media.App.Tests`: the real window through UI Automation | Every push, Windows, as its own job | `./dev.ps1 build` then `./dev.ps1 ui` |
| Fuzzing (`tests/Rex.Fuzz`) | Nightly, Linux (`fuzz.yml`); the corpus it keeps is replayed by `Rex.Media.Tests` on every push | Actions, "Fuzz", run workflow |
| Command-line smoke | Every push, against the packaged build | `./dev.ps1 smoke` |
| Site | Every push | `./dev.ps1 site`, `node --test tests/site/app.test.mjs`, `npx playwright test` |

The window suite starts rexplayer.exe with `REXPLAYER_FAKE_AUDIO=1` (no sound card needed) and, for
the caption wait, `REXPLAYER_FAKE_SPEECH=<seconds>`: a stand-in speech model that takes that long
over each piece and hears one word, so the wait, its overlay and its release run on machines with
no model downloaded.

## Coverage

Every file in `tests/coverage-required.txt` keeps 100 % line coverage; the gate fails otherwise.
Branch coverage is reported by the gate but not enforced, because switch expressions compile to
branches no input can reach ([ADR-0018](adr/0018-test-projects-and-the-coverage-gate.md)).

## Performance

`./dev.ps1 perf` measures our own decoders and the resampler (× real time) and the demuxers (MB/s),
each as its fastest pass after a warm-up, because noise only ever slows a pass down. The
resampler gets a longer warm-up to let the .NET tiered JIT finish optimizing its convolution
on slower CI machines; the committed throughput floor is unchanged. A measure may
fall to 0.7 of its baseline in `tests/perf-baselines.json` before the run fails. Baselines are kept
per lane: CI's runners are `ci-windows`, and a developer's machine is `local-<name>`. A lane with no
baseline reports and passes, because a working machine is too noisy to gate on. Every run writes its
measurements to `artifacts/perf/<lane>.json` (CI keeps them as the `perf-measurements` artefact).
`REXPLAYER_WRITE_PERF=1` makes a run's measurements the lane's new baselines; commit them with the
reason they moved.

Allocations are exact rather than relative: once warm, demuxing and decoding with our own code
allocate nothing at all, on every machine.

The suite also measures what people feel, part by part at full HD (`ExperiencePerfTests`): each
software visualisation's frame, each picture effect's time on the graphics card (Direct3D 11
timestamp queries), the conversion of a decoded picture to BGRA for Subject Lock, Subject Lock's
tracking, and scanning and reopening a library of 20 000 files. Each visualisation and effect is
reported on its own (`visual-1080p-ms-*`, `effect-1080p-gpu-ms-*`) and never gated; their totals
are. A cost (lower is better) may grow to its baseline divided by 0.7 before the run fails.

A new measure has no `ci-windows` baseline until one is recorded from CI itself: download the
`perf-measurements` artefact of a green run on `main` and copy its numbers into
`tests/perf-baselines.json`, saying in the commit where they came from. Numbers from a developer's
machine never become CI's baselines.

## Screenshots of the window

The window suite also compares how the window looks with committed pictures, through
`Screenshots.Match(app, "name")` (the whole window) or `Screenshots.Match(app, "name", "AutomationId")`
(one control). A pixel counts as changed when a colour channel moves by more than 40 of 255, and a shot
fails when more than 0.5 % of its pixels change, so antialiasing and graphics-driver noise never fail it.
Baselines are kept per lane in `tests/screenshot-baselines/<lane>/`: CI's job sets
`REXPLAYER_UI_LANE=ci-windows`, and any other machine only records. Every run writes its shots to
`artifacts/screenshots/<lane>/actual/` and, for a failed one, a diff to `.../diff/`; CI keeps both as
the `screenshots` artefact. A shot with no baseline passes and says so: copy CI's picture from that
artefact into `tests/screenshot-baselines/ci-windows/` to start comparing it, and do the same, with
the reason in the commit, when a change of look is meant. Pictures from a developer's machine never
become baselines. `REXPLAYER_WRITE_SCREENSHOTS=1` cannot silently overwrite
an image: an existing declared `ci-windows` baseline also requires
`REXPLAYER_VISUAL_APPROVAL_CONFIRMED=1` after human inspection. A new
`unreviewed` scene must be inspected, promoted in the inventory and
reviewed in a commit before an automated writer can replace its baseline.
Screenshots are for layout; behaviour is asserted through UI Automation.

Every capture must be declared in `tests/screenshot-scenes.json` with its offline fixture,
route, state, intended dimensions and theme, automation IDs, observable text, readiness signal
and owning test. `Screenshots.Match` requires that real UI Automation state before taking pixels,
and writes a companion `actual/<scene-id>.json` describing the scene, crop, captured dimensions,
CI commit and baseline presence. The image preflight still rejects blank or nearly uniform frames.
The `unreviewed` inventory status means the capture is record-only, not visually approved.
`ci-windows` means a committed baseline exists, not that all human visual review gates passed.
All 60 declared scene dimensions are now explicit host-window sizes. The capture
path checks each pinned UI Automation viewport before accepting evidence. A
pinned dimension is not itself a reviewed PNG or proof of matching layout.
The scene contract also declares `captureMode`: `window` uses PrintWindow for the host HWND,
whereas `desktop` uses the actual on-screen desktop composition. Most scenes use `desktop`, because CI evidence showed PrintWindow could silently omit both
floating popups and full window content, even after UIA reports it ready. The two existing
letterboxed-card baselines retain `window` mode at their original pinned
1280x800 host viewport; their PrintWindow crops were committed in that lane
and must not silently change capture mode. PrintWindow does not require an
unobstructed onscreen desktop, so its original viewport may exceed the
visible desktop. The strict virtual-desktop clipping check applies only to
`desktop` mode.
Popup and Preferences scenes require visible variation in the UIA control witness;
ordinary library and playback scenes instead require semantic UIA readiness and a
nonuniform rendered image, since an individual icon can legitimately be mostly uniform.
The harness positions isolated test windows on the visible desktop before opening dialogs
and rejects cropped/offscreen desktop evidence. Each scene writes an actual PNG, a JSON
sidecar recording both semantic and pixel checks, and the app log. CI also keeps the
full scene inventory and per-test process logs. Run these tests on a visible,
unobstructed, serial Windows desktop; never approve desktop captures made with other
windows covering the app. The capture mode is recorded in each JSON sidecar.

The faulted-media scene opens a four-byte malformed local WAV and verifies the
actual failure message is rendered. The screenshot-only
`REXPLAYER_VISUAL_EVIDENCE=1` environment variable extends that notice to
30 seconds while leaving the normal 1.5-second behavior unchanged. The
caption-loading scene uses a slow offline fake-speech fixture; no network,
external speech model or fabricated PNG is involved.

Every successful scene takes a second compositor frame and checks the differing-pixel
ratio against its declared tolerance. CI retains `repeat/<scene-id>.png` and, on
instability, `repeat-diff/<scene-id>.png`. It then reruns the seven fixture-backed
empty, single, mixed, RTL, long-text and missing-artwork captures in new app
processes, under `ci-windows-repeat`, and checks their resulting PNG pixels with
`scripts/check-screenshot-repeatability.ps1`. These checks measure stability;
neither proves that a screenshot actually shows the intended interface.

CI saves `review/index.html` alongside images, the full scene inventory, JSON
UIA witnesses, process logs, actual PNGs and retained baselines/diffs. Open the
gallery in a local browser to inspect every image and its semantic assertions
before promoting it to a reviewed baseline. Missing files or pending approval
must remain visible, not counted as success.

Two specialized contracts require real, independently configured Windows desktops:
run the `LibraryAccessibilityVisualTests` with
`REXPLAYER_UI_LANE=ci-windows-high-dpi` on a monitor reporting at least 144 DPI,
or with `REXPLAYER_UI_LANE=ci-windows-high-contrast` under real Windows high
contrast. Each test rejects a falsely configured lane, and ordinary CI skips
these specialist tests. The regular `ci-windows` artifact does not supply their
proof. The image JSON records the actual OS DPI and contrast state.

## Measuring the whole player

`./dev.ps1 measure` publishes the player as people get it and measures it with
`scripts/measure-startup.ps1 -All`, then runs the performance suite, and writes the tables to
`artifacts/measure/normal.md`. Each figure is the median and the worst of several runs, with how
busy the rest of the computer was just before each run, so a measurement taken while other work ran
can be told apart. Timed launch, open, play and seek runs include sampled peak working set and
private memory; driven runs report those process-lifetime peaks alongside current working set,
private memory and managed heap. It needs FFmpeg, which makes the media it plays once (H.264 at full HD and 4K,
10-bit HEVC, VP9, MP3, FLAC and a picture) into `artifacts/measure/media`.

| Scenario | What it measures |
|---|---|
| launch, cold | Start to first paint and to interactive, with the program in memory and read from the disk |
| play | Start to the first picture and the first sound for each kind of media |
| seek, seekfar | A short jump forward, and a seek four fifths of the way through, to the picture and the sound |
| steady | Playing full HD and 4K for 15 seconds: processor, pictures dropped, the jitter of the pictures' timing, memory, allocations and how long the window's thread was blocked |
| music | The same while a software visualisation draws, and its frame cost |
| ui | Opening the library, Preferences, the Subject Lock panel, the picture effect picker and the effects panel |
| library | Scanning a generated library of 3 000 files and opening the library on it |

`./dev.ps1 measure -LowEnd` runs all of it on an imitation of a weak computer: two processor
cores, below-normal priority, a cap of 2 GB on each process's memory (a job object), pictures drawn
in software (`REXPLAYER_GRAPHICS=software`) and no decoding on the graphics card
(`REXPLAYER_HARDWARE_DECODING=off`). The player's own device profile sees the same limits, so the
imitation also shows what the Fastest tier does. Its report is `artifacts/measure/low-end.md`.
## Rules

- No wall-clock waits in the pure suite: clocks are `ManualTimeProvider`, and blocking points are
  held by gates the test controls.
- No retries. A flaky test is a bug in the test or in the product.
- Tests that prove a capability carry `[Capability("ID")]`, which proves the row on every
  platform a portable test runs on and only on its own platform from a platform project.
  `docs/parity.md` lists the rows where the platforms differ.
