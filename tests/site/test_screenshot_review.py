"""Portable contract tests for the Windows screenshot review index generator."""

import json
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path


SCRIPT = Path(__file__).resolve().parents[2] / "scripts" / "build-screenshot-review.py"


class ScreenshotReviewTests(unittest.TestCase):
    def test_review_index_displays_semantic_oracles_and_all_image_variants(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            inventory = root / "scenes.json"
            scene = {
                "id": "sample-songs",
                "fixture": "offline-song",
                "route": "library/songs",
                "state": "long-text",
                "dimensions": "1000x760",
                "theme": "light",
                "requiredLane": "ci-windows",
                "expectedAutomationIds": ["LibraryPane", "LibraryTitle"],
                "expectedVisibleData": [
                    {"automationId": "LibraryTitle", "name": "Songs & <More>"}
                ],
            }
            inventory.write_text(json.dumps({"scenes": [scene]}), encoding="utf-8")
            output = root / "artifacts" / "review" / "index.html"
            for folder in ("actual", "window", "repeat", "diff"):
                image = root / "artifacts" / "ci-windows" / folder / "sample-songs.png"
                image.parent.mkdir(parents=True, exist_ok=True)
                image.write_bytes(b"\x89PNG\r\n\x1a\n")

            metadata = root / "artifacts" / "ci-windows" / "actual" / "sample-songs.json"
            metadata.write_text(
                json.dumps(
                    {
                        "sceneId": "sample-songs",
                        "readinessAchieved": True,
                        "pixelReadinessAchieved": True,
                        "repeatabilityAchieved": True,
                        "measuredDpi": 96,
                        "actualHighContrast": False,
                    }
                ),
                encoding="utf-8",
            )
            subprocess.run(
                [
                    sys.executable,
                    str(SCRIPT),
                    "--inventory",
                    str(inventory),
                    "--root",
                    str(root / "artifacts"),
                    "--output",
                    str(output),
                ],
                check=True,
                capture_output=True,
                text=True,
            )
            page = output.read_text(encoding="utf-8")
            self.assertIn("sample-songs", page)
            self.assertIn("offline-song", page)
            self.assertIn("Songs &amp; &lt;More&gt;", page)
            self.assertIn("ci-windows/window/sample-songs.png", page)
            self.assertIn("ci-windows/repeat/sample-songs.png", page)
            self.assertNotIn("MISSING CURRENT IMAGE", page)

    def test_stage_review_displays_escaped_capability_and_focus_witnesses(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            inventory = root / "scenes.json"
            inventory.write_text(json.dumps({"scenes": [{
                "id": "stage-audio-compact",
                "fixture": "direct-audio",
                "route": "player/audio",
                "state": "paused",
                "dimensions": "720x540",
                "theme": "dark",
                "requiredLane": "ci-windows",
                "baseline": "unreviewed",
                "expectedAutomationIds": ["Stage"],
                "expectedVisibleData": [],
            }]}), encoding="utf-8")
            actual = root / "artifacts" / "ci-windows" / "actual"
            actual.mkdir(parents=True)
            (actual / "stage-audio-compact.json").write_text(json.dumps({
                "observedStageAffordances": [
                    {"automationId": "Stage", "shown": True, "IsEnabled": True,
                     "HasKeyboardFocus": False, "Name": "AUDIO playback stage",
                     "HelpText": "Press <Enter> & seek"},
                    {"automationId": "StageChaptersButton", "shown": False,
                     "isEnabled": False, "hasKeyboardFocus": False},
                ],
            }), encoding="utf-8")
            output = root / "artifacts" / "review" / "index.html"
            subprocess.run([
                sys.executable, str(SCRIPT), "--inventory", str(inventory),
                "--root", str(root / "artifacts"), "--output", str(output),
            ], check=True, capture_output=True, text=True)
            page = output.read_text(encoding="utf-8")
            self.assertIn("Declared baseline state", page)
            self.assertIn("unreviewed", page)
            self.assertIn("Baseline file witness", page)
            self.assertIn("not captured", page)
            self.assertIn("Observed stage UI Automation", page)
            self.assertIn("Press &lt;Enter&gt; &amp; seek", page)
            self.assertIn("StageChaptersButton: visible=False, enabled=False", page)
            self.assertIn("AUDIO playback stage", page)

    def test_wrong_scene_or_accessible_text_is_never_reported_as_matching(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            inventory = root / "scenes.json"
            inventory.write_text(json.dumps({"scenes": [{
                "id": "stage-audio-windowed",
                "fixture": "sound",
                "route": "player/audio",
                "state": "paused",
                "dimensions": "1000x720",
                "theme": "dark",
                "requiredLane": "ci-windows",
                "expectedAutomationIds": ["Stage"],
                "expectedVisibleData": [{
                    "automationId": "Stage", "name": "AUDIO playback stage"
                }],
            }]}), encoding="utf-8")
            actual = root / "artifacts" / "ci-windows" / "actual"
            actual.mkdir(parents=True)
            evidence = {
                "sceneId": "stage-movie-windowed",
                "observedVisibleData": [{
                    "automationId": "Stage", "shown": True,
                    "Name": "MOVIE playback stage",
                }],
            }
            (actual / "stage-audio-windowed.json").write_text(
                json.dumps(evidence), encoding="utf-8")
            output = root / "artifacts" / "review" / "index.html"
            subprocess.run([
                sys.executable, str(SCRIPT), "--inventory", str(inventory),
                "--root", str(root / "artifacts"), "--output", str(output),
            ], check=True, capture_output=True, text=True)
            page = output.read_text(encoding="utf-8")
            self.assertIn("MISMATCH: metadata belongs to", page)
            self.assertIn("MISMATCH: Stage: expected", page)
            self.assertIn("MOVIE playback stage", page)

            evidence["sceneId"] = "stage-audio-windowed"
            evidence["observedVisibleData"][0]["Name"] = "AUDIO playback stage"
            (actual / "stage-audio-windowed.json").write_text(
                json.dumps(evidence), encoding="utf-8")
            subprocess.run([
                sys.executable, str(SCRIPT), "--inventory", str(inventory),
                "--root", str(root / "artifacts"), "--output", str(output),
            ], check=True, capture_output=True, text=True)
            page = output.read_text(encoding="utf-8")
            self.assertNotIn("MISMATCH", page)
            self.assertIn("Semantic text witness</dt><dd>MATCH", page)

    def test_missing_evidence_is_visible_but_specialist_lane_is_not_misrepresented(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            inventory = root / "scenes.json"
            inventory.write_text(
                json.dumps(
                    {
                        "scenes": [
                            {
                                "id": "high-dpi",
                                "fixture": "offline",
                                "route": "library/songs",
                                "state": "dpi",
                                "dimensions": "1000x760",
                                "theme": "system",
                                "requiredLane": "ci-windows-high-dpi",
                                "expectedAutomationIds": ["LibraryPane"],
                                "expectedVisibleData": [],
                            }
                        ]
                    }
                ),
                encoding="utf-8",
            )
            output = root / "artifacts" / "review" / "index.html"
            subprocess.run(
                [
                    sys.executable,
                    str(SCRIPT),
                    "--inventory",
                    str(inventory),
                    "--root",
                    str(root / "artifacts"),
                    "--output",
                    str(output),
                ],
                check=True,
                capture_output=True,
                text=True,
            )
            page = output.read_text(encoding="utf-8")
            self.assertIn("ci-windows-high-dpi", page)
            self.assertIn("MISSING CURRENT IMAGE", page)
            self.assertIn("not captured", page)


if __name__ == "__main__":
    unittest.main()
