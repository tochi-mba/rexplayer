# ADR-0018: Test projects and the coverage gate

Status: accepted (2026-10-05)

## Context

The pure engine must be proved on any operating system; Windows integration needs Windows.

## Decision

`Rex.Media.Tests` (portable) holds unit, engine and repository tests and runs on Windows and Linux. Windows integration and desktop UI tests get their own projects. Every file in `tests/coverage-required.txt` must keep 100 % line coverage; branch coverage is reported, because switch expressions compile to branches no input can reach.

## Consequences

A fast, portable suite is the default, and slow suites cannot slow it down.
