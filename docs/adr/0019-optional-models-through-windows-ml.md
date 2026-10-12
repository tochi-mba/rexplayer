# ADR-0019: Optional models through Windows ML

Status: accepted (2026-10-12). Amends [ADR-0002](0002-no-third-party-runtime-code.md).

## Context

Subject Lock selects, follows and removes an object with hand-written algorithms: a template and
colour tracker, a conservative reacquisition search and a fill from neighbouring and earlier pixels.
They work everywhere and fail safe, but a click selects a rectangle rather than the object, and a
removed object leaves a smeared hole. Small, published segmentation and inpainting networks do both
far better. Running them needs a neural network runtime and model weights, which ADR-0002 did not
allow. The Windows App SDK that rexplayer already ships carries Windows ML (the ONNX Runtime with
DirectML, package `Microsoft.WindowsAppSDK.ML`), so the runtime is already in the installer; only
the weights are new, and they are third-party data with their own licences.

## Decision

The product may run model weights through Windows ML and nothing else, on these terms:

1. **Runtime.** Only Windows ML, the ONNX Runtime the Windows App SDK ships. `Microsoft.WindowsAppSDK.ML`
   is listed in the product packages at the version `Microsoft.WindowsAppSDK` depends on, referenced
   by `Rex.Media.Vision.WindowsML` alone; the app already carries its binaries through the SDK, so the
   installer does not grow. `RepositoryTests.ProductPackagesAreOnlyTheOnesTheDependencyRecordAllows`
   enforces both.
2. **Never bundled.** No weights are committed, packaged or installed. They are downloaded only after
   the user chooses Download in Preferences ("Enhanced Subject Lock (optional download)"), into
   `%LocalAppData%\REX\rexplayer\models`, and can be removed there at any time.
3. **Pinned.** `ModelCatalogue` names each file's URL at a fixed revision of the authors' own release
   (their GitHub repository or Hugging Face page), its size and its SHA-256. A download resumes with a
   byte range, is written beside its final name as `.part`, and is moved into place only when its hash
   matches; anything else is deleted and reported.
4. **Permissive licences only.** Apache-2.0, MIT or BSD, which allow commercial use and
   redistribution. Never AGPL, GPL, non-commercial or research-only terms. The downloader refuses a
   model whose catalogue licence is not on that list.
5. **Optional.** Every feature works without the models. The hand-written algorithms stay the
   fallback: when a model is not installed, fails to load, or is too slow on this computer
   (`FrameBudget`), Subject Lock uses its own methods.
6. **Policy stays pure.** The catalogue, the install state machine, the store, retry and disk-space
   rules, the choice of device (`ExecutionPolicy`: DirectML on a graphics processor when Windows ML
   offers one, else the processor), the per-frame or on-demand decision, and every tensor's layout
   live in `Rex.Media.Vision` (portable, 100 % covered). `Rex.Media.Vision.WindowsML` only opens
   sessions and runs them (ADR-0008).

### Licences checked

Each licence was read from the authors' own repository or model page on 2026-10-12.

