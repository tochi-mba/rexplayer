# ADR-0008: Humble adapters, pure policy

Status: accepted (2026-10-05)

## Context

Windows APIs cannot run on CI runners without a GPU or an audio device, but their decisions must still be tested.

## Decision

Adapters only sequence calls and translate errors. Any decision (which format to ask a device for, how to fall back) lives in a pure class with tests. Native declarations live only in `Rex.Media.Interop`, enforced by a repository test, which is also the only place `unsafe` code is allowed.

## Consequences

Coverage gates the code that decides; adapters are proved by integration and hardware runs.
