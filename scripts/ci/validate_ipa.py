#!/usr/bin/env python3
"""Validate an unsigned IPA meant for LiveContainer.

    python3 scripts/ci/validate_ipa.py LiveCue-Remake.ipa \
        --bundle-id com.mahmudkarim.LiveCueRemake --version 1.1.0 --build 2 --minimum-os 18.0

Checks, all of which must pass:

- the archive is an intact zip and holds nothing but one `Payload/<Name>.app` bundle;
- `Info.plist` is readable and carries the expected identifier, version, build, minimum iOS
  version and device family;
- the main executable exists, is marked executable, and is a Mach-O program with an arm64 slice
  built for iOS devices (not for the simulator) that is not encrypted;
- the app does not use the debug-dylib layout (a stub executable next to `<Name>.debug.dylib`),
  which LiveContainer cannot patch;
- the executable is not instrumented for code coverage, which a scheme that gathers coverage
  turns on for plain builds too.

Needs nothing outside the Python standard library, so it runs on macOS, Linux and Windows.
"""

from __future__ import annotations

import argparse
import hashlib
import plistlib
import struct
import sys
import zipfile
from dataclasses import dataclass, field

FAT_MAGIC = 0xCAFEBABE
FAT_MAGIC_64 = 0xCAFEBABF
MH_MAGIC_64 = 0xFEEDFACF
CPU_TYPE_ARM64 = 0x0100000C
MH_EXECUTE = 0x2

LC_REQ_DYLD = 0x80000000
DYLIB_COMMANDS = {
    0x0C,  # LC_LOAD_DYLIB
    0x18 | LC_REQ_DYLD,  # LC_LOAD_WEAK_DYLIB
    0x1F | LC_REQ_DYLD,  # LC_REEXPORT_DYLIB
    0x23 | LC_REQ_DYLD,  # LC_LOAD_UPWARD_DYLIB
}
ENCRYPTION_COMMANDS = {0x21, 0x2C}  # LC_ENCRYPTION_INFO, LC_ENCRYPTION_INFO_64
LC_SEGMENT_64 = 0x19
LC_VERSION_MIN_IPHONEOS = 0x25
LC_BUILD_VERSION = 0x32

# Sections the compilers add when a build is instrumented for code coverage.
COVERAGE_SECTION_PREFIXES = ("__llvm_prf", "__llvm_cov")

PLATFORM_NAMES = {1: "macOS", 2: "iOS", 6: "Mac Catalyst", 7: "iOS simulator"}
PLATFORM_IOS = 2

DEBUG_DYLIB_SUFFIX = ".debug.dylib"
PREVIEW_DYLIB_NAME = "__preview.dylib"


class MachOError(Exception):
    """The bytes are not a Mach-O file this script understands."""


@dataclass
class MachOSlice:
    cpu_type: int
    file_type: int
    dylibs: list[str] = field(default_factory=list)
    sections: list[str] = field(default_factory=list)
    crypt_id: int | None = None
    platform: int | None = None
    minimum_os: str | None = None


def _packed_version(value: int) -> str:
    return f"{value >> 16}.{(value >> 8) & 0xFF}.{value & 0xFF}"


