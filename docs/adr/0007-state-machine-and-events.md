# ADR-0007: State machine and events

Status: accepted (2026-10-05)

## Context

A player's states must be few, named and impossible to misuse.

## Decision

A session has ten states with a legal-transition table held as data and checked pair by pair by a test. Events go to listeners on one thread in order: discrete events are queued and never lost; position and statistics are latest-value slots.

## Consequences

Listeners can be slow without stalling playback, and an illegal transition is a test failure, not a mystery.
