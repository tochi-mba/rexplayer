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
| Fuzzing (`tests/Rex.Fuzz`) | Nightly, Linux (`fuzz.yml`); the corpus it keeps is replayed by `Rex.Media.Tests` on every push | Actions, "Fuzz", run workflow |
| Command-line smoke | Every push, against the packaged build | `./dev.ps1 smoke` |
| Site | Every push | `./dev.ps1 site`, `node --test tests/site/app.test.mjs`, `npx playwright test` |

## Coverage

Every file in `tests/coverage-required.txt` keeps 100 % line coverage; the gate fails otherwise.
Branch coverage is reported by the gate but not enforced, because switch expressions compile to
branches no input can reach ([ADR-0018](adr/0018-test-projects-and-the-coverage-gate.md)).

## Rules

- No wall-clock waits in the pure suite: clocks are `ManualTimeProvider`, and blocking points are
  held by gates the test controls.
- No retries. A flaky test is a bug in the test or in the product.
- Tests that prove a capability carry `[Capability("ID")]`.
