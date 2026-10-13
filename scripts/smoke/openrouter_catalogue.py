#!/usr/bin/env python3
"""Opt-in live check of the public OpenRouter model catalogue (test plan section 3, L-2).

    python scripts/smoke/openrouter_catalogue.py [--timeout SECONDS]

Makes one unauthenticated GET of the public model list, the request the iPhone app makes, and checks
what the phone's parser relies on (docs/spec/02-ios-core.md, section 4):

  * the body is a JSON object with a `data` array;
  * every entry has a string `id` and a string `name`;
  * every entry has `architecture.input_modalities` and `architecture.output_modalities`, arrays of strings;
  * `supported_parameters`, where present, is an array of strings;
  * `pricing`, where present, is an object, and `pricing.prompt` and `pricing.completion`, where
    present, are strings.

It prints the number of entries, the number the phone would leave out of its list (not text in and
text out, or a `:batch` variant), and PASS or FAIL.

It sends no key and no other header that identifies anyone, follows no redirect, reads at most
8 MiB and writes no file. It is run by hand; no test and no CI job runs it. One anonymous request,
free of charge.

Exit codes: 0 PASS, 1 FAIL, 2 the arguments were refused.
"""

from __future__ import annotations

import argparse
import json
import sys
import urllib.error
import urllib.request
from dataclasses import dataclass, field

# Fixed. There is no argument that changes it: this script talks to one public address only.
CATALOGUE_URL = "https://openrouter.ai/api/v1/models"

DEFAULT_TIMEOUT_SECONDS = 20  # the phone's idle timeout for this request
MAX_TIMEOUT_SECONDS = 120
MAX_BODY_BYTES = 8 * 1024 * 1024  # the phone's size cap
MAX_PROBLEMS_SHOWN = 10

EXIT_PASS = 0
EXIT_FAIL = 1
EXIT_USAGE = 2


@dataclass
class CatalogueReport:
    """What a catalogue document holds, as the phone's parser would see it."""

    entries: int = 0
    skipped: int = 0
    problems: list[str] = field(default_factory=list)

    @property
    def passed(self) -> bool:
        return not self.problems


def _is_string_list(value: object) -> bool:
    return isinstance(value, list) and all(isinstance(item, str) for item in value)


def _entry_problems(entry: object) -> list[str]:
    """Why the phone's parser would refuse this entry; empty when it would not."""
    if not isinstance(entry, dict):
        return ["is not an object"]

    problems = []
    for member in ("id", "name"):
        if not isinstance(entry.get(member), str):
            problems.append(f"`{member}` is not a string")

    architecture = entry.get("architecture")
    if not isinstance(architecture, dict):
        problems.append("`architecture` is not an object")
    else:
        for member in ("input_modalities", "output_modalities"):
            if not _is_string_list(architecture.get(member)):
                problems.append(f"`architecture.{member}` is not an array of strings")

    if "supported_parameters" in entry and not _is_string_list(entry["supported_parameters"]):
        problems.append("`supported_parameters` is not an array of strings")

    if "pricing" in entry:
        pricing = entry["pricing"]
        if not isinstance(pricing, dict):
            problems.append("`pricing` is not an object")
        else:
            for member in ("prompt", "completion"):
                if member in pricing and not isinstance(pricing[member], str):
                    problems.append(f"`pricing.{member}` is not a string")

    return problems


def _phone_skips(entry: dict) -> bool:
    """The phone's filter: text in, text out, and not a `:batch` variant."""
    architecture = entry["architecture"]
    return (
        "text" not in architecture["input_modalities"]
        or "text" not in architecture["output_modalities"]
        or ":batch" in entry["id"]
    )


