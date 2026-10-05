# ADR-0014: Passthrough

Status: accepted (2026-10-05)

## Context

Receivers can decode surround formats themselves.

## Decision

Bitstream passthrough is off by default and used only when the endpoint accepts the format in exclusive mode; the volume and effects are bypassed and the interface says so.

## Consequences

No silent surprises for users without receivers.
