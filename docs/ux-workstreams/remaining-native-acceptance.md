# PR #488 — remaining acceptance evidence for source version 0.30.8

This document explains why some consolidated recovery tasks remain unchecked even after
their code, native screenshot fixtures, and CI workflows have been authored. The
[69-item source audit](merged-pr-followup-audit.md) and draft PR description are
the authoritative checkbox ledgers.

## Work completed in code but not accepted as a platform result

- **Windows adaptive Home:** real minimum (360), compact (720), standard (1000),
  and wide (1280) WinUI fixture scenarios; navigation expand/collapse/restoration,
  empty/recovery, pinned shelves, RTL/search/filter, system contrast and DPI
  conditional captures. Contrast uses the OS-selected foreground/background.
- **Movie/TV Browse:** native featured/specials/partial/error fixture screenshots;
  separate mouse-driven Play vs More info actions and accessible names.
- **Lyrics and visualizers:** five source-verified lyric modes, word/line seek and
  compact screenshots; high-contrast lyric weight/colors; visualizer paused,
  compact, high-DPI and system-contrast capture scenarios plus actual
  pause/seek/next/resize/minimize native test source.
- **Mac library and queue:** AppKit library search, watch/remove folder, periodic
  rescan, Play and queue, safe bounded pages; an AppKit smoke suite that requests
  native fixture captures. These are *not* finalized-release screenshots.
- **Site:** source image checker validates PNG structure, checksums and minimum
  preview dimensions. Local Playwright checks each image at phone/desktop sizes.
  The main-branch Pages workflow now stamps the deployed SHA and **after deployment**
  runs `pages.live.spec.js` against the live URL with 320/1280 screenshot
  captures. A local web-server pass cannot satisfy a deployed-pages gate.
- **Release:** `Directory.Build.props`, `CHANGELOG.md` and `README.md` all
  describe **source version 0.30.8**. A tested Python gate rejects missing,
  duplicated or stale changelog entries in CI and the Pages publication path.
  This version is *not released* merely because it exists on a draft branch.

## Native Windows checklist still needing execution/review

1. On a Windows 10/11 machine, run `./dev.ps1 build`,
   `./dev.ps1 test`, `./dev.ps1 gate`, `./dev.ps1 adapters`,
   `./dev.ps1 ui` and `./dev.ps1 perf` at the exact, then-current PR head,
   with warnings as errors. Collect logs/coverage/performance and the
   `screenshots` CI artifact. No runner outcome is inferred from authored tests.
2. Review actual `artifacts/screenshots/ci-windows/actual` and diff outputs;
   explicitly approve missing baselines. A record-only test with no approved
   baseline is **not** a completed screenshot visual gate.
3. Repeat the conditional Home and spectrum tests on **genuine** high-contrast
   Windows and a **real** monitor at 125% (120 DPI) or higher. Tests skip where
   the OS setting does not match: counting a skip as an accessibility pass is
   prohibited. Verify theme change, OS window controls, minimum width, Snap,
   scaling, high contrast and usable keyboard/screen-reader/controller/touch.
4. Verify native playback, seek, pause, track change, sound-device change,
   minimise/restore and shutdown without lingering workers or stale animation.
   Verify Explorer/taskbar thumbnails, SMTC, file associations and Jump List
   against the built installer, plus keyboard focus and live announcements.

## Native macOS and release-media checklist still needing execution/review

1. Build and launch the true `src/Rex.Media.App.Mac` app under macOS with the
   required Xcode and .NET 10 macOS workload, on the exact PR head; verify
   Intel, Apple Silicon and universal bundles through the existing CI matrix.
2. Run `MacAppSmokeTests` with the built AppKit bundle and inspect
   `artifacts/mac-library-screenshots/` and
   `artifacts/mac-queue-screenshots/` captures and their provenance.
   Those images are deliberately labeled synthetic **smoke fixtures**, not
   native release-media evidence.
3. Obtain *separate*, truthful finalized-build human-reviewed Windows and Mac
   screenshots: now playing, Settings, real captions, first run,
   movie/TV details, library and Up Next where implemented. Record platform,
   version, operating system, source media and reviewer. Only after that
   may the site replace its fixture-labelled pictures and the matrix rows
   be changed with legitimate platform verification.

## Promoting genuinely reviewed native release screenshots

`scripts/check_release_media.py` is a fail-closed source guard for any new
`site/assets/{windows|macos}-*-release.png` file. It runs in Windows/Linux CI
and GitHub Pages and requires a matching `site/assets/release-media.json`
manifest. This source guard **does not manufacture images or approve them**.
Once a human has examined native final-build captures, add a manifest like:

```json
{
  "version": "0.30.8",
  "captures": [
    {
      "file": "windows-now-playing-release.png",
      "platform": "windows",
      "version": "0.30.8",
      "operatingSystem": "Windows 11",
      "screen": "Now playing",
      "captureMethod": "native-finalized-build",
      "reviewer": "<actual reviewer>",
      "reviewDate": "<actual review date>",
      "sha256": "<sha256 digest of the captured PNG>"
    }
  ]
}
```

The file names and labels must match their platform and use the actual built
source version. The manifest checker rejects missing, duplicate, changed-hash,
mislabeled, outdated or synthetic/CI-test-only records. The independent site
checker validates PNG headers, chunk CRCs and minimum readable dimensions.
**Neither automated check establishes visual authenticity:** a reviewer must
compare the native window with the image and record real provenance before
promoting a final screenshot. No release-media manifest or final release PNG
has been invented or added to the PR.

## GitHub Pages post-merge gate

- `.github/workflows/pages.yml` deploys on `main`, not from this draft branch.
  After the PR is accepted and merged, `verify-live` fetches
  `deployment.json?sha=<main commit>` until the CDN serves the exact revision.
- The live browser then verifies all committed screenshots decode and remain
  within the 320px and 1280px viewports, checks all walkthrough steps,
  confirms no horizontal overflow or JavaScript exceptions, exercises
  screenshot lightbox keyboard focus, and uploads full-page
  `deployed-pages-review` images for human inspection.
- Do **not** check #468.5, #468.6 or #468.10 based solely on the existence of
  this workflow or on a passing local Playwright test. The deploy and
  review must actually occur against this PR's final gallery.

## Non-retroactive gate

Historical #482.10 cannot be made true in the past. Its equivalent protection
for PR #488 is to keep this PR **draft and unmerged** until the native visual,
CI, parity/release-media and Pages acceptance checks above are completed.
