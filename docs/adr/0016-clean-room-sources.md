# ADR-0016: Clean-room sources

Status: accepted (2026-10-05)

## Context

Media formats are defined by standards, but other players' code is not ours to learn from.

## Decision

Implementations come from published standards and format documentation, papers and observed behaviour. Code published inside a standard (an RFC's normative decoder) or a format owner's permissively licensed reference counts as specification. The source code of other media players and frameworks is never read. Each format file cites its source on a `// Spec:` line.

## Consequences

Every implementation's provenance is on record.
