"""Unit tests of the opt-in catalogue check: its arguments and its rules. They make no request.

    python3 -B -m unittest discover -s scripts/smoke/tests -t scripts/smoke/tests
"""

from __future__ import annotations

import contextlib
import io
import pathlib
import sys
import unittest
from unittest import mock

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parents[1]))

import openrouter_catalogue as smoke  # noqa: E402


def entry(**changes: object) -> dict:
    """An entry the phone accepts and keeps, with members replaced or (value `...`) removed."""
    value: dict = {
        "id": "text/model",
        "name": "Text",
        "architecture": {"input_modalities": ["text"], "output_modalities": ["text"]},
        "supported_parameters": ["structured_outputs"],
        "pricing": {"prompt": "0.000001", "completion": "0.000002"},
    }
    for member, replacement in changes.items():
        if replacement is ...:
            del value[member]
        else:
            value[member] = replacement
    return value


def run(argv: list[str]) -> tuple[int, str, str]:
    """Runs main() with the request forbidden. Returns the exit code and what was printed."""
    out, err = io.StringIO(), io.StringIO()
    with contextlib.redirect_stdout(out), contextlib.redirect_stderr(err):
        with mock.patch.object(smoke.urllib.request, "build_opener", side_effect=AssertionError("no request in a test")):
            try:
                code = smoke.main(argv)
            except SystemExit as stop:
                code = stop.code
    return code, out.getvalue(), err.getvalue()


class ArgumentTests(unittest.TestCase):
    def test_help_prints_the_usage_and_makes_no_request(self) -> None:
        code, out, _ = run(["--help"])

        self.assertEqual(0, code)
        self.assertIn("usage: openrouter_catalogue.py", out)
        self.assertIn("--timeout SECONDS", out)
        self.assertIn(smoke.CATALOGUE_URL, out)

    def test_bad_input_is_refused_with_the_usage_and_exit_code_2(self) -> None:
        refused = [
            ["extra"],
            ["--url", "https://example.test/"],
            ["--key", "synthetic-fixture-key"],
            ["--timeout"],
            ["--timeout", "soon"],
            ["--timeout", "0"],
            ["--timeout", "-5"],
            ["--timeout", "121"],
            ["--time", "5"],
        ]
        for argv in refused:
            with self.subTest(argv=argv):
                code, out, err = run(argv)

                self.assertEqual(smoke.EXIT_USAGE, code)
                self.assertIn("usage: openrouter_catalogue.py", err)
                self.assertEqual("", out)

    def test_the_address_is_fixed_and_there_is_no_way_to_give_a_key(self) -> None:
        self.assertEqual("https://openrouter.ai/api/v1/models", smoke.CATALOGUE_URL)
        options = {option for action in smoke.build_parser()._actions for option in action.option_strings}
        self.assertEqual({"-h", "--help", "--timeout"}, options)

    def test_a_failed_request_is_a_fail_not_a_crash(self) -> None:
        out = io.StringIO()
        with contextlib.redirect_stdout(out):
            with mock.patch.object(smoke, "fetch_catalogue", side_effect=ValueError("the request failed (URLError)")):
                code = smoke.main([])

        self.assertEqual(smoke.EXIT_FAIL, code)
        self.assertEqual(["OpenRouter catalogue: the request failed (URLError)", "FAIL"], out.getvalue().splitlines())

    def test_a_good_document_prints_the_counts_and_pass(self) -> None:
        document = {"data": [entry(), entry(id="text/model:batch"), entry(id="audio/model", architecture={"input_modalities": ["audio"], "output_modalities": ["text"]})]}
        out = io.StringIO()
        with contextlib.redirect_stdout(out):
            with mock.patch.object(smoke, "fetch_catalogue", return_value=document) as fetch:
                code = smoke.main(["--timeout", "5"])

        fetch.assert_called_once_with(5.0)
        self.assertEqual(smoke.EXIT_PASS, code)
        self.assertEqual(["OpenRouter catalogue: 3 entries, 2 the phone would skip", "PASS"], out.getvalue().splitlines())


