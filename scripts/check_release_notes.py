#!/usr/bin/env python3
"""Fail releases whose source version, top changelog heading and README disagree.

This is intentionally independent of the GitHub release API: a draft PR can
prove its proposed release notes are ready without publishing a version.
"""

from __future__ import annotations

import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
VERSION_PATTERN = re.compile(r"<Version>(\d+\.\d+\.\d+)</Version>")
HEADING_PATTERN = re.compile(r"^## (\d+\.\d+\.\d+) - \d{4}-\d{2}-\d{2}$", re.MULTILINE)


def check(root: Path = ROOT) -> list[str]:
    problems: list[str] = []
    props = (root / "Directory.Build.props").read_text(encoding="utf-8")
    changelog = (root / "CHANGELOG.md").read_text(encoding="utf-8")
    readme = (root / "README.md").read_text(encoding="utf-8")
    match = VERSION_PATTERN.search(props)
    if match is None:
        return ["Directory.Build.props needs one semantic <Version>X.Y.Z</Version>."]
    version = match.group(1)
    if len(VERSION_PATTERN.findall(props)) != 1:
        problems.append("Directory.Build.props must declare one project version.")

    headings = list(HEADING_PATTERN.finditer(changelog))
    if not headings:
        problems.append("CHANGELOG.md has no dated semantic version headings.")
    elif headings[0].group(1) != version:
        problems.append(f"CHANGELOG.md begins with {headings[0].group(1)}, not source version {version}.")
    if sum(heading.group(1) == version for heading in headings) != 1:
        problems.append(f"CHANGELOG.md must contain exactly one release entry for {version}.")
    if f"**Source version: {version}.**" not in readme:
        problems.append(f"README.md does not identify source version {version}.")
    return problems


def main() -> int:
    problems = check()
    if problems:
        for problem in problems:
            print(problem, file=sys.stderr)
        return 1
    print("release version and notes match")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
