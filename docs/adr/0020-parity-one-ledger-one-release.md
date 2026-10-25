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