class RuleTests(unittest.TestCase):
    def test_what_the_phone_accepts(self) -> None:
        accepted = [
            entry(),
            entry(pricing=...),
            entry(supported_parameters=...),
            entry(pricing={}),
            entry(pricing={"prompt": "0"}),
            entry(extra_member={"anything": 1}),
        ]
        report = smoke.check_catalogue({"data": accepted})

        self.assertTrue(report.passed, report.problems)
        self.assertEqual(len(accepted), report.entries)
        self.assertEqual(0, report.skipped)

    def test_what_the_phone_skips(self) -> None:
        report = smoke.check_catalogue(
            {
                "data": [
                    entry(),
                    entry(id="vendor/model:batch"),
                    entry(architecture={"input_modalities": ["image"], "output_modalities": ["text"]}),
                    entry(architecture={"input_modalities": ["text", "image"], "output_modalities": ["image"]}),
                    entry(architecture={"input_modalities": ["text", "image"], "output_modalities": ["text", "image"]}),
                ]
            }
        )

        self.assertTrue(report.passed, report.problems)
        self.assertEqual(5, report.entries)
        self.assertEqual(3, report.skipped)

    def test_what_makes_the_whole_catalogue_fail(self) -> None:
        broken = {
            "the body is not a JSON object": ["data"],
            "`data` is not an array": {"data": {"id": "x"}},
            "`data` is empty": {"data": []},
            "entry 0: is not an object": {"data": ["text/model"]},
            "entry 0: `id` is not a string": {"data": [entry(id=7)]},
            "entry 0: `name` is not a string": {"data": [entry(name=...)]},
            "entry 0: `architecture` is not an object": {"data": [entry(architecture=...)]},
            "entry 0: `architecture.input_modalities` is not an array of strings": {
                "data": [entry(architecture={"input_modalities": "text", "output_modalities": ["text"]})]
            },
            "entry 0: `architecture.output_modalities` is not an array of strings": {
                "data": [entry(architecture={"input_modalities": ["text"], "output_modalities": [1]})]
            },
            "entry 0: `supported_parameters` is not an array of strings": {"data": [entry(supported_parameters="tools")]},
            "entry 0: `pricing` is not an object": {"data": [entry(pricing="free")]},
            "entry 0: `pricing.prompt` is not a string": {"data": [entry(pricing={"prompt": 0.000001, "completion": "0"})]},
            "entry 0: `pricing.completion` is not a string": {"data": [entry(pricing={"prompt": "0", "completion": None})]},
            "entry 1: `id` is not a string": {"data": [entry(), entry(id=None)]},
        }
        for problem, document in broken.items():
            with self.subTest(problem=problem):
                report = smoke.check_catalogue(document)

                self.assertFalse(report.passed)
                self.assertIn(problem, report.problems)

    def test_a_problem_names_the_entry_by_its_position_only(self) -> None:
        report = smoke.check_catalogue({"data": [entry(name=["a private-looking value"])]})

        self.assertEqual(["entry 0: `name` is not a string"], report.problems)


class RequestTests(unittest.TestCase):
    def test_the_request_carries_no_key_and_follows_no_redirect(self) -> None:
        seen = {}

        class Answer(io.BytesIO):
            status = 200

        class Opener:
            def open(self, request, timeout):  # noqa: A003
                seen["request"] = request
                seen["timeout"] = timeout
                return Answer(b'{"data": []}')

        def build(*handlers):
            seen["handlers"] = handlers
            return Opener()

        with mock.patch.object(smoke.urllib.request, "build_opener", side_effect=build):
            document = smoke.fetch_catalogue(7)

        request = seen["request"]
        self.assertEqual({"data": []}, document)
        self.assertEqual("GET", request.get_method())
        self.assertEqual(smoke.CATALOGUE_URL, request.full_url)
        self.assertIsNone(request.data)
        self.assertEqual({"Content-type": "application/json"}, dict(request.header_items()))
        self.assertEqual(7, seen["timeout"])
        self.assertIn(smoke._NoRedirect, seen["handlers"])
        self.assertIsNone(smoke._NoRedirect().redirect_request(request, None, 302, "Found", {}, "https://example.test/"))


if __name__ == "__main__":
    unittest.main()
