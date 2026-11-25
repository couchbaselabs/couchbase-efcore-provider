#!/usr/bin/env python3
"""Find spec tests that passed in one run and failed in another, and add them to flaky.txt.

Give each run as one directory: the downloaded artifacts of a CI run (every spec-results directory
under it is read, so both shards count as one run) or the output directory of run-suites.sh. Use
runs of the same code, for example consecutive nightly runs of main: a test that a code change
fixed or broke also changes outcome, but it is not flaky.

With --update, the tests are added to flaky.txt and removed from known-failures.txt. Without it,
they are only listed.

It also lists the flaky.txt entries whose outcome did not change: those that passed in every run
where they ran (perhaps no longer flaky; remove them) and those that failed in every run where they
ran (perhaps a real failure; move them to known-failures.txt). --update does not change these; an
entry that did not run is in neither list. Both lists must exist.

Usage: find-flaky.py --runs DIR DIR [DIR...] [--baseline FILE] [--flaky FILE] [--update]
"""
import argparse
import os
import sys

from testlists import (DEFAULT_BASELINE, DEFAULT_FLAKY, read_baseline, read_flaky, read_results,
                       write_baseline, write_flaky)


def result_dirs(run_dir):
    dirs = [root for root, _, files in os.walk(run_dir) if any(f.endswith(".trx") for f in files)]
    if not dirs:
        sys.exit(f"No TRX files under {run_dir}")
    return dirs


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--runs", required=True, nargs="+")
    parser.add_argument("--baseline", default=DEFAULT_BASELINE)
    parser.add_argument("--flaky", default=DEFAULT_FLAKY)
    parser.add_argument("--update", action="store_true")
    args = parser.parse_args()
    if len(args.runs) < 2:
        sys.exit("Give at least two runs.")

    # Read the lists first, so that a wrong path stops before anything is written.
    flaky = read_flaky(args.flaky)
    known, known_aborted = read_baseline(args.baseline)
    passed, failed, _, _ = read_results([d for run_dir in args.runs for d in result_dirs(run_dir)])

    found = (passed & failed) - flaky
    print(f"{len(args.runs)} runs: {len(passed & failed)} tests both passed and failed; "
          f"{len(found)} are not in {os.path.basename(args.flaky)} yet")
    for name in sorted(found):
        print(f"  {name}")

    for title, items in ((f"In {os.path.basename(args.flaky)} but passed in every run where they ran",
                          sorted(flaky & (passed - failed))),
                         (f"In {os.path.basename(args.flaky)} but failed in every run where they ran",
                          sorted(flaky & (failed - passed)))):
        if items:
            print(f"\n{title} ({len(items)}):")
            for name in items:
                print(f"  {name}")

    if args.update and found:
        write_flaky(args.flaky, flaky | found)
        write_baseline(args.baseline, known - found, known_aborted)
        print(f"\nAdded {len(found)} to {args.flaky}; removed {len(known & found)} from {args.baseline}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
