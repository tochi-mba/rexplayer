# Library backup, restore, and incomplete-file transaction contract

Status: **design complete; complete export publication and digest verification are implemented in follow-up PR #488, but atomic cross-store restore, previews and native acceptance remain outstanding**. Scope: merged PR #485 task **485.4**, with interfaces to #485.3 (refresh/recovery), #481.7 (disconnected media), and existing LIB-10 and LIB-05 behavior.

## 1. Source-informed baseline (2026-10-23)

- `LibraryBackup.Export` currently produces a JSON manifest (`rexplayer library.json`) and optional M3U8 sidecars. The manifest includes watched folder paths, `LibraryEntry` values and named playlists.
- `MainWindow.BackUpLibraryAsync` now calls `LibraryBackup.PublishSnapshot` to stage and flush all payloads and `complete.json` with SHA-256 digests before atomically publishing a fresh sibling directory. Existing backups are never overwritten. `LibraryBackup.ReadFromFile` verifies its manifest and contents before mutation, while allowing legacy standalone v1 JSON restores.
- `LibraryBackup.Read` validates JSON syntax and schema version. `LibraryBackup.Restore` mutates watched folders, entries, then playlists; there is **no cross-store transaction** or full preflight of semantic validity.
- `MediaLibrary.Restore` merges newer play history into its store and preserves hidden entries. `RexStore` is append/compact based, with its own I/O fallbacks; it does not expose a transaction covering `PlayerController` playlists.
- `Downloads.Judge` already tracks `Downloading`, `Suspect`, `FinalSize` and `Allocated` separately; an incomplete but present file is **not** the same state as a missing or unreadable file.

These facts remain deliberate constraints: **export publication is snapshot-atomic**, but the existing **restore is still not cross-store transactional**.

## 2. Export format and commit protocol

### Snapshot

1. Capture the complete library/playlist snapshot under the existing storage locks or an equivalent immutable snapshot boundary. Do not enumerate live collections while scanning mutates them. Keep only local user-selected folders and data; never transmit file paths.
2. Write to a fresh sibling staging directory named `.<backup-name>.<uuid>.staging` on the same volume as the destination. Never expose an in-progress backup as the advertised completed archive.
3. Write the canonical versioned JSON manifest and sidecar M3U8 files; reject paths that escape the staging root (no `..`, absolute path, or symlink traversal) and resolve collisions using the existing `SafeName` policy.
4. Hash every file with SHA-256 and record path, size and digest in a separate versioned completion manifest, along with a snapshot ID, creation UTC timestamp, producer version and a `complete: true` marker. Record whether copied files were incomplete at snapshot time; **do not** include media bytes.
5. Flush file contents before manifest completion. Flush the completion manifest last. Publish by same-volume atomic rename into a new versioned backup folder; keep the previous published version until the new one is visible and validated. Do not overwrite a completed folder piecemeal.
6. On cancellation, full disk, access denial, crash or interruption, remove stale stage files when safe but **never** damage the previously published backup. At next launch, stale staging folders must be identified as abandoned, not offered as restorable archives. Clean them only with user consent when they are outside app-owned temp locations.
7. Distinguish `Cancelled`, `Disk full`, `Access denied`, `Partial staging data remains` and `Completed backup` in progress and error messaging. Never show success before atomic publication.

### Backward compatibility

- Keep `LibraryBackup.CurrentVersion == 1` imports readable as **legacy** backups. A lone v1 JSON manifest has no completion hashes; explicitly tell the user this provenance cannot be verified instead of pretending it was written transactionally.
- A future v2 export must include the completion manifest and verified digests; reject unsupported future versions without mutation, and never silently downgrade.
- The M3U8 sidecars are *export conveniences*, not an independent source of truth for import. Missing or mismatched sidecars are reported as incomplete even if the JSON is intact; a separate, explicitly chosen legacy-JSON import may still be available.

## 3. Restore as preflight, plan, commit

**Preflight : strictly read-only:** parse and version-check the manifest; cap archive size and item counts; verify hashes; reject traversal, duplicate archive paths, invalid non-null collection members, malformed locations, nonfinite durations and counters, invalid playlist boundaries and unsupported schema. Normalize entries without touching the live library or controller. Detect and report existing watched folders, entries, duplicate playlists, unavailable disks and incomplete source media. Build one immutable `RestorePlan` with the proposed additions/merges/conflicts and estimated writes.

