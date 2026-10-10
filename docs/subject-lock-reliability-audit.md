# Subject Lock reliability audit

Scope: user-selected local video region tracking, global recovery when the region disappears,
and non-destructive removal preview. This audit does **not** establish semantic person or
object identity; identical appearances remain indistinguishable.

## Architecture and failure policy

The fast local matcher updates a small template only while tracking is confident. A separate,
periodic multi-scale full-frame search preserves the original selection's texture and requires
repeat sightings before resuming. Recovery never enables removal automatically, and a lost or
ambiguous target must leave the original picture available.

The global re-detection path is kept in `SubjectEditSession.Reacquisition.cs`; regular
frame-by-frame matching, preview, and state transitions remain in `SubjectEditSession.cs`.
This matches the separation between local tracking and recovery recommended in long-term
tracking literature:

- [Performance Evaluation Methodology for Long-Term Visual Object Tracking](https://arxiv.org/abs/1906.08675)
  assesses disappearances, re-detection, and tracking failure separately.
- [Re-detection and distractor association from a global perspective (2023)](https://doi.org/10.1016/j.compeleceng.2023.108611)
  distinguishes the local tracker, global search, and distractor handling.
- [The sixth Visual Object Tracking challenge (2018)](https://openaccess.thecvf.com/content_ECCVW_2018/papers/11129/Kristan_The_sixth_Visual_Object_Tracking_VOT2018_challenge_results_ECCVW_2018_paper.pdf)
  includes long sequences with multiple disappearances, occlusions, and out-of-view cases.

These references motivate evaluation and safety policy, not a claim that rexplayer implements
the papers' feature extractors, detectors, or machine-learning methods.

## Reproducible regression coverage

The Windows adapter tests in `SubjectEditSessionTests.cs` and
`SubjectEditSessionSafetyTests.cs` cover:

| Scenario | Required result |
| --- | --- |
| Initially textured user selection | Lock on the original visible region |
| Moderate frame-to-frame motion | Follow within a bounded displacement |
| Recovery after target exits the picture | Preserve selection and verify repeat sightings |
| Return at 0.5–2.0 times initial apparent size | Reacquire when appearance remains distinctive |
| Horizontal/vertical return displacement and corners | Correct approximate original region |
| Brightness changes | Allow bounded local colour differences, not identity changes |
| Many frames of occlusion | Keep original texture and preview disabled |
| One fleeting return | Do not resume without sufficient confirmation |
| Two nearly identical targets | Do not silently choose a lookalike |
| Missing subject or featureless video | Never report a confident recovered identity |
| Rejected featureless initial selection | Require explicit new user selection, even if a new object arrives |
| Changed decoder dimensions or oversized frame | Clear unsafe identity and preview state |
| Invalid decoded picture format | Reject without corrupting the selected state |
| GPU presenter redraw and return | Recover and restore original media through D3D11 path |
| Manual reset/reselect and seek | Forget stale reference and media timeline |

The smallest-distance edge cases are represented with deterministic synthetic decoded
pictures; these tests deliberately verify both successful re-detection and abstention.

## Residual limitations and release criteria

Matching visible colour and a small texture patch cannot identify a person across extreme
pose changes, motion blur, severe occlusion, identical clothing, camera cuts, or long
appearance changes. The tracker is bounded and local-only; it does not upload frames or
edit the source file. Never turn a failed re-detection into an unconditional lock.

**Release verification:** run Windows adapter tests, desktop UI Automation, performance
baselines, portable/Linux tests, the quality gate, package/update tests, and site checks.
Do not bump the release just because the synthetic suite is green if the real UI
or performance checks are failing. A real-world annotated clip benchmark measuring
success rate, false reacquisition rate, latency, and runtime at HD/UHD resolutions is
still needed for claims about broad real-video reliability.
