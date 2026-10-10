#!/usr/bin/env python3
"""Boot a simulator and wait until it is ready, retrying a stalled cold boot.

    python3 scripts/ci/boot_simulator.py <device identifier>

Each attempt boots the device if needed and waits for the boot to finish. A stalled or failed
attempt shuts the device down before the next one. The last failure fails the step.
"""

from __future__ import annotations

import argparse
import subprocess
import sys


def boot(udid: str, attempts: int, timeout: float) -> bool:
    for attempt in range(1, attempts + 1):
        print(f"Simulator boot attempt {attempt}/{attempts}", flush=True)
        try:
            # `bootstatus -b` boots the device when it is shut down, then blocks until it is usable.
            subprocess.run(["xcrun", "simctl", "bootstatus", udid, "-b"], check=True, timeout=timeout)
            return True
        except subprocess.TimeoutExpired:
            print(f"The simulator did not finish booting within {timeout:.0f} s.", flush=True)
        except subprocess.CalledProcessError as error:
            print(f"simctl bootstatus failed with exit code {error.returncode}.", flush=True)

        if attempt < attempts:
            print("Cold boot stalled; shutting down this test simulator before retry.", flush=True)
            try:
                subprocess.run(["xcrun", "simctl", "shutdown", udid], check=False, timeout=60)
            except subprocess.TimeoutExpired:
                pass
    return False


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("udid", help="simulator device identifier")
    parser.add_argument("--attempts", type=int, default=3)
    parser.add_argument("--timeout", type=float, default=180, help="seconds allowed per attempt")
    arguments = parser.parse_args(argv)

    if boot(arguments.udid, arguments.attempts, arguments.timeout):
        return 0
    print(f"::error title=Simulator did not boot::{arguments.udid} failed {arguments.attempts} boot attempts.")
    return 1


if __name__ == "__main__":
    sys.exit(main())