**Preview and consent:** show counts and non-destructive merge policy. A missing file in the backup is *not* restored as a live playable file; the row may keep its history, original location, missing/relink action and no invented duration or art. Existing play history wins by the current higher-play-count/later-played rule; an explicit user choice is required before replacing nonhistory metadata. Never rescan network paths as a side effect of merely previewing.

**Commit:** create a durable rollback checkpoint of the library store and named-playlist state before any change. Apply the plan under an exclusive logical restore gate that pauses conflicting writers but not playback; resume the scanner only after commit. On success, publish/flush both stores and record completion, then make one UI refresh. If an exception occurs before the durable commit marker, restore the checkpoint for **both** stores, suppress intermediate success messages and show rollback status. If rolling back fails, report that the state is *partial*, retain both checkpoints and provide non-destructive repair/retry instructions. Restore is idempotent across retries by stable entry location, folder and playlist identity; it must never double play counts or create duplicate playlists.

A transaction spanning two independent `RexStore` consumers must be implemented explicitly; relying only on the current `AtomicFile.Write` helper is insufficient. The operation must not write embedded source media tags or silently delete existing library history.

## 4. Incomplete, missing, disconnected and inaccessible media

These are distinct and must remain so in library rows, item details, backup previews, restoration summaries, and player actions:

| State | Evidence | Safe visible behavior | Restore/refresh rule |
| --- | --- | --- | --- |
| Complete / readable | Stable normal file with no partial flags | Play/seek where supported | Restore location and history |
| Downloading / partial | Marker or declared final size larger than written bytes; sparse allocation short | Show `Still downloading` plus percentage **only** if denominator trusted; permit only supported growing-file playback | Preserve download evidence; never claim complete from file size alone |
| Suspect / changing | Weak growth or writer lock not yet independently confirmed | Show a cautious `Checking file` state, never a confirmed download percentage | Keep existing history and recheck, do not delete |
| Missing / moved | Previous indexed location no longer resolves | Show unavailable and a safe relink/file-locate action | Preserve play counts, bookmarks, playlists and history |
| Disconnected storage | Mounted volume or network share absent | Name the volume/folder as temporarily unavailable; do not erase cached library | Defer scan/delete until storage reconnects |
| Access denied / corrupt | Filesystem or media-probe error | Explain error, do not promote to ready state or repeatedly retry on UI thread | Keep metadata/history and record actionable error |
| Restored from legacy backup | v1 JSON with no completion proof | Say “legacy backup, completeness unverified” | Offer preview; don't equate JSON parse success with validated export |

**Critical:** an entry may have an incomplete download *and* a last-known valid history. Keep these orthogonal rather than collapsing them to `Missing`. Percent must be bounded 0–99 during active download and only reach 100 when the completed-file invariant is confirmed.

## 5. Required validation before implementation is called shipped

Pure tests: malformed/versioned archive, path traversal, 0-byte/stale manifest, hash mismatch, duplicate/case-colliding sidecar paths, playlist conflicts, hidden-entry preservation, big library bounds, Unicode/RTL names, interrupted snapshot and idempotent repeated import.

Fault injection: disk full at every write and commit step, denied staging/manifest path, cancellation before/after manifest flush, crash between per-store writes, rollback failure, simultaneous scanner updates, stale staging recovery, unplugged drive and hard-to-read network share.

Native Windows tests: backup preview/cancel, restore preview/cancel, incomplete/downloaded/missing indicators, correct per-item status, recovery after restart, keyboard focus, accessible names and error announcements at compact/high-DPI/high-contrast widths.

Quality gates: bounded background I/O and memory, test all current-head platforms, screenshot proof for each error state, warnings-as-errors and a clear audit trace of final commit. **None of these native/runtime checks are claimed by this design file.**

## 6. Decision and dependency boundary

Chosen contract: *snapshot → stage+hash → durable publish* for export, and *read-only preflight → explicit preview → cross-store atomic commit/rollback* for import. This preserves existing history semantics while making partial and disconnected media truthful.

This document satisfies the **design** deliverable in #485.4. The follow-up implements the snapshot-stage-digest-publish portion with pure integrity/interruption tests. The full restore protocol, preflight preview, checkpoint/rollback, stale-stage recovery UX and native validations in section 5 **remain unimplemented and are not claimed as shipped**.
