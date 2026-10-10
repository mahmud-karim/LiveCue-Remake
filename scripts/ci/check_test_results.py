#!/usr/bin/env python3
"""Print what an Xcode result bundle ran and insist that named test bundles took part.

    python3 scripts/ci/check_test_results.py artifacts/Tests.xcresult \
        --require-bundle LiveCueCoreTests --require-bundle LiveCueRemakeUITests

`xcodebuild test` succeeds when no test fails, which is also true when a scheme quietly stops
running one of its bundles. This reads the result bundle with `xcresulttool` (Xcode 16 and
later) and fails unless every required bundle has at least one passed test and no failed one.
"""

from __future__ import annotations

import argparse
import json
import subprocess
import sys
from dataclasses import dataclass

TEST_CASE = "Test Case"
FAILED = "Failed"
PASSED = "Passed"


@dataclass
class BundleResult:
    name: str
    kind: str
    passed: list[str]
    failed: list[str]
    other: list[str]


def walk(node: dict):
    yield node
    for child in node.get("children", []):
        yield from walk(child)


def is_bundle(node: dict) -> bool:
    return "bundle" in node.get("nodeType", "").lower()


def bundles(results: dict) -> list[BundleResult]:
    """Collects every test bundle node in `xcresulttool get test-results tests` output."""
    found = []
    for root in results.get("testNodes", []):
        for node in walk(root):
            if not is_bundle(node):
                continue
            bundle = BundleResult(node.get("name", ""), node.get("nodeType", ""), [], [], [])
            for inner in walk(node):
                if inner.get("nodeType") != TEST_CASE:
                    continue
                name = inner.get("nodeIdentifier") or inner.get("name", "")
                result = inner.get("result", "")
                (bundle.passed if result == PASSED else bundle.failed if result == FAILED else bundle.other).append(name)
            found.append(bundle)
    return found


def problems(found: list[BundleResult], required: list[str]) -> list[str]:
    """Returns one message per broken rule; an empty list means the run is acceptable."""
    messages = []
    by_name = {bundle.name: bundle for bundle in found}
    for name in required:
        bundle = by_name.get(name)
        if bundle is None:
            present = ", ".join(sorted(by_name)) or "none"
            messages.append(f"Test bundle {name} did not run. Bundles in the result: {present}.")
        elif not bundle.passed:
            messages.append(f"Test bundle {name} ran but no test in it passed.")
    for bundle in found:
        for name in bundle.failed:
            messages.append(f"{bundle.name}: {name} failed.")
    return messages


def describe(found: list[BundleResult]) -> None:
    for bundle in found:
        print(f"{bundle.name} ({bundle.kind}): {len(bundle.passed)} passed, {len(bundle.failed)} failed, {len(bundle.other)} other")
        for label, names in (("ok  ", bundle.passed), ("FAIL", bundle.failed), ("skip", bundle.other)):
            for name in names:
                print(f"  {label}  {name}")


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("result_bundle")
    parser.add_argument("--require-bundle", action="append", default=[], metavar="NAME")
    parser.add_argument("--save", metavar="FILE", help="also write the raw test tree as JSON")
    arguments = parser.parse_args(argv)

    tool = subprocess.run(
        ["xcrun", "xcresulttool", "get", "test-results", "tests", "--path", arguments.result_bundle, "--compact"],
        check=False,
        capture_output=True,
        text=True,
    )
    if tool.returncode != 0:
        print(f"::error title=Result bundle unreadable::xcresulttool exited with code {tool.returncode}: {tool.stderr.strip()}")
        return 1
    if arguments.save:
        with open(arguments.save, "w", encoding="utf-8") as handle:
            handle.write(tool.stdout)

    try:
        found = bundles(json.loads(tool.stdout))
    except (ValueError, AttributeError) as error:
        print(f"::error title=Result bundle unreadable::unexpected xcresulttool output: {error}")
        print(tool.stdout[:4000])
        return 1

    describe(found)
    messages = problems(found, arguments.require_bundle)
    if messages:
        for message in messages:
            print(f"::error title=Test results::{message}")
        if not found:
            print("Raw xcresulttool output, first 4000 characters:")
            print(tool.stdout[:4000])
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
