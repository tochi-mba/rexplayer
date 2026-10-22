#!/usr/bin/env python3
"""Combine matching arm64 and x86_64 app bundles into one universal app."""

from __future__ import annotations

import argparse
import os
import shutil
import subprocess
import tempfile
from pathlib import Path


def architectures(path: Path) -> set[str] | None:
    result = subprocess.run(
        ["lipo", "-archs", str(path)],
        capture_output=True,
        text=True,
    )
    return set(result.stdout.split()) if result.returncode == 0 else None


def fail(message: str) -> None:
    raise SystemExit(message)


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--arm64", required=True, type=Path)
    parser.add_argument("--x64", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    args = parser.parse_args()

    for app in (args.arm64, args.x64):
        if not (app / "Contents" / "Info.plist").is_file():
            fail(f"Not an app bundle: {app}")
    if args.output.exists():
        fail(f"Output already exists: {args.output}")

    shutil.copytree(args.arm64, args.output, symlinks=True)
    merged: set[Path] = set()

    for arm_path in args.arm64.rglob("*"):
        if not arm_path.is_file() or arm_path.is_symlink():
            continue
        arm_architectures = architectures(arm_path)
        if arm_architectures is None:
            continue
        relative = arm_path.relative_to(args.arm64)
        x64_path = args.x64 / relative
        if not x64_path.is_file() or architectures(x64_path) is None:
            fail(f"The x86_64 bundle has no matching Mach-O file: {relative}")
        output_path = args.output / relative
        with tempfile.NamedTemporaryFile(dir=output_path.parent, delete=False) as temporary:
            temporary_path = Path(temporary.name)
        try:
            subprocess.run(
                ["lipo", "-create", str(arm_path), str(x64_path), "-output", str(temporary_path)],
                check=True,
            )
            os.chmod(temporary_path, arm_path.stat().st_mode)
            temporary_path.replace(output_path)
        finally:
            temporary_path.unlink(missing_ok=True)
        if architectures(output_path) != {"arm64", "x86_64"}:
            fail(f"Universal merge failed: {relative}")
        merged.add(relative)

    for x64_path in args.x64.rglob("*"):
        if not x64_path.is_file() or x64_path.is_symlink() or architectures(x64_path) is None:
            continue
        relative = x64_path.relative_to(args.x64)
        if relative not in merged:
            fail(f"The arm64 bundle has no matching Mach-O file: {relative}")

    if not merged:
        fail("No Mach-O files were found in the app bundles")
    print(f"Created {args.output} with {len(merged)} universal Mach-O files.")


if __name__ == "__main__":
    main()
