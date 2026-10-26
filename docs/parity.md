# Parity ledger

Every row of the [capability matrix](capability-matrix.md) where the platforms differ, generated from
`capability-matrix.json`. Parity policy: **preview**. Under *preview* a row may be further along on one
platform while the other catches up; under *locked* every must row is verified on both platforms or marked not
applicable with a reason, and CI blocks a release otherwise (ADR-0020). A **gap** is a difference someone has
accepted for now: its note says why and its issue says who closes it.

Totals: 5 rows differ (2 must), 2 not applicable, 0 gaps.

## Formats: Audio decoders

| ID | Capability | Priority | Windows | macOS | Note |
|---|---|---|---|---|---|
| FMT-A10 | Opus (SILK/CELT/hybrid, multistream for surround) | must | built | planned |  |

## Audio

| ID | Capability | Priority | Windows | macOS | Note |
|---|---|---|---|---|---|
| AU-18 | Visualisations | should | verified | built |  |

## Tools, interfaces, extensions & CLI

| ID | Capability | Priority | Windows | macOS | Note |
|---|---|---|---|---|---|
| TOOL-09 | Updater | should | verified | built |  |

## Platform integration

| ID | Capability | Priority | Windows | macOS | Note |
|---|---|---|---|---|---|
| WIN-03 | Explorer verbs | must | planned | not-applicable | Explorer verbs are Windows' own; on macOS the Finder's Open With comes from the file associations (WIN-02). |
| WIN-04 | AutoPlay handlers | should | planned | not-applicable | AutoPlay has no macOS equivalent. |
