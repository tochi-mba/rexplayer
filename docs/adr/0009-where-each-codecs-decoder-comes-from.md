# ADR-0009: Where each codec's decoder comes from

Status: accepted (2026-10-05)

## Context

Some codecs are still under patent; others are free, and Windows ships only some decoders in the box.

## Decision

rexplayer writes its own decoders for codecs whose patents have expired or that are royalty-free (PCM, G.711, MP3, FLAC, AC-3, MPEG-1/2 video, Vorbis, Opus and others). Patented codecs (H.264, HEVC, AAC) decode only through Windows Media Foundation. A decode ladder tries candidates best first and reports in plain words why a track cannot play.

## Consequences

Patent exposure stays with licensed operating-system components, and a missing Windows codec is explained to the user.
