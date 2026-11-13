#!/usr/bin/env bash
# Runs the unit and integration suites once per Automate line in eng/lines.txt (SQLite).
# Usage: bash build/test.sh [line ...] [-- extra dotnet test args]   (default: every line). Quiet output; failures in full.
set -euo pipefail
cd "$(dirname "$0")/.."
lines=()
while [ $# -gt 0 ] && [ "$1" != "--" ]; do lines+=("$1"); shift; done
[ "${1:-}" = "--" ] && shift
if [ ${#lines[@]} -eq 0 ]; then while read -r l; do [ -n "$l" ] && lines+=("$l"); done < eng/lines.txt; fi
for line in "${lines[@]}"; do
  echo "== Automate $line"
  dotnet restore src/Umbraco.Community.Automate.DeployHooks.slnx "-p:AutomateMajor=$line" -v q
  dotnet build src/Umbraco.Community.Automate.DeployHooks.slnx -c Release --no-restore "-p:AutomateMajor=$line" -v q
  dotnet test src/Umbraco.Community.Automate.DeployHooks.slnx -c Release --no-build "-p:AutomateMajor=$line" --logger "console;verbosity=minimal" "$@"
done
