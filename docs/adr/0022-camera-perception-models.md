# ADR-0022: Camera perception for the visualisations and the conductor

Status: accepted (2026-10-29). Amends [ADR-0019](0019-optional-models-through-windows-ml.md).

## Context

The camera visualisations (AU-18) know one thing about the listener: `SilhouetteMask`, an outline
from background subtraction at 240 by 180. It must learn an empty room first, it breaks when the
light changes, and it says nothing about where the hands, the joints or the face are. So every
camera effect amounts to tinting that outline or copying the picture, and nothing in the player can
be steered by a hand. The conductor (remixing a song's stems with hand gestures) needs hands with
fingers; the effects the user asked for need a soft matte, the body's joints and the motion of the
picture, at the camera's own rate, on an ordinary laptop.

ADR-0019 allows model weights through Windows ML, pinned, permissively licensed, optional and never
bundled, but it names only the authors' own release as a source, and the one time it measured
models (EfficientSAM, MI-GAN) they took seconds a picture on integrated graphics. Those were the
wrong size of model for a camera; the right size exists.

## Decision

1. **One contract for what the camera sees.** `Rex.Media.Vision.SceneSense` (portable, fully
   tested) is the one shape every finder fills: a soft matte, a flow field, up to two hands of 21
   points in the MediaPipe order, and a body of 33 points in the BlazePose order, each point as a
   share of the picture with a confidence. Effects, the gesture reader and the conductor read only
   this, so they are the same whether the points came from a downloaded model, the Mac's own
   finders, or rexplayer's hand-written fallback. Points are smoothed with a 1 euro filter
   (`OneEuroFilter`, `LandmarkSmoother`): steady at rest, immediate in motion.

2. **Published conversions count as a release.** ADR-0019 pinned "a fixed revision of the authors'
   own release". A model may also be taken from a conversion that a permissively licensed project
   publishes under its own repository and commit, when the original authors' licence is permissive
   too and the converting project names its source and tools. The catalogue records both licences
   and the converter's commit. Weights still never ship in the installer, are still verified by
   SHA-256, and every feature still works without them.

3. **The camera pack.** Four models, all Apache-2.0, converted and published by OpenCV's model
   zoo (`opencv/opencv_zoo`, Apache-2.0, mirrored under the `opencv` organisation on Hugging Face,
   pinned by commit), about 32 MB together:

   | Model | Does | Input | Original authors | Converter |
   |---|---|---|---|---|
   | PPHumanSeg | the matte (person against room) | 192 by 192 | PaddlePaddle (Apache-2.0) | OpenCV zoo, `human_segmentation_pphumanseg_2023mar.onnx` |
   | Palm detector | where hands are (boxes and 7 palm points) | 192 by 192 | Google MediaPipe (Apache-2.0) | OpenCV zoo, `palm_detection_mediapipe_2023feb.onnx` |
   | Hand landmarks | 21 points per hand, handedness | 224 by 224 crop | Google MediaPipe (Apache-2.0) | OpenCV zoo, `handpose_estimation_mediapipe_2023feb.onnx` |
   | Person detector and pose | where the body is, then 33 joints and a 256 by 256 mask | 224 and 256 | Google MediaPipe (Apache-2.0) | OpenCV zoo, `person_detection_mediapipe_2023mar.onnx`, `pose_estimation_mediapipe_2023mar.onnx` |

   The detectors' SSD anchors are generated in code from the published strides and counts and
   checked against the zoo's own tables in tests; decoding, non-maximum suppression, the crop
   placed from the palm and the body, and the tiers below are pure code in `Rex.Media.Vision`.
   `Rex.Media.Vision.WindowsML` only runs sessions (ADR-0008).

4. **The Mac needs no download.** Apple's Vision framework ships the same three finders
   (`VNGeneratePersonSegmentationRequest`, `VNDetectHumanHandPoseRequest`,
   `VNDetectHumanBodyPoseRequest`), permitted by ADR-0021 as a platform framework. The Mac runner
   fills `SceneSense` from them; the row in the parity ledger says so rather than claiming a gap.

5. **Perception obeys the performance tier.** The work runs on its own worker at the camera's
   rate, never on the rendering or the audio thread, and never allocates per frame. `DeviceBudget`
   says how much of it a tier does: Best quality runs the matte and the hands every picture and the
   body every other; Balanced runs the body every third; Fastest runs the hand-written fallback
   only. A model that misses its `FrameBudget` on a given computer drops to the next lower rate for
   that model alone, and the hand-written matte and flow are always computed, so a frame always has
   something. Energy saver lowers the tier as it does everything else.

6. **The hand-written fallback stays.** Without the pack, or on Fastest, a new matte from motion and
   colour with no empty-room step replaces `SilhouetteMask`, a block-matching flow field gives the
   motion, and hands are guessed from the outline's upper ends. Gestures then reduce to zones and
   dwell, so the conductor still works with less precision.

### Licences checked

Each licence was read from the published repository on 2026-10-29.

| Model | Original licence | Conversion licence | Source |
|---|---|---|---|
| PPHumanSeg (PaddleSeg) | Apache-2.0 | Apache-2.0 (OpenCV zoo) | [LICENSE](https://huggingface.co/opencv/human_segmentation_pphumanseg/blob/main/LICENSE) |
| MediaPipe palm detector, hand landmarks | Apache-2.0 | Apache-2.0 (OpenCV zoo, converted with tensorflow-onnx and onnx-simplifier) | [LICENSE](https://huggingface.co/opencv/palm_detection_mediapipe/blob/main/LICENSE), [LICENSE](https://huggingface.co/opencv/handpose_estimation_mediapipe/blob/main/LICENSE) |
| MediaPipe person detector, pose | Apache-2.0 | Apache-2.0 (OpenCV zoo, from a published TFLite export) | [LICENSE](https://huggingface.co/opencv/person_detection_mediapipe/blob/main/LICENSE), [LICENSE](https://huggingface.co/opencv/pose_estimation_mediapipe/blob/main/LICENSE) |

Considered and not chosen: the multi-person RTMO and RTMPose families (Apache-2.0, OpenMMLab)
publish ONNX only through their own deployment tool and at sizes measured in the hundreds of
milliseconds on integrated graphics; Depth Anything V2 Small (Apache-2.0) is a later, optional
tier for depth; EfficientSAM (ADR-0019) stays for Subject Lock, where seconds a picture are fine.

### Measured

`onnxruntime 1.24.4`, 30 runs after one warm-up, random input, on the same computer as the
2026-10-14 hardware run (Intel Core i7-10510U, Intel UHD Graphics, 32 GB), while builds ran
alongside. Full table in `docs/hardware-runs/2026-10-29-camera-models.md`.

| Model | Processor, median / p90 | Graphics (DirectML), median / p90 |
|---|---|---|
| PPHumanSeg matte | 16.0 / 20.0 ms | 15.7 / 23.8 ms |
| Palm detector | 25.7 / 31.2 ms | 34.4 / 40.8 ms |
| Hand landmarks, per hand | 25.4 / 36.6 ms | 17.9 / 42.3 ms |
| Person detector | 70.6 / 361.6 ms | 31.9 / 33.9 ms |
| Pose, 33 joints and mask | 53.9 / 86.1 ms | 61.0 / 74.8 ms |

On this laptop the matte and two hands fit a 30 frames-a-second camera on the processor alone, and
the body fits every other frame; a separate graphics card has room to spare. The person detector is
run only until a body is found and again when it is lost, as MediaPipe does, so its cost is rare.

## Consequences

The camera effects see a matte, joints and fingers instead of a learned outline, and the conductor
can be built on the same points. Four more weight files are tracked, each a reviewed URL, size and
hash, and two licences each. The hand-written path remains and is what Fastest and the first run
use, so no one loses the feature by declining the download. `SilhouetteMask` goes once the new
matte lands; nothing else in the player changes.
