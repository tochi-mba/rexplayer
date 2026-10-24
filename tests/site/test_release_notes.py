"""The release version must be accompanied by accurate, matching release notes."""

from __future__ import annotations

import sys
import tempfile
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "scripts"))
import check_release_notes  # noqa: E402


class ReleaseNotesTests(unittest.TestCase):
    def fixture(self, version: str = "0.30.8", notes: str = "0.30.8",
                readme: str = "0.30.8") -> Path:
        root = Path(tempfile.mkdtemp())
        (root / "Directory.Build.props").write_text(
            f"<Project><PropertyGroup><Version>{version}</Version></PropertyGroup></Project>",
            encoding="utf-8",
        )
        (root / "CHANGELOG.md").write_text(
            f"# Changelog\\n\\n## {notes} - 2026-10-24\\n\\n### Fixed\\n\\n- Local recovery.\\n",
            encoding="utf-8",
        )
        (root / "README.md").write_text(f"> **Source version: {readme}.**\\n", encoding="utf-8")
        return root

    def test_the_actual_pr_version_changelog_and_readme_match(self) -> None:
        self.assertEqual([], check_release_notes.check())

    def test_matching_version_release_heading_and_readme_pass(self) -> None:
        self.assertEqual([], check_release_notes.check(self.fixture()))

    def test_missing_or_conflicting_release_entry_is_an_error(self) -> None:
        self.assertTrue(check_release_notes.check(self.fixture(notes="0.30.7")))
        self.assertTrue(check_release_notes.check(self.fixture(readme="0.30.7")))
        self.assertTrue(check_release_notes.check(self.fixture(version="invalid")))

    def test_duplicate_release_heading_is_an_error(self) -> None:
        root = self.fixture()
        changelog = root / "CHANGELOG.md"
        changelog.write_text(changelog.read_text(encoding="utf-8") +
                             "\\n## 0.30.8 - 2026-10-24\\n", encoding="utf-8")
        self.assertIn("CHANGELOG.md must contain exactly one release entry for 0.30.8.",
                      check_release_notes.check(root))


if __name__ == "__main__":
    unittest.main()
