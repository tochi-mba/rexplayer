# Architecture

rexplayer is a family of small .NET projects. The engine is portable .NET; only the edges know about
Windows ([ADR-0003](adr/0003-portable-engine-windows-at-the-edges.md)).

```text
           rexplay.exe (Rex.Media.Cli)          rexplayer.exe (Rex.Media.App, later)
                         \                         /
                          Rex.Media.AppCore  ── command line, shared app logic
                                 |
                          Rex.Media.Engine   ── media session: mailbox, states, clocks, threads
                     /       |        |        \
          Containers     Codecs    Audio      (Video, Subtitles, Net... as they arrive)
               |            |         |
              IO     Codecs.Software  Audio.Wasapi ── Interop (CsWin32, the only native code)
               \            |         /
                    Rex.Media.Primitives ── time, buffers, bit reading, the media model
```

`ArchitectureTests` holds the full allowed-reference table and fails on any other edge
([ADR-0001](adr/0001-project-family-and-dependency-graph.md)).

## How a file plays

1. **Open.** The session's mailbox thread asks `DemuxerRegistry` to probe the first 64 KiB. Every
   demuxer scores the bytes; the best one opens the file. The extension only breaks ties.
2. **Choose decoders.** The first audio track (or the one marked default) goes to
   `DecoderRegistry`, which walks the decode ladder ([ADR-0009](adr/0009-where-each-codecs-decoder-comes-from.md)).
3. **Open the sink.** The sink says what format it wants; `AudioPipeline` will resample and remix to it.
4. **Run.** The demux thread reads packets into a bounded queue. The audio thread decodes, trims
   (for a precise seek), converts and writes to the sink, which blocks when its buffer is full. That
   back-pressure paces everything.
5. **Report.** State changes, position and statistics go to listeners on the event thread
   ([ADR-0007](adr/0007-state-machine-and-events.md)).

## Seeking

A seek increments the generation, cancels the old generation's token, rebases the clock and
flushes the queue. The demux thread repositions; the audio thread meets the first packet of the new
generation, flushes its decoder, pipeline and sink, and reports the seek complete when the first new
frame is ready ([ADR-0006](adr/0006-seeks-are-generations.md)).

## Where to look

| Question | File |
|---|---|
| Which states exist and how they connect | `src/Rex.Media.Engine/SessionState.cs` |
| What each command does | `src/Rex.Media.Engine/MediaSession.Commands.cs` |
| How data moves between threads | `src/Rex.Media.Engine/Playback.cs`, `BoundedQueue.cs` |
| How audio is converted for a device | `src/Rex.Media.Audio/AudioPipeline.cs` |
| How a format is recognised | `src/Rex.Media.Containers/DemuxerRegistry.cs` |
