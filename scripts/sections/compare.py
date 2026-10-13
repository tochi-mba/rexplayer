"""Compares `rexplay sections <file> --json` results with the hand-checked times.

    rexplay sections <episode> --json > <folder>/<anything>.json      (once per episode)
    python scripts/sections/compare.py <folder>

Matches each result to tests/Rex.Media.Windows.Tests/Fixtures/episode-sections.json by show word and
SxxEyy in the file name, and prints one row per episode: what was offered, its confidence and the
error in seconds against the fixture. Offered means confidence at or above the offer threshold.
"""
import glob
import json
import os
import re
import sys

FIXTURE = os.path.join(os.path.dirname(__file__), "..", "..", "tests", "Rex.Media.Windows.Tests", "Fixtures", "episode-sections.json")


def main():
    with open(FIXTURE, encoding="utf-8") as handle:
        fixture = json.load(handle)
    print("| Episode | Intro offered | Credits offered | Confidence | Start error | End error |")
    print("|---|---|---|---|---|---|")
    for path in sorted(glob.glob(os.path.join(sys.argv[1], "*.json"))):
        with open(path, encoding="utf-8-sig") as handle:
            document = json.load(handle)
        data = document["data"]
        name = os.path.basename(data["file"])
        episode = re.search(r"(?i)s\d\de\d\d", name).group(0).upper()
        expected = next((e for e in fixture["episodes"] if e["show"].lower() in name.lower() and e["episode"] == episode), None)
        if expected is None:
            continue
        offered = [p for p in data["proposals"] if p["offered"]]
        intro = next((p for p in offered if p["kind"] == "intro"), None)
        credits = next((p for p in offered if p["kind"] == "credits"), None)
        intro_text = "none" if intro is None else "%.2f to %.2f" % (intro["start"], intro["end"])
        if credits is None:
            print("| %s %s | %s | none | | | |" % (expected["show"], episode, intro_text))
            continue
        print("| %s %s | %s | %.2f to %.2f | %.3f | %+.2f | %+.2f |" % (
            expected["show"], episode, intro_text, credits["start"], credits["end"], credits["confidence"],
            credits["start"] - expected["creditsStart"], credits["end"] - expected["creditsEnd"]))


if __name__ == "__main__":
    main()
