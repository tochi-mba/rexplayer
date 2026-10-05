# ADR-0017: Packaging

Status: accepted (2026-10-05)

## Context

Installs must need no administrator rights and upgrade cleanly.

## Decision

rexplayer is unpackaged. A per-user Inno Setup installer with a fixed AppId installs it to `%LocalAppData%\Programs\rexplayer`; every release also ships a portable zip; both have SHA-256 files. Releases are published by CI and never replaced.

## Consequences

No Store dependency; upgrades and rollbacks are predictable.
