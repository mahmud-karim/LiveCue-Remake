import os
import sys
import unittest

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))

import select_simulator  # noqa: E402
from select_simulator import SelectionError, choose, parse_version  # noqa: E402

PREFIX = "com.apple.CoreSimulator.SimRuntime."


def runtime(version, available=True, device_types=()):
    return {
        "identifier": PREFIX + "iOS-" + version.replace(".", "-"),
        "version": version,
        "platform": "iOS",
        "isAvailable": available,
        "supportedDeviceTypes": [
            {"name": name, "identifier": "type." + name.replace(" ", "-")} for name in device_types
        ],
    }


def device(name, available=True):
    return {"name": name, "udid": "UDID-" + name.replace(" ", "-"), "isAvailable": available, "state": "Shutdown"}


def listing(runtimes, devices):
    return {
        "runtimes": runtimes + [{"identifier": PREFIX + "tvOS-26-2", "version": "26.2", "platform": "tvOS", "isAvailable": True}],
        "devices": {PREFIX + "iOS-" + version.replace(".", "-"): entries for version, entries in devices.items()},
    }


class ParseVersionTests(unittest.TestCase):
    def test_reads_dotted_and_dashed_versions(self):
        self.assertEqual(parse_version("26.2"), (26, 2))
        self.assertEqual(parse_version("26-4-1"), (26, 4, 1))

    def test_rejects_text(self):
        with self.assertRaises(ValueError):
            parse_version("26.x")


class ChooseTests(unittest.TestCase):
    def test_picks_exactly_the_requested_runtime(self):
        simulators = listing(
            [runtime("18.5"), runtime("18.6"), runtime("26.2"), runtime("26.5")],
            {
                "18.5": [device("iPhone 16")],
                "18.6": [device("iPhone 16")],
                "26.2": [device("iPhone 17")],
                "26.5": [device("iPhone 17")],
            },
        )
        self.assertEqual(choose(simulators, (26, 2)).runtime_version, "26.2")
        selection = choose(simulators, (18, 6))
        self.assertEqual((selection.runtime_version, selection.name, selection.udid), ("18.6", "iPhone 16", "UDID-iPhone-16"))

    def test_treats_a_patch_release_as_the_requested_runtime(self):
        simulators = listing([runtime("26.4.1"), runtime("26.5")], {"26.4.1": [device("iPhone 17")]})
        self.assertEqual(choose(simulators, (26, 4)).runtime_version, "26.4.1")

    def test_prefers_models_in_order_then_any_iphone_by_name(self):
        phones = [device("iPhone 16 Pro Max"), device("iPad (A16)"), device("iPhone 16"), device("iPhone 16 Pro")]
        self.assertEqual(choose(listing([runtime("18.6")], {"18.6": phones}), (18, 6)).name, "iPhone 16 Pro")

        newer = [device("iPhone 16 Pro"), device("iPhone 17"), device("iPhone Air")]
        self.assertEqual(choose(listing([runtime("26.2")], {"26.2": newer}), (26, 2)).name, "iPhone 17")

        others = [device("iPad (A16)"), device("iPhone SE (3rd generation)"), device("iPhone 16e")]
        self.assertEqual(choose(listing([runtime("18.6")], {"18.6": others}), (18, 6)).name, "iPhone 16e")

    def test_ignores_unavailable_devices(self):
        simulators = listing([runtime("26.2")], {"26.2": [device("iPhone 17 Pro", available=False), device("iPhone Air")]})
        self.assertEqual(choose(simulators, (26, 2)).name, "iPhone Air")

    def test_asks_for_a_new_device_when_the_runtime_has_no_iphone(self):
        simulators = listing(
            [runtime("26.2", device_types=["iPad (A16)", "iPhone 17", "iPhone 17 Pro"])],
            {"26.2": [device("iPad (A16)")]},
        )
        selection = choose(simulators, (26, 2))
        self.assertIsNone(selection.udid)
        self.assertEqual(selection.device_type_id, "type.iPhone-17-Pro")
        self.assertEqual(selection.runtime_id, PREFIX + "iOS-26-2")

    def test_fails_with_the_installed_runtimes_when_the_runtime_is_missing(self):
        simulators = listing([runtime("26.2"), runtime("26.5", available=False)], {})
        for wanted in ((18, 6), (26, 5)):
            with self.assertRaises(SelectionError) as raised:
                choose(simulators, wanted)
            self.assertIn("is not installed", str(raised.exception))
            self.assertIn("26.5 (unavailable), 26.2", str(raised.exception))

    def test_reads_the_version_from_the_identifier_when_the_field_is_missing(self):
        entry = runtime("18.6")
        del entry["version"], entry["platform"]
        self.assertEqual(select_simulator.ios_runtimes({"runtimes": [entry]})[0][0], (18, 6))


if __name__ == "__main__":
    unittest.main()
