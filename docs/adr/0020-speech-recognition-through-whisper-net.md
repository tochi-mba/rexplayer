# ADR-0020: Speech recognition through Whisper.net

Status: accepted (2026-10-13). Amends [ADR-0002](0002-no-third-party-runtime-code.md).

## Context

Kinetic captions (OSD-11) put each word on the picture the moment it is said. A subtitle file
times its cues, not its words, so without hearing the speech the words can only be placed by
guessing from the sound's syllables. Hearing the speech needs a speech model and a runtime that
runs it. Windows has no offline speech-to-text for files; its speech API listens to a microphone.
The open Whisper models (MIT) hear speech well, give every token a time, and run on an ordinary
processor; whisper.cpp runs them, and Whisper.net (MIT) wraps whisper.cpp for .NET and carries
its native runtime for Windows x64.

## Decision

The product may run speech models through Whisper.net, and nothing else, on these terms:

1. **Runtime.** `Whisper.net` and `Whisper.net.Runtime` (MIT) are listed in the product packages
   and referenced by `Rex.Media.Speech` alone; the app and the command line reach them through
   that adapter. `RepositoryTests.ProductPackagesAreOnlyTheOnesTheDependencyRecordAllows` enforces
   both. The runtime runs on the processor; no graphics card build is taken.
2. **Never bundled.** No model is committed, packaged or installed. A model is downloaded only
   after the user asks (Transcribe, or Download in the kinetic caption settings), into
   `%LocalAppData%\REX\rexplayer\models\speech`, and can be removed there at any time.
3. **Pinned.** `SpeechModels` names each file's URL at the whisper.cpp project's model page, its
   size and its SHA-256. A download resumes with a byte range, is written beside its final name as
   `.part`, and is moved into place only when its hash matches; anything else is deleted and reported.
4. **Permissive licences only.** The models and the runtime are MIT.
5. **Optional.** Kinetic captions work without a model: a subtitle track's words are placed by
   the syllables heard. The transcript is a better source, not a required one.
6. **Policy stays pure.** The catalogue, the store, the download, the transcript's shape, its
   cache, its turning into cues and its matching against a subtitle file live in `Rex.Media.AppCore`
   (portable, 100 % covered). `Rex.Media.Speech` only decodes the sound to 16 kHz and runs the model.

### Licences checked

Read from the authors' own pages on 2026-10-13.

| What | Licence | Source |
|---|---|---|
| Whisper.net, Whisper.net.Runtime | MIT | [LICENSE](https://github.com/sandrohanea/whisper.net/blob/main/LICENSE) |
| whisper.cpp (the runtime inside) | MIT | [LICENSE](https://github.com/ggerganov/whisper.cpp/blob/master/LICENSE) |
| Whisper models (tiny.en, base.en, small.en as ggml) | MIT | [openai/whisper LICENSE](https://github.com/openai/whisper/blob/main/LICENSE), [model page](https://huggingface.co/ggerganov/whisper.cpp) |

## Progressive transcription

Library transcription and transcription of what is playing use the same optional offline model.
The pure AppCore worker plans bounded audio chunks, prioritises the current playhead, and
merges recognised words into a transcript keyed to the file's signature and model. It saves
partial progress between runs, retries failed work with backoff, and publishes subtitle cue
replacements and a seek-bar coverage range as each chunk completes. Library work is off by
default and respects playback, battery, folder exclusion and model availability settings.

Only the speech adapter invokes the speech runtime. An audio reader turns each bounded range
into 16 kHz mono samples before the model hears it. The window merely displays progress and
cues, accepts the user preferences, and does not decide chunk order or retries. The user's
media and recognised words remain on the machine; a model is downloaded only under the
conditions set out above, and the user can clear retained transcripts in Preferences.