def check_catalogue(document: object) -> CatalogueReport:
    """Checks a parsed catalogue document. Pure: no network, no file."""
    report = CatalogueReport()
    if not isinstance(document, dict):
        report.problems.append("the body is not a JSON object")
        return report

    data = document.get("data")
    if not isinstance(data, list):
        report.problems.append("`data` is not an array")
        return report

    report.entries = len(data)
    if not data:
        report.problems.append("`data` is empty")

    for index, entry in enumerate(data):
        problems = _entry_problems(entry)
        if problems:
            # The index only: an entry's own text is not echoed.
            report.problems.extend(f"entry {index}: {problem}" for problem in problems)
        elif _phone_skips(entry):
            report.skipped += 1

    return report


class _NoRedirect(urllib.request.HTTPRedirectHandler):
    """One request means one request: a redirect is an answer, not an instruction."""

    def redirect_request(self, req, fp, code, msg, headers, newurl):  # noqa: D102, PLR0913
        return None


def fetch_catalogue(timeout: float) -> object:
    """The one request. Raises ValueError with a short reason when there is no usable document."""
    # The header the phone sends. No Authorization header, no cookie, and nothing read from the environment.
    request = urllib.request.Request(CATALOGUE_URL, headers={"Content-Type": "application/json"}, method="GET")
    opener = urllib.request.build_opener(_NoRedirect, urllib.request.ProxyHandler({}))
    try:
        with opener.open(request, timeout=timeout) as response:
            if response.status != 200:
                raise ValueError(f"the answer was HTTP {response.status}")
            body = response.read(MAX_BODY_BYTES + 1)
    except urllib.error.HTTPError as error:
        raise ValueError(f"the answer was HTTP {error.code}") from None
    except (urllib.error.URLError, TimeoutError, OSError) as error:
        raise ValueError(f"the request failed ({type(error).__name__})") from None

    if len(body) > MAX_BODY_BYTES:
        raise ValueError("the body is larger than 8 MiB")
    try:
        return json.loads(body.decode("utf-8"))
    except (UnicodeDecodeError, json.JSONDecodeError):
        raise ValueError("the body is not JSON") from None


def _timeout(text: str) -> float:
    try:
        value = float(text)
    except ValueError:
        raise argparse.ArgumentTypeError("must be a number of seconds") from None
    if not 1 <= value <= MAX_TIMEOUT_SECONDS:
        raise argparse.ArgumentTypeError(f"must be from 1 to {MAX_TIMEOUT_SECONDS} seconds")
    return value


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        prog="openrouter_catalogue.py",
        description="One anonymous GET of the public OpenRouter model list, checked against what the iPhone app's "
        "parser relies on. Sends no key and writes no file. Run by hand only.",
        epilog=f"Address (fixed): {CATALOGUE_URL}. Exit codes: 0 PASS, 1 FAIL, 2 the arguments were refused.",
        allow_abbrev=False,
    )
    parser.add_argument(
        "--timeout",
        type=_timeout,
        default=DEFAULT_TIMEOUT_SECONDS,
        metavar="SECONDS",
        help=f"how long to wait for the answer, 1 to {MAX_TIMEOUT_SECONDS} (default {DEFAULT_TIMEOUT_SECONDS})",
    )
    return parser


def main(argv: list[str] | None = None) -> int:
    # argparse prints the usage and exits with 2 for anything it does not know.
    arguments = build_parser().parse_args(argv)

    try:
        document = fetch_catalogue(arguments.timeout)
    except ValueError as error:
        print(f"OpenRouter catalogue: {error}")
        print("FAIL")
        return EXIT_FAIL

    report = check_catalogue(document)
    print(f"OpenRouter catalogue: {report.entries} entries, {report.skipped} the phone would skip")
    for problem in report.problems[:MAX_PROBLEMS_SHOWN]:
        print(f"  {problem}")
    if len(report.problems) > MAX_PROBLEMS_SHOWN:
        print(f"  and {len(report.problems) - MAX_PROBLEMS_SHOWN} more")
    print("PASS" if report.passed else "FAIL")
    return EXIT_PASS if report.passed else EXIT_FAIL


if __name__ == "__main__":
    sys.exit(main())
