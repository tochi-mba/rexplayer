# ADR-0004: Engine concurrency

Status: accepted (2026-10-05)

## Context

A media pipeline has several threads; shared mutable state between them is where players hang and crash.

## Decision

Each media session has one mailbox thread that owns its state, a demux thread and an audio thread (video threads join later). Data crosses threads only through bounded queues that hand over ownership of pooled buffers. Public calls post commands and return tasks.

## Consequences

State changes are serialised and testable; back-pressure comes from the sink; steady-state playback allocates nothing.
