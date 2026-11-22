# ADR-0020: Parity between platforms: one ledger, one release

Status: accepted (2026-10-20)

## Context

rexplayer is getting a macOS build, and the Windows build keeps changing while it is written. Two
builds of one product drift apart unless the difference between them is written down somewhere a
machine reads. What keeps two platforms honest, in every product that manages it, is the same
short list: shared semantics with native adapters, one ledger of accepted differences with an owner
for each, shared fixtures that every platform's suite consumes, one driver that tests both user
interfaces, and a release gate that reads the ledger.

## Decision

1. **One ledger.** `docs/capability-matrix.json` carries a status per platform (`status.windows`,
   `status.macos`). A platform's row is `verified` only while a `[Capability]` test that runs on that
   platform proves it; a test in a platform-specific project proves its platform only. Two statuses
   explain a difference away: `not-applicable` (the feature has no meaning there) and `gap` (a
   difference accepted for now), and both require a `parityNote`; a gap also names the GitHub issue
   that closes it in `parityIssue`. `docs/parity.md` is generated from the matrix and lists every
   row where the platforms differ.
2. **One policy.** The matrix's `parityPolicy` is `preview` while the macOS build catches up: a
   row may be further along on one platform. It becomes `locked` once the must rows match, and from
   then on `CapabilityMatrixTests.TheParityPolicyHolds` fails, and so does every build, while a must
   row is done on one platform and not the other.
3. **One release.** A version publishes the artefacts of every platform from one tag, or none of
   them; a platform job that fails means no release.
4. **Shared semantics.** State, commands, settings, persistence, errors and lifecycle live in the
   portable projects; the shells only bind them to widgets. A feature lands on both shells, or its
   matrix row says `gap` with a note and an issue.

## Consequences

Drift is visible on every pull request in `docs/parity.md` and, once locked, blocks releases
instead of surprising users. The price is one more status per row and a note for every exception,
which is exactly the record a second platform needs.

## Amendment (2026-11-22): the ledger covers the look

What went wrong: the ledger read "the platforms match" while one shell had none of the design. A
row was proved by a test that read state over the automation pipe (`state=Playing`, `meter=48`),
and nothing looked at the window; one launch-and-quit test on the Mac claimed 52 rows, `UI-01`
(Main window) and `AU-18` (Visualisations) among them, so both read verified on macOS while the
Mac window was unstyled and drew no visualisation. No row described the look at all.

5. **The look is in the ledger.** The matrix has an area "Look" with one row per screen
   (`LOOK-01` the welcome screen, `LOOK-02` the transport and seek bar, and so on). The structure of
   each screen is described once, in `Rex.Media.AppCore.Design.ShellScreens`, beside the design
   numbers (`DesignTokens`) and what each colour is for (`DesignRoles`).
6. **A look row is verified on a platform only while a `[Capability]` test on that platform proves
   two things.** The screen's structure, reported by the running shell, equals its one description
   in `ShellScreens`, so a control added to one shell fails the other until both agree; and a
   screenshot of that screen is saved by CI under the same name on both platforms, for a person to
   compare. A row whose screenshots have not been compared stays built.
7. **A behaviour row does not vouch for the look.** A behaviour row's verified says the behaviour
   is proved, not that its screen looks right. `UI-01` and `AU-18` on macOS are back to built until
   tests that look at the window prove them.
8. **A test proves a few rows, not a platform.** A test method may claim only a few rows; the limit,
   and an allow-list of the methods over it that may only shrink, are enforced in
   `CapabilityMatrixTests`. One launch-and-quit test cannot verify a whole platform again.

The price is one more test per screen on each platform and a screenshot to look at when a screen
changes, which is the only check that would have seen the difference.
