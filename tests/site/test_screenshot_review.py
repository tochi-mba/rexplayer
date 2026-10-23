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
