# Working on rexplayer

Instructions for anyone, human or agent, changing this repository. The rules here are enforced by
tests where a test can enforce them; the rest are enforced in review.

## Rules that are never broken

1. **The name rule.** This repository describes rexplayer only in its own terms. It never names other
   media players or their organisations: not in code, comments, tests, fixture names, docs, the site,
   commit messages, pull requests or release notes. The feature list is rexplayer's own
   [capability matrix](docs/capability-matrix.md). `RepositoryTests.NoTrackedTextNamesTheBannedProducts`
   checks every tracked file, and CI checks commit and pull-request text.
2. **Clean room.** Implementations come from published specifications, format documentation,
   papers and observed behaviour only. Never read the source code of other media players or
   media frameworks. Every file in `Containers`, `Codecs.Software`, `Subtitles`, `Net`, `Cast` and
   `Discs` starts with a `// Spec:` line naming the document and clause it implements.
3. **No third-party runtime code.** The product depends on the .NET base library and the Windows App
   SDK only; CsWin32 generates bindings at compile time. Test and build tools are listed in ADR-002.
4. **Patents.** rexplayer's own decoders exist only for codecs whose patents have expired or that are
   royalty-free (docs/adr/0009). Patented codecs decode through Windows Media Foundation.
5. **Every pure line is tested.** Every file in `tests/coverage-required.txt` keeps 100 % line
   coverage; a new pure file is added to the list in the same change.
6. **Long suites run in the background.** Locally, run `./dev.ps1 test` (the pure suite); leave the
   full and UI suites to CI or run them in the background.
7. **Commits.** Plain imperative English, no conventional prefixes, no co-author trailers. One change
   per pull request, rebase-merged. A release is a change of `Version` in `Directory.Build.props`
   with a matching CHANGELOG heading, committed as "Version X.Y.Z so the release publishes".

## Machine interface

`rexplay` is the stable surface for scripts and agents.

- `rexplay agent capabilities` prints one JSON line describing the build: version, commands,
  options, container formats and decoders.
- Any command with `--json` prints exactly one JSON document and never prompts:
  `{"ok":true,"protocolVersion":1,"command":"<name>","data":{...}}` or
  `{"ok":false,"protocolVersion":1,"command":"<name>","error":{"type":"...","message":"..."}}`.
- Exit codes: 0 success, 1 failure.
- `rexplay play <file> --aout wav:<path>` plays headless as fast as possible into a capture file,
  which is how CI proves the engine on machines with no audio device.
- `rexplay snapshot <file> --at <time> --out <png> --json` saves the exact picture shown at a
  moment, decoded by Windows for H.264 and HEVC: the quickest way to see what a video holds.

The protocol version changes only when a field's meaning changes or a field disappears.

## Layout

| Path | What it holds |
|---|---|
| `src/Rex.Media.Primitives` | Time, rationals, bit reading, CRCs, pooled buffers, the media model |
| `src/Rex.Media.Diagnostics` | The rotating log and exception summaries |
| `src/Rex.Media.IO` | Byte sources (file, memory, slice) and the parsing cursor |
| `src/Rex.Media.Containers` | Format probing and demuxers |
| `src/Rex.Media.Codecs` | Decoder contracts and the decode ladder |
| `src/Rex.Media.Codecs.Software` | rexplayer's own decoders |
| `src/Rex.Media.Video` | Picture formats' colour maths (H.273), letterboxing, the PNG writer, the presenter contract |
| `src/Rex.Media.Video.D3D11` | The Direct3D 11 presenter: GPU colour conversion into a window or offscreen |
| `src/Rex.Media.Codecs.MediaFoundation` | The adapter to Windows' decoders (AAC, AC-3, E-AC-3; video next) |
| `src/Rex.Media.Audio` | Resampler, mixer, volume, sinks, device-format policy |
| `src/Rex.Media.Audio.Wasapi` | The Windows audio adapter |
| `src/Rex.Media.Engine` | The media session: mailbox, state machine, clocks, pipeline threads |
| `src/Rex.Media.AppCore` | The command line and everything the app shares with it |
| `src/Rex.Media.Interop` | CsWin32 bindings and the only `unsafe` code |
| `src/Rex.Media.Cli` | `rexplay.exe`, the composition root of the command line |
| `tests/Rex.Media.Tests` | The pure suite: unit, engine and repository tests |
| `tests/Rex.Fuzz` | The libFuzzer entry point for the parsers in `tests/Rex.Media.TestKit/FuzzTargets.cs` (nightly `fuzz.yml`) |
| `tests/Rex.Media.Windows.Tests` | The adapter suite: Windows components driven for real, checked against independent decoders |
| `tests/Rex.Media.TestKit` | Builders, signals, fakes and the `[Capability]` attribute |
| `site/` | The website, checked by `scripts/check_site.py` and `tests/site` |
| `installer/` | The Inno Setup installer and its build script |
| `docs/` | Architecture, decisions, the capability matrix, testing and fixtures |

Data lives in `%LocalAppData%\REX\rexplayer`; the installer puts the programs in
`%LocalAppData%\Programs\rexplayer`.

## Invariants

- A media session changes state only on its mailbox thread, and only along the transitions in
  `SessionStateMachine`.
- Media data never crosses threads except through `BoundedQueue`, which hands over ownership; a seek
  starts a new generation and everything older is disposed unread.
- Every blocking wait on the audio side uses the generation's cancellation token, so a seek or a stop
  interrupts it at once.
- Parsers throw `MediaFormatException` for bad input and nothing else.
- Native calls live only in `Rex.Media.Interop` (`RepositoryTests.NativeCallsLiveOnlyInInterop`).
- Adapters (WASAPI, Media Foundation, Direct3D) make no decisions; the policy they follow lives in a
  pure, tested class.

## Testing

| Suite | Command | What it proves |
|---|---|---|
| Pure | `./dev.ps1 test` then `./dev.ps1 gate` | Every engine decision, exact samples, the repository rules |
| Adapters | `./dev.ps1 adapters` | Windows' own components (Media Foundation decoders) give what an independent decoder gives |
| Performance | `./dev.ps1 perf` | Throughput holds against `tests/perf-baselines.json`; steady-state playback allocates nothing |
| Command line | `./dev.ps1 smoke` | The published `rexplay.exe` keeps its JSON contract and plays a fixture exactly |
| Site | `./dev.ps1 site`, `node --test tests/site/app.test.mjs`, `npx playwright test` | The site's links, assets, accessibility and behaviour |

Tests that prove a row of the capability matrix carry `[Capability("ID")]`; a row may be marked
verified only while such a test exists.

## Generated files

`docs/capability-matrix.md` is generated from `docs/capability-matrix.json`. Never edit it by hand:
change the JSON, then run the tests with `REXPLAYER_WRITE_DOCS=1`.
