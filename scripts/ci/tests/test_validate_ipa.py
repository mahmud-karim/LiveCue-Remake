import contextlib
import io
import os
import plistlib
import struct
import sys
import tempfile
import unittest
import zipfile

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))

import validate_ipa  # noqa: E402

ARM64 = 0x0100000C
X86_64 = 0x01000007
IOS = 2
IOS_SIMULATOR = 7


def load_command(command, payload):
    padding = (-len(payload)) % 8
    return struct.pack("<II", command, 8 + len(payload) + padding) + payload + b"\0" * padding


def dylib(path):
    return load_command(0x0C, struct.pack("<4I", 24, 0, 0, 0) + path.encode() + b"\0")


def segment(name, sections):
    header = name.encode().ljust(16, b"\0") + struct.pack("<4Q4I", 0, 0, 0, 0, 7, 5, len(sections), 0)
    records = b"".join(
        section.encode().ljust(16, b"\0") + name.encode().ljust(16, b"\0") + b"\0" * 48 for section in sections
    )
    return load_command(0x19, header + records)


def macho(
    cpu_type=ARM64,
    file_type=2,
    platform=IOS,
    dylibs=("/usr/lib/libSystem.B.dylib",),
    crypt_id=None,
    data_sections=("__data", "__bss"),
):
    commands = [
        segment("__TEXT", ["__text", "__cstring"]),
        segment("__DATA", list(data_sections)),
        load_command(0x32, struct.pack("<4I", platform, 18 << 16, 26 << 16 | 2 << 8, 0)),
    ]
    commands += [dylib(path) for path in dylibs]
    if crypt_id is not None:
        commands.append(load_command(0x2C, struct.pack("<4I", 0x4000, 0x1000, crypt_id, 0)))
    body = b"".join(commands)
    return struct.pack("<8I", 0xFEEDFACF, cpu_type, 0, file_type, len(commands), len(body), 0, 0) + body


def universal(*slices):
    header = struct.pack(">II", 0xCAFEBABE, len(slices))
    offset = 4096
    entries, blobs = b"", b""
    for data in slices:
        cpu_type = struct.unpack_from("<I", data, 4)[0]
        entries += struct.pack(">5I", cpu_type, 0, offset + len(blobs), len(data), 0)
        blobs += data
    return (header + entries).ljust(offset, b"\0") + blobs


DEFAULT_INFO = {
    "CFBundleIdentifier": "com.example.App",
    "CFBundleExecutable": "App",
    "CFBundleShortVersionString": "1.0.0",
    "CFBundleVersion": "1",
    "MinimumOSVersion": "18.0",
    "UIDeviceFamily": [1],
}


