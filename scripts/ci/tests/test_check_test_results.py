import os
import sys
import unittest

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))

from check_test_results import bundles, problems  # noqa: E402


def case(name, result="Passed"):
    return {"name": name, "nodeIdentifier": f"Suite/{name}", "nodeType": "Test Case", "result": result}


def bundle(name, kind, cases):
    suite = {"name": "Suite", "nodeType": "Test Suite", "result": "Passed", "children": cases}
    return {"name": name, "nodeType": kind, "result": "Passed", "children": [suite]}


def results(*nodes):
    plan = {"name": "Scheme", "nodeType": "Test Plan", "result": "Passed", "children": list(nodes)}
    return {"devices": [], "testNodes": [plan], "testPlanConfigurations": []}


CORE = bundle("CoreTests", "Unit test bundle", [case("parses()"), case("orders()")])
UI = bundle("AppUITests", "UI test bundle", [case("testLaunch()")])


class BundlesTests(unittest.TestCase):
    def test_collects_bundles_with_their_test_cases(self):
        found = bundles(results(CORE, UI))
        self.assertEqual([(item.name, item.kind) for item in found], [("CoreTests", "Unit test bundle"), ("AppUITests", "UI test bundle")])
        self.assertEqual(found[0].passed, ["Suite/parses()", "Suite/orders()"])

    def test_sorts_cases_by_result(self):
        mixed = bundle("CoreTests", "Unit test bundle", [case("a()"), case("b()", "Failed"), case("c()", "Skipped")])
        (found,) = bundles(results(mixed))
        self.assertEqual((found.passed, found.failed, found.other), (["Suite/a()"], ["Suite/b()"], ["Suite/c()"]))

    def test_returns_nothing_for_an_empty_result(self):
        self.assertEqual(bundles({}), [])


class ProblemsTests(unittest.TestCase):
    def test_accepts_a_run_with_every_required_bundle(self):
        self.assertEqual(problems(bundles(results(CORE, UI)), ["CoreTests", "AppUITests"]), [])

    def test_reports_a_bundle_that_did_not_run(self):
        (message,) = problems(bundles(results(UI)), ["CoreTests", "AppUITests"])
        self.assertIn("CoreTests did not run", message)
        self.assertIn("AppUITests", message)

    def test_reports_a_bundle_without_a_passed_test(self):
        skipped = bundle("CoreTests", "Unit test bundle", [case("a()", "Skipped")])
        (message,) = problems(bundles(results(skipped)), ["CoreTests"])
        self.assertIn("no test in it passed", message)

    def test_reports_failed_tests_even_in_bundles_that_are_not_required(self):
        failing = bundle("OtherTests", "Unit test bundle", [case("a()", "Failed")])
        self.assertEqual(problems(bundles(results(CORE, failing)), ["CoreTests"]), ["OtherTests: Suite/a() failed."])


if __name__ == "__main__":
    unittest.main()
