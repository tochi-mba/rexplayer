# ADR-0010: The library store

Status: accepted (2026-10-05)

## Context

A media library must survive crashes and stay fully testable.

## Decision

The library is an append-only log of checksummed records, compacted by writing a new file and swapping it in atomically. A torn tail is truncated on load.

## Consequences

No database dependency; every recovery path is a unit test.
