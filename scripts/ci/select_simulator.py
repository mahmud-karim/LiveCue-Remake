#!/usr/bin/env python3
"""Choose an iPhone simulator on one iOS runtime for a CI test run.

    python3 scripts/ci/select_simulator.py --runtime 26.2

Only iPhone devices on exactly that runtime are considered (a patch release such as 26.2.1
counts as 26.2). The preferred model is taken; if the runtime has no iPhone device, one is
created; if the runtime is not installed, the step fails and lists what is.

The device identifier is printed to standard output; everything else goes to standard error.
Under GitHub Actions the step outputs `udid`, `name` and `runtime` are written as well.
"""

from __future__ import annotations

import argparse
import json
import os
import subprocess
import sys
from dataclasses import dataclass

# In order of preference; any other iPhone follows by name.
PREFERRED_MODELS = ["iPhone 17 Pro", "iPhone 17", "iPhone 16 Pro", "iPhone 16"]

CREATED_DEVICE_PREFIX = "LiveCue CI"

Version = tuple[int, ...]


class SelectionError(Exception):
    """No usable simulator could be chosen."""


@dataclass(frozen=True)
class Selection:
    runtime_id: str
    runtime_version: str
    name: str
    # Exactly one of the two below is set: an existing device, or a device type to create one from.
    udid: str | None = None
    device_type_id: str | None = None


def parse_version(text: str) -> Version:
    """Turns "26.2" or "26-2" into (26, 2). Raises ValueError for anything else."""
    parts = text.replace("-", ".").split(".")
    if not parts or any(not part.isdigit() for part in parts):
        raise ValueError(f"not a version: {text!r}")
    return tuple(int(part) for part in parts)


def format_version(version: Version) -> str:
    return ".".join(str(part) for part in version)


def model_rank(name: str) -> tuple[int, str]:
    """Sort key: preferred models in order, then every other model by name."""
    try:
        return (PREFERRED_MODELS.index(name), name)
    except ValueError:
        return (len(PREFERRED_MODELS), name)


def ios_runtimes(listing: dict) -> list[tuple[Version, dict]]:
    """Every iOS runtime in a `simctl list --json` listing with its parsed version, newest first."""
    found = []
    for runtime in listing.get("runtimes", []):
        identifier = runtime.get("identifier", "")
        if runtime.get("platform") != "iOS" and ".SimRuntime.iOS-" not in identifier:
            continue
        try:
            version = parse_version(runtime.get("version") or identifier.rsplit("iOS-", 1)[-1])
        except ValueError:
            continue
        found.append((version, runtime))
    found.sort(key=lambda item: item[0], reverse=True)
    return found


def choose(listing: dict, wanted: Version) -> Selection:
    """Picks a simulator from a `simctl list --json` listing. Does not run anything."""
    matching = [
        (version, runtime)
        for version, runtime in ios_runtimes(listing)
        if runtime.get("isAvailable", False) and version[: len(wanted)] == wanted
    ]
    if not matching:
        installed = ", ".join(
            format_version(version) + ("" if runtime.get("isAvailable", False) else " (unavailable)")
            for version, runtime in ios_runtimes(listing)
        )
        raise SelectionError(
            f"The iOS {format_version(wanted)} simulator runtime is not installed on this runner image. "
            f"Installed iOS runtimes: {installed or 'none'}."
        )

    version, runtime = matching[0]
    runtime_id = runtime["identifier"]

    devices = [
        device
        for device in listing.get("devices", {}).get(runtime_id, [])
        if device.get("isAvailable", False) and device.get("name", "").startswith("iPhone")
    ]
    if devices:
        device = min(devices, key=lambda item: model_rank(item["name"]))
        return Selection(runtime_id, format_version(version), device["name"], udid=device["udid"])

    device_types = [
        device_type
        for device_type in runtime.get("supportedDeviceTypes", [])
        if device_type.get("name", "").startswith("iPhone")
    ]
    if device_types:
        device_type = min(device_types, key=lambda item: model_rank(item["name"]))
        return Selection(
            runtime_id, format_version(version), device_type["name"], device_type_id=device_type["identifier"]
        )

    raise SelectionError(f"The iOS {format_version(version)} runtime has no iPhone device and no iPhone device type.")


def xcrun(*arguments: str) -> str:
    return subprocess.run(["xcrun", *arguments], check=True, capture_output=True, text=True).stdout


def describe(listing: dict, log) -> None:
    print("iOS simulator runtimes on this image:", file=log)
    for version, runtime in ios_runtimes(listing):
        devices = listing.get("devices", {}).get(runtime.get("identifier", ""), [])
        phones = sum(1 for device in devices if device.get("isAvailable") and device.get("name", "").startswith("iPhone"))
        state = "available" if runtime.get("isAvailable", False) else "unavailable"
        print(f"  iOS {format_version(version)}: {state}, {phones} iPhone device(s)", file=log)


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--runtime", required=True, help="iOS runtime version to test on, for example 26.2")
    arguments = parser.parse_args(argv)
    log = sys.stderr

    try:
        wanted = parse_version(arguments.runtime)
        listing = json.loads(xcrun("simctl", "list", "--json"))
        describe(listing, log)

        selection = choose(listing, wanted)
        udid = selection.udid
        if udid is None:
            name = f"{CREATED_DEVICE_PREFIX} {selection.name}"
            print(f"No iPhone device exists on iOS {selection.runtime_version}; creating {name!r}.", file=log)
            udid = xcrun("simctl", "create", name, selection.device_type_id, selection.runtime_id).strip()
    except SelectionError as error:
        print(f"::error title=No simulator::{error}", file=log)
        return 1
    except (subprocess.CalledProcessError, ValueError) as error:
        detail = getattr(error, "stderr", "") or ""
        print(f"::error title=Simulator selection failed::{error} {detail.strip()}", file=log)
        return 1

    print(f"Selected {selection.name} on iOS {selection.runtime_version} ({udid})", file=log)
    log.flush()

    github_output = os.environ.get("GITHUB_OUTPUT")
    if github_output:
        with open(github_output, "a", encoding="utf-8") as handle:
            handle.write(f"udid={udid}\nname={selection.name}\nruntime={selection.runtime_version}\n")

    print(udid)
    return 0


if __name__ == "__main__":
    sys.exit(main())
