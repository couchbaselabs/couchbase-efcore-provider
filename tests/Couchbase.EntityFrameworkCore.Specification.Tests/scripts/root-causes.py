#!/usr/bin/env python3
"""Group a spec-suite run's failures by root cause.

Reads the TRX files in one or more result directories (for example one per CI shard) and matches
each failed test's error message against scripts/root-causes.tsv. Prints the pass rate, the count
for each known cause, the pass rate of the tests no known cause stopped, and the most common
messages that match no cause (candidates for new rows).

Usage: root-causes.py --results DIR [DIR...] [--causes FILE] [--top N]
"""
import argparse
import collections
import os
import re

from trx import read_trx

DEFAULT_CAUSES = os.path.join(os.path.dirname(os.path.abspath(__file__)), "root-causes.tsv")


def read_causes(path):
    causes = []
    with open(path) as f:
        for line in f:
            line = line.rstrip("\n")
            if not line or line.startswith("#"):
                continue
            cause_id, pattern, description = line.split("\t", 2)
            causes.append((cause_id, re.compile(pattern), description))
    return causes


def read_results(dirs):
    """Return (outcome, message) once for each test name, as compare-baseline.py counts them."""
    results = {}
    for results_dir in dirs:
        for name, outcome, message in read_trx(results_dir):
            results[name] = (outcome, message)
    return results.values()


def normalize(message):
    """First line of a message, with names and numbers masked so similar messages group."""
    line = message.strip().split("\n")[0]
    line = re.sub(r"'[^']*'", "'…'", line)
    line = re.sub(r'"[^"]*"', '"…"', line)
    line = re.sub(r"\d+", "N", line)
    return line[:160]


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--results", required=True, nargs="+")
    parser.add_argument("--causes", default=DEFAULT_CAUSES)
    parser.add_argument("--top", type=int, default=25)
    args = parser.parse_args()

    causes = read_causes(args.causes)
    outcomes = collections.Counter()
    by_cause = collections.Counter()
    unmatched = collections.Counter()
    for outcome, message in read_results(args.results):
        outcomes[outcome] += 1
        if outcome != "Failed":
            continue
        cause = next((c for c, pattern, _ in causes if pattern.search(message)), None)
        if cause:
            by_cause[cause] += 1
        else:
            unmatched[normalize(message)] += 1

    passed, failed = outcomes["Passed"], outcomes["Failed"]
    total = sum(outcomes.values())
    blocked = sum(by_cause.values())
    other = failed - blocked
    print(f"Tests: {total}  passed {passed} ({passed / max(1, total):.1%})  failed {failed}  "
          f"not run {total - passed - failed}")
    print(f"Failures with a known cause: {blocked}; other failures: {other}")
    print(f"Pass rate without the known causes: {passed / max(1, passed + other):.1%}")

    print("\nKnown causes:")
    for cause_id, _, description in causes:
        if by_cause[cause_id]:
            print(f"  {by_cause[cause_id]:6d}  {cause_id:22s} {description}")

    print(f"\nMost common other failures (top {args.top} of {len(unmatched)} distinct messages):")
    for message, count in unmatched.most_common(args.top):
        print(f"  {count:6d}  {message}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