| Model | Licence of code and weights | Source | Weights as published |
|---|---|---|---|
| Segment Anything (SAM) | Apache-2.0 | [LICENSE](https://github.com/facebookresearch/segment-anything/blob/main/LICENSE) | PyTorch checkpoints from 375 MB (ViT-B); no ONNX encoder |
| SAM 2 and SAM 2.1 (tiny, small) | Apache-2.0, checkpoints included per the README | [LICENSE](https://github.com/facebookresearch/sam2/blob/main/LICENSE), [README](https://github.com/facebookresearch/sam2) | `sam2.1_hiera_tiny.pt` (38.9 M parameters), `sam2.1_hiera_small.pt` (46 M) at dl.fbaipublicfiles.com; no ONNX |
| MobileSAM | Apache-2.0 | [LICENSE](https://github.com/ChaoningZhang/MobileSAM/blob/master/LICENSE) | `weights/mobile_sam.pt`, 40.7 MB; an export script, no published ONNX |
| EfficientSAM | Apache-2.0 | [LICENSE](https://github.com/yformer/EfficientSAM/blob/main/LICENSE) | ONNX encoder and decoder in the repository's `weights` folder (Ti: 24.8 MB and 16.6 MB) |
| EdgeTAM | Apache-2.0, checkpoints included per the README | [LICENSE](https://github.com/facebookresearch/EdgeTAM/blob/main/LICENSE) | `checkpoints/edgetam.pt`, 56.1 MB; Core ML export only, no ONNX |
| EfficientTAM | Apache-2.0, checkpoints included per the README | [LICENSE](https://github.com/yformer/EfficientTAM/blob/main/LICENSE), [weights](https://huggingface.co/yunyangx/efficient-track-anything) | `efficienttam_ti.pt` 71.6 MB, `efficienttam_s.pt` 136 MB; no ONNX |
| MI-GAN | MIT, weights included (`LICENSE-WEIGHTS`) | [LICENSE-WEIGHTS](https://github.com/Picsart-AI-Research/MI-GAN/blob/main/LICENSE-WEIGHTS), [model page](https://huggingface.co/andraniksargsyan/migan) | `migan_pipeline_v2.onnx`, 28.1 MB, published by the first author |
| LaMa | Apache-2.0 | [LICENSE](https://github.com/advimman/lama/blob/main/LICENSE) | `big-lama.zip` (PyTorch) on Hugging Face and a shared drive; no ONNX |
| NanoTrack | Apache-2.0 | [LICENSE](https://github.com/HonglinChu/SiamTrackers/blob/master/NanoTrack/LICENSE) | ONNX backbone and head, about 1 to 2.5 MB together; boxes only, no mask |
| VitTrack | Apache-2.0 | [LICENSE](https://github.com/opencv/opencv_zoo/blob/main/models/object_tracking_vittrack/LICENSE) | `object_tracking_vittrack_2023sep.onnx`; boxes only, no mask |

### Models chosen

- **Click to select: EfficientSAM-Ti** (Xiong et al., CVPR 2024). Apache-2.0; the authors publish
  the ONNX encoder and decoder themselves, so no export is needed and the hash is of their own file;
  41.4 MB together. A point or box prompt gives three candidate masks with predicted quality; the
  encoder runs once per picture and is cached, so further clicks cost only the decoder. SAM and
  SAM 2 are larger with no published ONNX; MobileSAM is as small but only as a PyTorch checkpoint.
- **Removal: MI-GAN 512, Places2** (Sargsyan et al., ICCV 2023). MIT for code and weights; the
  first author publishes a whole ONNX pipeline (crop around the hole, fill at 512, blend back) that
  takes a picture of any size, 28.1 MB. LaMa is larger, has no published ONNX, and its Fourier
  convolutions run poorly through DirectML.
- **Reacquisition: on demand with EfficientSAM-Ti for now.** `PromptedMaskTracker` prompts the
  segmenter with a box around where the subject last was, or around a box the hand-written search
  proposes after a loss, and accepts the answer only when the model is confident and the outline has
  not changed size implausibly. A video model with a memory bank (EdgeTAM, then EfficientTAM-Ti) is
  the later step: neither publishes ONNX, their memory attention must be exported in pieces, and on
  the integrated graphics measured here even the single-picture encoder is far outside a per-frame
  budget (below). When one is exported, it goes behind the same `IMaskTracker` interface, with the
  export script under `scripts/`, its ONNX hashed in the catalogue and hosted from the authors'
  checkpoints under their Apache-2.0 terms.

### Measured

`Rex.Media.Vision.Windows.Tests` on 1080p and 1920 by 808 movie frames, on a 4-core laptop processor
of 2019 with integrated graphics, Windows ML offering `CPUExecutionProvider` and
`DmlExecutionProvider` (the integrated graphics). The machine was fully loaded by other work during
every run, so these are upper bounds; the ratios between devices are the useful part.

| Step | Processor | Graphics (DirectML) |
|---|---|---|
| Image encoder, once per picture | 4.3 to 6.3 s | 4.9 to 7.1 s (first call 9 to 78 s, shader compilation) |
| Prompt decoder, per click | 0.19 to 0.71 s | 0.26 to 0.83 s |
| Fill, per picture | 2.2 to 5.0 s | 2.6 to 5.2 s |

`FrameBudget` therefore rules both models out of per-frame use on such a computer, and the encoder
and fill out of on-demand use too (over two seconds); Subject Lock keeps its own methods there.
On a separate graphics card the same code takes the GPU path first, and the budget is measured
again on every computer before a model is trusted with live video.

Quality, judged from the saved masks and fills: a click on a person's shirt selects the whole
person when the model's whole-object outline is nearly as confident as the part (an office scene,
a driver), otherwise the garment; small or backlit targets (an awning, a face against a window)
give fragments the confidence check rejects. Fills continue walls, ceilings and furniture
convincingly (a patterned chair seat became the table behind it), and large holes come out soft.

## Consequences

Subject Lock can select whole objects and fill removals convincingly on computers whose users choose
the download, with no change for anyone else and no growth of the installer. The project now tracks
two external weight files: a catalogue change is a reviewed change of URL, size and hash, and the
licence table above is re-checked whenever a model is added. Weights are data run by Microsoft's
runtime, not code rexplayer executes, so the clean-room and name rules are unaffected; the authors
are cited by paper.
