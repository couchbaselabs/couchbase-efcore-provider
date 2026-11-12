#!/usr/bin/env bash
# Runs the EF Core specification suites against a Couchbase cluster, one test class per test
# process, and compares the results with the known-failures baseline.
#
# One process per class keeps a crash (for example a stack overflow) from aborting the whole run,
# and keeps each suite's fixture setup independent.
#
# Usage:
#   scripts/run-suites.sh [--out DIR] [--no-build] [--update-baseline] [CLASS_FILTER...]
#
#   --out DIR           where TRX files and the summary go (default: ./TestResults/spec)
#   --no-build          skip the Release build
#   --update-baseline   rewrite known-failures.txt from this run instead of comparing
#   CLASS_FILTER        run only classes whose name contains one of these strings
#
# Cluster settings come from COUCHBASE_CONNECTION_STRING, COUCHBASE_USERNAME, COUCHBASE_PASSWORD
# and COUCHBASE_BUCKET (see TestUtilities/CouchbaseTestEnvironment.cs). Without
# COUCHBASE_CONNECTION_STRING each test process starts the Aspire AppHost container instead.
set -uo pipefail

project_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
out_dir="$project_dir/TestResults/spec"
build=1
update=0
filters=()

while [[ $# -gt 0 ]]; do
  case "$1" in
    --out) out_dir="$2"; shift 2 ;;
    --no-build) build=0; shift ;;
    --update-baseline) update=1; shift ;;
    -h|--help) sed -n '2,19p' "$0"; exit 0 ;;
    *) filters+=("$1"); shift ;;
  esac
done

mkdir -p "$out_dir"
out_dir="$(cd "$out_dir" && pwd)"
cd "$project_dir"

if [[ $build -eq 1 ]]; then
  dotnet build -c Release --nologo -v q || exit 1
fi

# Every test class in the assembly, fully qualified ('+' for nested classes). A read loop rather
# than mapfile, which macOS's bash 3.2 lacks.
classes=()
while IFS= read -r c; do
  [[ -n "$c" ]] && classes+=("$c")
done < <(
  dotnet test -c Release --no-build --list-tests 2>/dev/null \
    | sed -n '/The following Tests are available/,$p' | sed 1d \
    | sed -E 's/^[[:space:]]+//; s/\(.*//' \
    | awk -F. '{ NF--; print }' OFS=. \
    | sort -u)

if [[ ${#filters[@]} -gt 0 ]]; then
  selected=()
  for c in "${classes[@]}"; do
    for f in "${filters[@]}"; do
      if [[ "$c" == *"$f"* ]]; then selected+=("$c"); break; fi
    done
  done
  classes=("${selected[@]}")
fi

echo "Running ${#classes[@]} test classes; results in $out_dir"
summary="$out_dir/summary.tsv"
: > "$summary"

for class in "${classes[@]}"; do
  short="${class##*.}"
  trx="$out_dir/$short.trx"
  rm -f "$trx"
  # The trailing '.' stops one class name matching another that starts with it.
  output=$(dotnet test -c Release --no-build --nologo \
    --filter "FullyQualifiedName~$class." \
    --blame-hang-timeout 30m --blame-hang-dump-type none \
    --logger "trx;LogFileName=$trx" 2>&1)
  line=$(grep -E "Passed!|Failed!" <<<"$output" | tail -1)
  if grep -q "aborted" <<<"$output"; then
    status="aborted"
  elif [[ -n "$line" ]]; then
    status=$(sed -E 's/.*Failed: *([0-9]+), Passed: *([0-9]+), Skipped: *([0-9]+), Total: *([0-9]+).*/failed=\1 passed=\2 skipped=\3 total=\4/' <<<"$line")
  else
    status="no-result"
  fi
  printf '%s\t%s\n' "$class" "$status" | tee -a "$summary"
done

if [[ $update -eq 1 ]]; then
  python3 "$project_dir/scripts/compare-baseline.py" --results "$out_dir" --update
else
  python3 "$project_dir/scripts/compare-baseline.py" --results "$out_dir"
fi
