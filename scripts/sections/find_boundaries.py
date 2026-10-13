"""Reads FrameSheet's frames.csv and prints the boundaries a window holds, to the frame.

    python scripts/sections/find_boundaries.py <frames-folder>...

For each folder: every change between a picture and black (dark fraction 0.85 or more), the last
picture of the scene before the final black run, and the first picture after it with any bright
pixel, which is where credit text appears.
"""
import csv
import os
import sys


def main():
    for folder in sys.argv[1:]:
        with open(os.path.join(folder, "frames.csv"), encoding="utf-8") as handle:
            rows = [(float(r["t"]), float(r["dark"]), float(r["bright"]), float(r["mean"])) for r in csv.DictReader(handle)]
        changes = []
        previous = None
        for t, dark, bright, mean in rows:
            black = dark >= 0.85
            if previous is not None and black != previous:
                changes.append("%s at %.3f (mean %.1f)" % ("to black" if black else "to picture", t, mean))
            previous = black
        scene = max((t for t, dark, _, _ in rows if dark < 0.85), default=None)
        after = [row for row in rows if scene is None or row[0] > scene]
        text = next((t for t, _, bright, _ in after if bright > 0.0005), None)
        print("%s: %s | last scene picture %s, first text %s" % (
            os.path.basename(folder.rstrip("\\/")), "; ".join(changes) or "no change", scene, text))


if __name__ == "__main__":
    main()
