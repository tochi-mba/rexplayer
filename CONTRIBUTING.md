# Contributing

Read [AGENTS.md](AGENTS.md) first: it holds the rules every change follows.

## Build and test

You need the .NET 10 SDK, Python 3, Node 22 and, to build the installer, Inno Setup 6.

```powershell
./dev.ps1 build    # warnings are errors
./dev.ps1 test     # the pure suite, with coverage
./dev.ps1 gate     # every required line covered
./dev.ps1 check    # all of the above plus the formatting check: run it before you push
```

## Test layers

| Layer | Where | What it proves |
|---|---|---|
| Unit and property | `tests/Rex.Media.Tests/<Area>` | One rule per test, with signals and fixtures built in code |
| Engine | `tests/Rex.Media.Tests/Engine` | Whole sessions against a recording sink: exact samples, seeks, failures |
| Command line | `tests/Rex.Media.Tests/AppCore` and `./dev.ps1 smoke` | The commands in-process, then the published executable |
| Repository | `tests/Rex.Media.Tests/Repository` | The rules in AGENTS.md that a test can check |
| Website | `tests/site` | The checker, the page script and the real page in a browser |

## Checklists

**Adding a format reader or decoder**

1. Start the file with `// Spec:` naming the document and clause.
2. Build fixtures in code with `WavBuilder`-style builders, or describe a committed fixture in
   `docs/fixtures.md`.
3. Register it in `MediaRegistries`.
4. Test every header field, every error path and a round trip through the engine.
5. Add its files to `tests/coverage-required.txt`, and its row status in
   `docs/capability-matrix.json` with a `[Capability]` test.

**Adding a command-line option**

1. Add it to `CliReference` so help, capabilities and the docs list it.
2. Parse it in `CliApplication` with a plain-words error for bad values.
3. Test the good path, every bad value, and machine mode.

**Every change**

- Plain words in sentence case; an error message says what to do next.
- A CHANGELOG entry for anything a user would notice.
- No new warning suppressions without a written reason in `.editorconfig`.

## Shipping

One pull request per change, branched from an up-to-date `main`, rebase-merged. CI must be green;
a red job is fixed, never retried blindly. To release, change `Version` in `Directory.Build.props`,
add the matching CHANGELOG heading, and commit "Version X.Y.Z so the release publishes". The
pipeline publishes the installer, the portable zip and their checksums. Published releases are never
replaced.
