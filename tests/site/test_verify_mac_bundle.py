"""Tests for the macOS release-bundle signature parser, runnable on any CI OS."""

from __future__ import annotations

import importlib.util
import plistlib
import unittest
from pathlib import Path


SCRIPT = Path(__file__).resolve().parents[2] / "scripts" / "verify-mac-bundle.py"
SPEC = importlib.util.spec_from_file_location("verify_mac_bundle", SCRIPT)
assert SPEC is not None and SPEC.loader is not None
verify = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(verify)

JIT = "com.apple.security.cs.allow-jit"


class MacBundleSignatureTests(unittest.TestCase):
    def setUp(self) -> None:
        self.allowed = plistlib.dumps({JIT: True})
        self.disallowed = plistlib.dumps({JIT: False})

    def test_codesign_xml_on_stdout_with_diagnostics_on_stderr(self) -> None:
        # codesign's diagnostic messages must never be appended to a valid XML plist.
        stdout = self.allowed
        stderr = b"Executable=/Applications/rexplayer.app/Contents/MacOS/rexplayer\n"
        self.assertEqual({JIT: True}, verify.signed_entitlements(stdout, stderr))

    def test_codesign_xml_on_stderr_with_diagnostics_before_and_after(self) -> None:
        stderr = b"Executable=rexplayer\n" + self.allowed + b"\nformat=app bundle\n"
        self.assertEqual({JIT: True}, verify.signed_entitlements(b"", stderr))

    def test_codesign_xml_with_extra_text_on_the_same_stream(self) -> None:
        stdout = b"Ad-hoc signature detected\n" + self.allowed + b"\nRuntime warnings\n"
        self.assertEqual({JIT: True}, verify.signed_entitlements(stdout, b""))

    def test_signed_entitlements_reject_missing_or_broken_plists(self) -> None:
        cases = [
            (b"", b""),
            (b"", b"Executable=app"),
            (b"<?xml version='1.0'?><plist>not closed", b""),
            (b"<?xml version='1.0'?><plist>not valid</plist>", b""),
            (b"", plistlib.dumps(["not a dictionary"])),
        ]
        for stdout, stderr in cases:
            with self.subTest(stdout=stdout, stderr=stderr):
                with self.assertRaisesRegex(SystemExit, "no readable entitlements plist"):
                    verify.signed_entitlements(stdout, stderr)

    def test_a_false_jit_entitlement_is_not_mistaken_for_true(self) -> None:
        self.assertIs(verify.signed_entitlements(self.disallowed, b"")[JIT], False)

    def test_codesign_can_output_xml_without_an_xml_declaration(self) -> None:
        start = self.allowed.index(b"<plist")
        self.assertEqual({JIT: True}, verify.signed_entitlements(self.allowed[start:], b""))


if __name__ == "__main__":
    unittest.main()
