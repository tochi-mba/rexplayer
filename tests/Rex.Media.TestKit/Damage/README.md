# Damaged-file corpus

Builders here take a healthy file made by the TestKit builders and damage it the way real files are
damaged: truncated downloads, flipped bits, zeroed or garbage runs, lost headers, broken boxes, elements,
pages, blocks and frames. `DamagedFileTests` plays every damaged file and asserts that the player never
crashes, never hangs, keeps its memory bounded and salvages a minimum share of the healthy duration.