def _parse_thin(data: bytes) -> MachOSlice:
    if len(data) < 32 or struct.unpack_from("<I", data, 0)[0] != MH_MAGIC_64:
        raise MachOError("not a 64-bit little-endian Mach-O file")
    _, cpu_type, _, file_type, command_count, _, _, _ = struct.unpack_from("<8I", data, 0)
    result = MachOSlice(cpu_type=cpu_type, file_type=file_type)

    offset = 32
    for _ in range(command_count):
        if offset + 8 > len(data):
            raise MachOError("load commands run past the end of the file")
        command, size = struct.unpack_from("<II", data, offset)
        if size < 8 or offset + size > len(data):
            raise MachOError("malformed load command")

        if command in DYLIB_COMMANDS:
            name_offset = struct.unpack_from("<I", data, offset + 8)[0]
            name = data[offset + name_offset : offset + size].split(b"\0", 1)[0]
            result.dylibs.append(name.decode("utf-8", "replace"))
        elif command == LC_SEGMENT_64:
            # A 72-byte segment header, then one 80-byte record per section that starts with its name.
            section_count = struct.unpack_from("<I", data, offset + 64)[0]
            if 72 + section_count * 80 > size:
                raise MachOError("malformed segment command")
            for index in range(section_count):
                start = offset + 72 + index * 80
                result.sections.append(data[start : start + 16].split(b"\0", 1)[0].decode("ascii", "replace"))
        elif command in ENCRYPTION_COMMANDS:
            result.crypt_id = struct.unpack_from("<I", data, offset + 16)[0]
        elif command == LC_BUILD_VERSION:
            result.platform, minimum_os = struct.unpack_from("<II", data, offset + 8)
            result.minimum_os = _packed_version(minimum_os)
        elif command == LC_VERSION_MIN_IPHONEOS:
            result.platform = PLATFORM_IOS
            result.minimum_os = _packed_version(struct.unpack_from("<I", data, offset + 8)[0])
        offset += size
    return result


def parse_macho(data: bytes) -> list[MachOSlice]:
    """Returns one entry per architecture slice of a thin or universal Mach-O file."""
    if len(data) < 8:
        raise MachOError("file is too short to be Mach-O")
    magic = struct.unpack_from(">I", data, 0)[0]
    if magic not in (FAT_MAGIC, FAT_MAGIC_64):
        return [_parse_thin(data)]

    slices = []
    entry_format, entry_size = (">2I3I", 20) if magic == FAT_MAGIC else (">2I2Q2I", 32)
    count = struct.unpack_from(">I", data, 4)[0]
    for index in range(count):
        entry = struct.unpack_from(entry_format, data, 8 + index * entry_size)
        start, length = entry[2], entry[3]
        slices.append(_parse_thin(data[start : start + length]))
    return slices


class Report:
    """Collects the outcome of every check so that one run shows all problems."""

    def __init__(self) -> None:
        self.failures: list[str] = []
        self.passed = 0

    def check(self, ok: bool, passed: str, failed: str) -> bool:
        if ok:
            self.passed += 1
            print(f"  ok    {passed}")
        else:
            self.failures.append(failed)
            print(f"  FAIL  {failed}")
        return ok

    def equal(self, what: str, actual: object, expected: object) -> bool:
        return self.check(actual == expected, f"{what} is {actual!r}", f"{what} is {actual!r}, expected {expected!r}")


def sha256_of(path: str) -> str:
    digest = hashlib.sha256()
    with open(path, "rb") as handle:
        for block in iter(lambda: handle.read(1 << 20), b""):
            digest.update(block)
    return digest.hexdigest()


