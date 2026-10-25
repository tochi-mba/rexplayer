"""A release gallery never quietly substitutes CI/synthetic photos for actual builds."""

from __future__ import annotations

import hashlib
import json
import sys
import tempfile
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "scripts"))
import check_release_media  # noqa: E402


class ReleaseMediaTests(unittest.TestCase):
    def fixture(self) -> tuple[Path, dict]:
        root = Path(tempfile.mkdtemp())
        (root / "Directory.Build.props").write_text(
            "<Project><Version>0.30.8</Version></Project>", encoding="utf-8",
        )
        folder = root / "site" / "assets"
        folder.mkdir(parents=True)
        file = folder / "windows-now-playing-release.png"
        file.write_bytes(b"native capture represented by fixture bytes")
        entry = {
            "file": file.name,
            "platform": "windows",
            "version": "0.30.8",
            "operatingSystem": "Windows 11",
            "screen": "Now playing",
            "captureMethod": "native-finalized-build",
            "reviewer": "Visual reviewer",
            "reviewDate": "2026-10-24",
            "sha256": hashlib.sha256(file.read_bytes()).hexdigest(),
        }
        manifest = {"version": "0.30.8", "captures": [entry]}
        (folder / "release-media.json").write_text(json.dumps(manifest), encoding="utf-8")
        return root, manifest

    def test_the_current_fixture_only_gallery_is_not_misrepresented(self) -> None:
        self.assertEqual([], check_release_media.check())

    def test_valid_platform_version_and_digest_evidence_pass_source_validation(self) -> None:
        root, _ = self.fixture()
        self.assertEqual([], check_release_media.check(root))

    def test_unregistered_native_release_images_are_refused(self) -> None:
        root, _ = self.fixture()
        (root / "site" / "assets" / "release-media.json").unlink()
        self.assertTrue(check_release_media.check(root))

    def test_mislabeled_synthetic_and_changed_images_are_refused(self) -> None:
        root, manifest = self.fixture()
        manifest["captures"][0]["captureMethod"] = "ci-fixture"
        (root / "site" / "assets" / "release-media.json").write_text(
            json.dumps(manifest), encoding="utf-8",
        )
        self.assertTrue(any("test/synthetic" in message for message in check_release_media.check(root)))
        manifest["captures"][0]["captureMethod"] = "native-finalized-build"
        manifest["captures"][0]["platform"] = "macos"
        (root / "site" / "assets" / "release-media.json").write_text(
            json.dumps(manifest), encoding="utf-8",
        )
        self.assertTrue(any("platform label" in message for message in check_release_media.check(root)))
        manifest["captures"][0]["platform"] = "windows"
        screenshot = root / "site" / "assets" / "windows-now-playing-release.png"
        screenshot.write_bytes(b"changed after review")
        (root / "site" / "assets" / "release-media.json").write_text(
            json.dumps(manifest), encoding="utf-8",
        )
        self.assertTrue(any("changed after" in message for message in check_release_media.check(root)))

    def test_duplicate_missing_and_stale_versions_are_refused(self) -> None:
        root, manifest = self.fixture()
        manifest["version"] = "0.30.7"
        manifest["captures"][0]["version"] = "0.30.7"
        manifest["captures"].append(dict(manifest["captures"][0]))
        (root / "site" / "assets" / "release-media.json").write_text(
            json.dumps(manifest), encoding="utf-8",
        )
        problems = check_release_media.check(root)
        self.assertTrue(any("must equal" in issue for issue in problems))
        self.assertTrue(any("Duplicate" in issue for issue in problems))
        self.assertTrue(any("recorded build version" in issue for issue in problems))


if __name__ == "__main__":
    unittest.main()
