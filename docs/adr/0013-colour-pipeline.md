# ADR-0013: Colour pipeline

Status: accepted (2026-10-05)

## Context

Video colour depends on matrices, ranges and transfer functions that files often leave unspecified.

## Decision

Unspecified colour is resolved from the picture size (BT.601 for standard definition, BT.709 above), converted to RGB in shaders with tested constants, and HDR is tone-mapped for SDR displays.

## Consequences

Correct colour by default and a single place to change it.
