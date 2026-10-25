#!/usr/bin/env python3
"""Validate release screenshot provenance before CI publishes a promoted gallery.

UI-test fixtures remain honestly labeled as fixtures. Final native release
captions must carry versioned, hash-bound review evidence; a manifest cannot
replace the human viewing the actual PNG on both platforms.
"""

from __future__ import annotations

import hashlib
import json
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
RELEASE_NAME = re.compile(r"^(windows|macos)-[a-z0-9-]+-release\.png$")


def check(root: Path = ROOT) -> list[str]:
    problems: list[str] = []
    assets = root / "site" / "assets"
    manifest_file = assets / "release-media.json"
    release_images = {f.name for f in assets.glob("*-release.png")}
    if not manifest_file.exists():
        return (["Final native release screenshot files require assets/release-media.json."]
                if release_images else [])

    try:
        manifest = json.loads(manifest_file.read_text(encoding="utf-8"))
    except (OSError, ValueError) as exc:
        return [f"Invalid release media manifest: {exc}."]
    if not isinstance(manifest, dict) or not isinstance(manifest.get("captures"), list):
        return ["Release media manifest requires a captures array."]

    props = (root / "Directory.Build.props").read_text(encoding="utf-8")
    version = re.search(r"<Version>(\d+\.\d+\.\d+)</Version>", props)
    current = version.group(1) if version else ""
    if not current or manifest.get("version") != current:
        problems.append("Release media manifest version must equal the source build version.")

    seen: set[str] = set()
    for capture in manifest["captures"]:
        if not isinstance(capture, dict):
            problems.append("Every release media capture requires an evidence object.")
            continue
        name = capture.get("file")
        if not isinstance(name, str) or not RELEASE_NAME.fullmatch(name):
            problems.append("A release capture must be a platform-named *-release.png.")
            continue
        if name in seen:
            problems.append(f"Duplicate release screenshot: {name}.")
        seen.add(name)
        prefix = name.split("-", 1)[0]
        if capture.get("platform") != prefix:
            problems.append(f"{name}: platform label must match its filename.")
        if capture.get("version") != current:
            problems.append(f"{name}: recorded build version is not the source version.")
        for field in ("operatingSystem", "screen", "captureMethod", "reviewer", "reviewDate"):
            if not isinstance(capture.get(field), str) or not capture[field].strip():
                problems.append(f"{name}: missing {field} provenance.")
        if capture.get("captureMethod") != "native-finalized-build":
            problems.append(f"{name}: test/synthetic captures cannot be promoted to release media.")
        path = assets / name
        if not path.is_file():
            problems.append(f"{name}: screenshot is missing.")
            continue
        digest = hashlib.sha256(path.read_bytes()).hexdigest()
        if capture.get("sha256") != digest:
            problems.append(f"{name}: screenshot content changed after provenance approval.")
    if release_images != seen:
        problems.append("Every *-release.png screenshot must have exactly one manifest entry.")
    return problems


def main() -> int:
    errors = check()
    if errors:
        for error in errors:
            print(error, file=sys.stderr)
        return 1
    print("release media provenance check passed (human review still required)")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