class ValidateIPATests(unittest.TestCase):
    def setUp(self):
        self.folder = tempfile.TemporaryDirectory()
        self.addCleanup(self.folder.cleanup)

    def make_ipa(self, info=None, executable=None, extra=(), executable_mode=0o755, app="App.app"):
        path = os.path.join(self.folder.name, "App.ipa")
        root = f"Payload/{app}/"
        with zipfile.ZipFile(path, "w") as archive:
            archive.writestr(root + "Info.plist", plistlib.dumps(DEFAULT_INFO if info is None else info, fmt=plistlib.FMT_BINARY))
            entry = zipfile.ZipInfo(root + "App")
            entry.create_system = 3
            entry.external_attr = (0o100000 | executable_mode) << 16
            archive.writestr(entry, macho() if executable is None else executable)
            for name, data in extra:
                archive.writestr(name, data)
        return path

    def run_validator(self, path, *options):
        arguments = [path, "--bundle-id", "com.example.App", "--version", "1.0.0", "--build", "1",
                     "--minimum-os", "18.0", "--device-family", "1", *options]
        output = io.StringIO()
        with contextlib.redirect_stdout(output):
            status = validate_ipa.main(arguments)
        return status, output.getvalue()

    def assert_rejected(self, path, reason):
        status, output = self.run_validator(path)
        self.assertEqual(status, 1, output)
        self.assertIn(reason, output)

    def test_accepts_a_release_device_build(self):
        status, output = self.run_validator(self.make_ipa())
        self.assertEqual(status, 0, output)
        self.assertIn("SHA-256 ", output)

    def test_accepts_a_universal_binary_with_an_arm64_slice(self):
        status, output = self.run_validator(self.make_ipa(executable=universal(macho(cpu_type=X86_64), macho())))
        self.assertEqual(status, 0, output)

    def test_rejects_a_file_that_is_not_a_zip(self):
        path = os.path.join(self.folder.name, "broken.ipa")
        with open(path, "wb") as handle:
            handle.write(b"not a zip archive")
        self.assert_rejected(path, "cannot open the archive")

    def test_rejects_the_wrong_identifier_version_and_build(self):
        info = dict(DEFAULT_INFO, CFBundleIdentifier="com.example.Other", CFBundleShortVersionString="0.9.0", CFBundleVersion="7")
        status, output = self.run_validator(self.make_ipa(info=info))
        self.assertEqual(status, 1)
        for reason in ("bundle identifier is 'com.example.Other'", "version is '0.9.0'", "build is '7'"):
            self.assertIn(reason, output)

    def test_rejects_the_wrong_minimum_os_and_device_family(self):
        info = dict(DEFAULT_INFO, MinimumOSVersion="26.0", UIDeviceFamily=[1, 2])
        status, output = self.run_validator(self.make_ipa(info=info))
        self.assertEqual(status, 1)
        self.assertIn("minimum iOS version is '26.0'", output)
        self.assertIn("device family is [1, 2]", output)

    def test_rejects_a_simulator_build(self):
        self.assert_rejected(self.make_ipa(executable=macho(platform=IOS_SIMULATOR)), "built for iOS simulator")

    def test_rejects_a_binary_without_an_arm64_slice(self):
        self.assert_rejected(self.make_ipa(executable=macho(cpu_type=X86_64)), "no arm64 slice")

    def test_rejects_an_encrypted_binary(self):
        self.assert_rejected(self.make_ipa(executable=macho(crypt_id=1)), "encrypted (cryptid 1)")

    def test_rejects_the_debug_dylib_layout(self):
        stub = macho(dylibs=("/usr/lib/libSystem.B.dylib", "@rpath/App.debug.dylib"))
        path = self.make_ipa(executable=stub, extra=[("Payload/App.app/App.debug.dylib", macho(file_type=6))])
        status, output = self.run_validator(path)
        self.assertEqual(status, 1)
        self.assertIn("debug-dylib layout: Payload/App.app/App.debug.dylib", output)
        self.assertIn("stub that loads @rpath/App.debug.dylib", output)

    def test_rejects_a_build_instrumented_for_code_coverage(self):
        instrumented = macho(data_sections=("__data", "__llvm_prf_cnts", "__llvm_prf_data"))
        self.assert_rejected(
            self.make_ipa(executable=instrumented),
            "instrumented for code coverage (sections __llvm_prf_cnts, __llvm_prf_data)",
        )

    def test_rejects_a_missing_or_unreadable_executable(self):
        self.assert_rejected(self.make_ipa(info=dict(DEFAULT_INFO, CFBundleExecutable="Other")), "is missing")
        self.assert_rejected(self.make_ipa(executable=b"#!/bin/sh\necho not Mach-O, but long enough to parse\n"), "not Mach-O")
        self.assert_rejected(self.make_ipa(executable_mode=0o644), "not executable")

    def test_rejects_entries_outside_payload_and_extra_bundles(self):
        self.assert_rejected(self.make_ipa(extra=[("__MACOSX/._Payload", b"")]), "entries outside Payload/: __MACOSX")
        self.assert_rejected(self.make_ipa(extra=[("Payload/Other.app/Info.plist", b"")]), "exactly one .app bundle")

    def test_rejects_a_missing_info_plist(self):
        path = os.path.join(self.folder.name, "empty.ipa")
        with zipfile.ZipFile(path, "w") as archive:
            archive.writestr("Payload/App.app/App", macho())
        self.assert_rejected(path, "Info.plist cannot be read")


class MachOParserTests(unittest.TestCase):
    def test_reads_platform_minimum_os_and_libraries(self):
        (parsed,) = validate_ipa.parse_macho(macho(dylibs=("/usr/lib/a.dylib", "@rpath/b.dylib")))
        self.assertEqual(parsed.cpu_type, ARM64)
        self.assertEqual(parsed.platform, IOS)
        self.assertEqual(parsed.minimum_os, "18.0.0")
        self.assertEqual(parsed.dylibs, ["/usr/lib/a.dylib", "@rpath/b.dylib"])
        self.assertEqual(parsed.sections, ["__text", "__cstring", "__data", "__bss"])
        self.assertIsNone(parsed.crypt_id)

    def test_rejects_truncated_load_commands(self):
        with self.assertRaises(validate_ipa.MachOError):
            validate_ipa.parse_macho(macho()[:40])


if __name__ == "__main__":
    unittest.main()
