# ADR-0002: No third-party runtime code

Status: accepted (2026-10-05)

## Context

rexplayer is meant to be REX code end to end, and every dependency is something to track for the life of the product.

## Decision

The runtime closure is the .NET base library and the Windows App SDK. CsWin32 is a compile-time generator. Build and test tools are allowed: Inno Setup, xunit.v3, Microsoft code coverage, Node, Playwright and axe for the site, and the Python standard library. `RepositoryTests.ProductPackagesAreOnlyTheOnesTheDependencyRecordAllows` enforces the product list.

The FFmpeg command line is a fixture tool only: `scripts/make-fixtures.ps1` uses it to encode a few committed test files with an encoder rexplayer did not write, so a misreading of a specification shared by rexplayer's own encoder and decoder cannot hide. Nothing builds, tests or ships with it.

## Consequences

Everything media-related is written here, so the project owns its bugs and its pace.