def validate(path: str, expected: argparse.Namespace, report: Report) -> None:
    try:
        archive = zipfile.ZipFile(path)
    except (OSError, zipfile.BadZipFile) as error:
        report.check(False, "", f"cannot open the archive: {error}")
        return

    with archive:
        damaged = archive.testzip()
        if not report.check(damaged is None, "zip integrity", f"zip integrity: {damaged!r} is damaged"):
            return

        names = archive.namelist()
        outside = sorted({name.split("/", 1)[0] for name in names if not name.startswith("Payload/")})
        report.check(not outside, "everything is under Payload/", f"entries outside Payload/: {', '.join(outside)}")

        bundles = sorted(
            {name.split("/")[1] for name in names if name.startswith("Payload/") and name.count("/") >= 2}
        )
        apps = [bundle for bundle in bundles if bundle.endswith(".app")]
        ok = len(apps) == 1 and len(bundles) == 1
        if not report.check(ok, f"one app bundle: Payload/{apps[0] if apps else ''}", f"Payload/ must hold exactly one .app bundle, found: {bundles}"):
            return
        root = f"Payload/{apps[0]}/"

        try:
            info = plistlib.loads(archive.read(root + "Info.plist"))
        except (KeyError, plistlib.InvalidFileException, ValueError) as error:
            report.check(False, "", f"Info.plist cannot be read: {error}")
            return
        report.check(True, "Info.plist is readable", "")

        report.equal("bundle identifier", info.get("CFBundleIdentifier"), expected.bundle_id)
        if expected.version is not None:
            report.equal("version", info.get("CFBundleShortVersionString"), expected.version)
        if expected.build is not None:
            report.equal("build", info.get("CFBundleVersion"), expected.build)
        if expected.minimum_os is not None:
            report.equal("minimum iOS version", info.get("MinimumOSVersion"), expected.minimum_os)
        if expected.device_family is not None:
            report.equal("device family", info.get("UIDeviceFamily"), expected.device_family)

        debug_dylibs = [
            name
            for name in names
            if name.startswith(root) and (name.endswith(DEBUG_DYLIB_SUFFIX) or name.endswith("/" + PREVIEW_DYLIB_NAME))
        ]
        report.check(
            not debug_dylibs,
            "no debug dylib in the bundle",
            f"debug-dylib layout: {', '.join(debug_dylibs)} (build the Release configuration)",
        )

        executable = info.get("CFBundleExecutable")
        if not report.check(
            bool(executable) and root + str(executable) in names,
            f"main executable {executable!r} is present",
            f"main executable {executable!r} named by CFBundleExecutable is missing",
        ):
            return
        entry = archive.getinfo(root + executable)
        mode = entry.external_attr >> 16
        report.check(mode & 0o111 != 0, "main executable is marked executable", f"main executable mode is {mode:o}, not executable")

        try:
            slices = parse_macho(archive.read(entry))
        except (MachOError, struct.error) as error:
            report.check(False, "", f"main executable is not Mach-O: {error}")
            return

    arm64 = [item for item in slices if item.cpu_type == CPU_TYPE_ARM64]
    if not report.check(bool(arm64), "arm64 slice", f"no arm64 slice (CPU types: {[hex(item.cpu_type) for item in slices]})"):
        return
    binary = arm64[0]

    report.check(binary.file_type == MH_EXECUTE, "Mach-O file type is an executable", f"Mach-O file type is {binary.file_type}, not an executable")
    platform = PLATFORM_NAMES.get(binary.platform, f"platform {binary.platform}")
    report.check(binary.platform == PLATFORM_IOS, f"built for iOS devices (minimum {binary.minimum_os})", f"built for {platform}, not for iOS devices")
    report.check(binary.crypt_id in (None, 0), "not encrypted", f"encrypted (cryptid {binary.crypt_id})")
    stub_links = [name for name in binary.dylibs if name.endswith(DEBUG_DYLIB_SUFFIX)]
    report.check(not stub_links, "executable is not a debug-dylib stub", f"executable is a stub that loads {', '.join(stub_links)}")
    instrumented = sorted({name for name in binary.sections if name.startswith(COVERAGE_SECTION_PREFIXES)})
    report.check(
        not instrumented,
        "not instrumented for code coverage",
        f"instrumented for code coverage (sections {', '.join(instrumented)}); "
        "build with CLANG_ENABLE_CODE_COVERAGE=NO, because the scheme gathers coverage",
    )


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("ipa")
    parser.add_argument("--bundle-id", required=True)
    parser.add_argument("--version", help="expected CFBundleShortVersionString")
    parser.add_argument("--build", help="expected CFBundleVersion")
    parser.add_argument("--minimum-os", help="expected MinimumOSVersion, for example 18.0")
    parser.add_argument(
        "--device-family",
        type=lambda text: [int(part) for part in text.split(",")],
        help="expected UIDeviceFamily as comma-separated numbers; 1 is iPhone",
    )
    expected = parser.parse_args(argv)

    print(f"Validating {expected.ipa}")
    report = Report()
    validate(expected.ipa, expected, report)

    if report.failures:
        for failure in report.failures:
            print(f"::error title=IPA validation::{failure}")
        print(f"{expected.ipa} is NOT valid: {len(report.failures)} check(s) failed.")
        return 1

    print(f"{expected.ipa} is valid: {report.passed} checks passed.")
    print(f"SHA-256 {sha256_of(expected.ipa)}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
