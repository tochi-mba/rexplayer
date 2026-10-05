# Security

## Reporting a problem

Report security problems privately through
[GitHub's private vulnerability reporting](https://github.com/tochi-mba/rexplayer/security/advisories/new)
rather than in a public issue. Include the rexplayer version (`rexplay version`) and, if a file
triggers the problem, a description of how it was made rather than the file itself until we ask.

## What rexplayer may do

- Read the media you open, and nothing else on your disk.
- Write its settings, library and logs to `%LocalAppData%\REX\rexplayer`, and capture files only where
  you tell it to.
- Use the network only for addresses you open.

## What rexplayer never does

- Collect telemetry or analytics, or require an account.
- Run code from a media file. Media is parsed by code written to treat every byte as hostile: every
  parser rejects impossible values, and parsers are fuzzed.
- Ask for administrator rights. It installs and runs for the current user only.
