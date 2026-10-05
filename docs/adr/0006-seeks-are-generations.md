# ADR-0006: Seeks are generations

Status: accepted (2026-10-05)

## Context

A seek must stop old media instantly, even when a thread is blocked in a full device buffer or a pause.

## Decision

Every seek increments a generation. Packets and frames carry their generation; queues dispose older ones; the demuxer repositions; the audio side flushes its decoder and sink when the new generation's first packet arrives. Each generation has a cancellation token that interrupts every blocking wait. A newer seek supersedes an older one. Precise seeks trim to the exact sample; keyframe seeks start at the containing keyframe.

## Consequences

Seeking is immediate and exact, and no stale sample can be played.
