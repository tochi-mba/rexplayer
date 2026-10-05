# ADR-0011: Commands, settings and the machine envelope

Status: accepted (2026-10-05)

## Context

The app, the command line, keyboard shortcuts and extensions all trigger the same actions.

## Decision

Every action is a named command. Hotkeys map to commands. The command line prints one JSON envelope per command, `{ok, protocolVersion, command, data | error}`, starting at protocol version 1.

## Consequences

One behaviour, many surfaces, one set of tests.
