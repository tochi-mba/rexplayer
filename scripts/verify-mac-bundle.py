#!/usr/bin/env python3
"""Reject a macOS package whose compatibility metadata or CPU does not match its name."""

from __future__ import annotations

import argparse
import plistlib
import subprocess
import sys
import zipfile
from pathlib import Path


ARCHITECTURES = {
    "osx-arm64": "arm64",
    "osx-x64": "x86_64",
}


def version(value: str) -> tuple[int, ...]:
    try:
        return tuple(int(part) for part in value.split("."))
    except ValueError as error:
        raise ValueError(f"Invalid macOS version: {value}") from error


def fail(message: str) -> None:
    raise SystemExit(message)


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--app", required=True, type=Path)
    parser.add_argument("--rid", required=True, choices=ARCHITECTURES)
    parser.add_argument("--minimum", required=True)
    parser.add_argument("--zip", type=Path)
    args = parser.parse_args()

    info_path = args.app / "Contents" / "Info.plist"
    if not info_path.is_file():
        fail(f"Bundle has no Info.plist: {info_path}")

    with info_path.open("rb") as stream:
        info = plistlib.load(stream)

    actual_minimum = str(info.get("LSMinimumSystemVersion", ""))
    if actual_minimum != args.minimum:
        fail(
            "Bundle minimum macOS version does not match the project: "
            f"expected {args.minimum}, found {actual_minimum or 'nothing'}"
        )
    if version(actual_minimum) > version("15.8.1"):
        fail(f"Bundle requires macOS {actual_minimum}, so it cannot run on macOS 15.8.1")

    executable_name = info.get("CFBundleExecutable")
    if not executable_name:
        fail("Bundle Info.plist has no CFBundleExecutable")
    executable = args.app / "Contents" / "MacOS" / str(executable_name)
    if not executable.is_file():
        fail(f"Bundle executable is missing: {executable}")

    result = subprocess.run(
        ["lipo", "-archs", str(executable)],
        check=True,
        capture_output=True,
        text=True,
    )
    architectures = result.stdout.split()
    expected_architecture = ARCHITECTURES[args.rid]
    if architectures != [expected_architecture]:
        fail(
            f"{args.rid} package must contain one {expected_architecture} executable; "
            f"found {', '.join(architectures) or 'no architecture'}"
        )

    if args.zip:
        if not args.zip.is_file():
            fail(f"Package is missing: {args.zip}")
        expected_prefix = f"{args.app.name}/"
        with zipfile.ZipFile(args.zip) as archive:
            names = archive.namelist()
        info_members = [name for name in names if name.endswith(".app/Contents/Info.plist")]
        if info_members != [f"{expected_prefix}Contents/Info.plist"]:
            fail(f"Package must contain exactly {expected_prefix}Contents/Info.plist")
        executable_member = f"{expected_prefix}Contents/MacOS/{executable_name}"
        if executable_member not in names:
            fail(f"Package does not contain {executable_member}")

    print(
        f"Verified {args.app.name}: macOS {actual_minimum}+; "
        f"{expected_architecture}; package layout is valid."
    )


if __name__ == "__main__":
    try:
        main()
    except (
        OSError,
        plistlib.InvalidFileException,
        subprocess.CalledProcessError,
        zipfile.BadZipFile,
    ) as error:
        print(f"Mac bundle verification failed: {error}", file=sys.stderr)
        raise SystemExit(1) from error
