#!/usr/bin/env python3
"""Export the attachments of an Xcode result bundle and give the images readable names.

    python3 scripts/ci/export_screenshots.py artifacts/Tests.xcresult artifacts/screenshots --prefix ios26

`xcresulttool export attachments` (Xcode 16 and later) writes files named by identifier plus a
`manifest.json` that maps them to tests. This renames each file to
`<prefix>-<test>-<attachment name>.<extension>` and fails when fewer images than `--minimum`
come out, so a test run that silently stopped attaching screenshots does not pass.
"""

from __future__ import annotations

import argparse
import json
import os
import re
import subprocess
import sys

IMAGE_EXTENSIONS = {".png", ".jpg", ".jpeg", ".heic"}


def slug(text: str) -> str:
    """Keeps letters, digits, dots and dashes; everything else becomes one dash."""
    return re.sub(r"[^A-Za-z0-9.]+", "-", text).strip("-")


def readable_name(prefix: str, test_identifier: str, attachment: dict) -> str:
    """Builds a file name from a manifest entry. Pure, so it can be tested without Xcode."""
    exported = attachment.get("exportedFileName", "")
    extension = os.path.splitext(exported)[1].lower()

    # Xcode suggests "<name>_<index>_<UUID>.<ext>"; keep the name the test gave the attachment.
    suggested = os.path.splitext(attachment.get("suggestedHumanReadableName", ""))[0]
    name = re.sub(r"_\d+_[0-9A-Fa-f-]{36}$", "", suggested) or os.path.splitext(exported)[0]

    # "PlaceholderUITests/testSomething()" becomes "PlaceholderUITests-testSomething".
    test = slug(test_identifier.replace("()", ""))
    parts = [part for part in (slug(prefix), test, slug(name)) if part]
    return "-".join(parts) + extension


def rename_from_manifest(directory: str, prefix: str) -> list[str]:
    """Renames exported files using manifest.json. Returns the image file names now present."""
    manifest_path = os.path.join(directory, "manifest.json")
    if os.path.exists(manifest_path):
        with open(manifest_path, encoding="utf-8") as handle:
            manifest = json.load(handle)
        taken: set[str] = set()
        for test in manifest:
            for attachment in test.get("attachments", []):
                source = os.path.join(directory, attachment.get("exportedFileName", ""))
                if not os.path.isfile(source):
                    continue
                target = readable_name(prefix, test.get("testIdentifier", ""), attachment)
                stem, extension = os.path.splitext(target)
                counter = 2
                while target in taken or os.path.exists(os.path.join(directory, target)):
                    target = f"{stem}-{counter}{extension}"
                    counter += 1
                taken.add(target)
                os.rename(source, os.path.join(directory, target))

    return sorted(
        name for name in os.listdir(directory) if os.path.splitext(name)[1].lower() in IMAGE_EXTENSIONS
    )


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("result_bundle")
    parser.add_argument("output_directory")
    parser.add_argument("--prefix", default="", help="put in front of every file name, for example ios26")
    parser.add_argument("--minimum", type=int, default=1, help="fail when fewer images are exported")
    arguments = parser.parse_args(argv)

    if not os.path.isdir(arguments.result_bundle):
        print(f"::error title=No result bundle::{arguments.result_bundle} does not exist.")
        return 1

    os.makedirs(arguments.output_directory, exist_ok=True)
    export = subprocess.run(
        [
            "xcrun", "xcresulttool", "export", "attachments",
            "--path", arguments.result_bundle,
            "--output-path", arguments.output_directory,
        ],
        check=False,
    )
    if export.returncode != 0:
        print(f"::error title=Attachment export failed::xcresulttool exited with code {export.returncode}.")
        return 1

    images = rename_from_manifest(arguments.output_directory, arguments.prefix)
    for image in images:
        size = os.path.getsize(os.path.join(arguments.output_directory, image))
        print(f"  {image} ({size} bytes)")

    if len(images) < arguments.minimum:
        print(f"::error title=Screenshots missing::Expected at least {arguments.minimum} image(s), exported {len(images)}.")
        return 1
    print(f"Exported {len(images)} image(s) to {arguments.output_directory}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
