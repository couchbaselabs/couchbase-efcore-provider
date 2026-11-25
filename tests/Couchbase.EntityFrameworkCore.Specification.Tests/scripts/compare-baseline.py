#!/usr/bin/env python3
"""Compare a spec-suite run with the known-failures baseline.

known-failures.txt lists every test expected to fail, one display name per line, sorted, plus
'# aborted: <class>' lines for test classes whose process crashed. A run passes when nothing
outside the baseline fails. Tests that now pass but are still listed are reported so the baseline
can shrink; pass --update to rewrite it from the run. Pass several result directories (for
example one per CI shard) to treat them as one run.

flaky.txt lists tests that pass in some runs and fail in others (scripts/find-flaky.py finds them).
Their outcome is ignored: they are never a new failure, never reported as now passing, and never
written to known-failures.txt. The flaky tests that failed are listed, so that a test that now
always fails can be seen.

Both lists must exist, except that --update may create the baseline.

Usage: compare-baseline.py --results DIR [DIR...] [--baseline FILE] [--flaky FILE] [--update]
"""
import argparse
import sys

from testlists import (DEFAULT_BASELINE, DEFAULT_FLAKY, read_baseline, read_flaky, read_results,
                       write_baseline)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--results", required=True, nargs="+")
    parser.add_argument("--baseline", default=DEFAULT_BASELINE)
    parser.add_argument("--flaky", default=DEFAULT_FLAKY)
    parser.add_argument("--update", action="store_true")
    args = parser.parse_args()

    # Read the lists first, so that a wrong path stops before anything is written.
    flaky = read_flaky(args.flaky)
    if not args.update:
        known, known_aborted = read_baseline(args.baseline)
    passed, failed, aborted, ran = read_results(args.results)
    print(f"Run: {len(passed)} passed, {len(failed)} failed, {len(aborted)} classes aborted; "
          f"{len(flaky & failed)} of the failures are flaky tests")
    flaky_failed = sorted(flaky & failed)
    failed -= flaky
    passed -= flaky

    if args.update:
        write_baseline(args.baseline, failed, aborted)
        print(f"Baseline written: {len(failed)} failing tests, {len(aborted)} aborted classes -> {args.baseline}")
        return 0

    new_failures = sorted(failed - known)
    newly_aborted = sorted(aborted - known_aborted)
    now_passing = sorted(known & passed)
    # Only classes this run included (a shard or a filtered run skips the rest).
    recovered = sorted((known_aborted - aborted) & ran)

    for title, items in (("New failures", new_failures), ("Newly aborted classes", newly_aborted),
                         ("Now passing (remove from the baseline)", now_passing),
                         ("Aborted classes that now run", recovered),
                         ("Flaky tests that failed (outcome ignored)", flaky_failed)):
        if items:
            print(f"\n{title} ({len(items)}):")
            for item in items[:200]:
                print(f"  {item}")
            if len(items) > 200:
                print(f"  ... and {len(items) - 200} more")

    if new_failures or newly_aborted:
        print("\nFAILED: the run has failures that are not in the baseline.")
        return 1
    print("\nOK: no failures outside the baseline.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
