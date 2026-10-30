# 2026-10-29: the camera perception models, timed

Capabilities proved: none new; this records the measurements behind ADR-0022's choice of models
and the perception tiers in `DeviceBudget`.

Machine: Intel Core i7-10510U (4 cores, 8 threads), 32 GB, Intel UHD Graphics (driver
31.0.101.2137), Windows 11 Pro 26300, on mains power. Builds and a test run were going on at the
same time, so read these as upper bounds.

Method: `onnxruntime 1.24.4` (the DirectML build), each model opened once per device, one warm-up
run, then thirty runs of random input of the model's own shape; the median and the 90th percentile
of the thirty. The "first" column is the warm-up, which on DirectML includes shader compilation.

| Model (OpenCV zoo file) | Size | Device | First | Median | p90 |
|---|---:|---|---:|---:|---:|
| human_segmentation_pphumanseg_2023mar.onnx | 6.2 MB | DirectML | 29.9 ms | 15.7 ms | 23.8 ms |
| | | Processor | 21.2 ms | 16.0 ms | 20.0 ms |
| palm_detection_mediapipe_2023feb.onnx | 3.9 MB | DirectML | 53.6 ms | 34.4 ms | 40.8 ms |
| | | Processor | 29.9 ms | 25.7 ms | 31.2 ms |
| handpose_estimation_mediapipe_2023feb.onnx | 4.1 MB | DirectML | 678.2 ms | 17.9 ms | 42.3 ms |
| | | Processor | 42.9 ms | 25.4 ms | 36.6 ms |
| person_detection_mediapipe_2023mar.onnx | 12.0 MB | DirectML | 58.8 ms | 31.9 ms | 33.9 ms |
| | | Processor | 32.3 ms | 70.6 ms | 361.6 ms |
| pose_estimation_mediapipe_2023mar.onnx | 5.6 MB | DirectML | 139.2 ms | 61.0 ms | 74.8 ms |
| | | Processor | 31.6 ms | 53.9 ms | 86.1 ms |

Tensors, as the files declare them (NHWC for the MediaPipe models, NCHW for the others):

| Model | Inputs | Outputs |
|---|---|---|
| PPHumanSeg | `x` float [1,3,192,192] | `save_infer_model/scale_0.tmp_1` float [1,2,192,192] |
| Palm detector | `input_1` float [1,192,192,3] | `Identity` [1,2016,18] boxes and palm points, `Identity_1` [1,2016,1] scores |
| Hand landmarks | `input_1` float [1,224,224,3] | `Identity` [1,63] points, `Identity_1` [1,1] score, `Identity_2` [1,1] handedness, `Identity_3` [1,63] world points |
| Person detector | `input_1:0` float [1,3,224,224] | `Identity_1:0` [1,2254,1] scores, `Identity:0` [1,2254,12] boxes and points |
| Pose | `input_1` float [1,256,256,3] | `Identity` [1,195] 39 points by 5, `Identity_1` [1,1] score, `Identity_2` [1,256,256,1] mask, `Identity_3` heatmaps, `Identity_4` [1,117] world points |

How many processor threads a model may use (2026-10-30, the processor only, median of thirty runs,
one thread between operators):

| Model | 1 thread | 2 threads | 4 threads | 8 threads |
|---|---:|---:|---:|---:|
| PPHumanSeg matte | 34.6 ms | 22.7 ms | 19.7 ms | 59.0 ms |
| Palm detector | 32.2 ms | 19.5 ms | 16.2 ms | 36.4 ms |
| Hand landmarks | 13.8 ms | 8.5 ms | 5.2 ms | 11.0 ms |
| Person detector | 33.0 ms | 19.0 ms | 15.1 ms | 31.1 ms |
| Pose | 38.7 ms | 26.4 ms | 19.6 ms | 43.7 ms |

Eight threads on a processor of four cores is the slowest of all (the second thread of each core
fights the first), and four is only about a quarter faster than two. The camera's models therefore
run on two threads: with both hands followed a picture costs about 55 ms (the matte, two hands and
the body every other picture), close to 18 pictures a second, and half the processor stays with the
sound, the decoding and the window. The renderer smooths the points between pictures.

What follows from it: on built-in graphics like these, DirectML is no faster than the processor
for models this small, and the graphics are busy drawing the camera stage, so the models run on the
processor (`DeviceBudget.CameraModelsOnGpu` is true only for a card of its own). This laptop is on
the Best quality tier (the body every other picture); Balanced follows the body every third and
Fastest runs no model. Perception has its own worker below normal priority and always takes the
newest picture, so a slower computer sees fewer pictures, never a slower stage. The person
detector's slow tail on the processor (361 ms at p90 with every thread) is why it runs only until
a body is found and again when it is lost.

Compared with ADR-0019's models on the same class of machine (an image encoder at 4 to 9 seconds),
these are two hundred times cheaper, which is the difference between a tool for a paused picture
and a camera feature.
