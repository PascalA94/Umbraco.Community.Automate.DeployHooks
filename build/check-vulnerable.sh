#!/usr/bin/env bash
# Release and PR gate: fail if any project resolves a package with a known advisory, direct or transitive.
# Parses the JSON output; never trusts the command's exit code.
# Usage: build/check-vulnerable.sh [--line N] [solution-or-project]   (default: the package solution)
# `dotnet list package` runs its own implicit restore with the default line (18), which overwrites a restore done for
# another line, so a bare call always audits 18. --line N does the locked restore for that line and then lists with
# --no-restore, so the audit covers the line named.
set -euo pipefail
cd "$(dirname "$0")/.."

LINE=""
if [ "${1:-}" = "--line" ]; then
  LINE="${2:-}"
  [[ "$LINE" =~ ^[0-9]+$ ]] || { echo "check-vulnerable: --line needs a number (got '${LINE}')" >&2; exit 2; }
  shift 2
fi
TARGET="${1:-src/Umbraco.Community.Automate.DeployHooks.slnx}"
OUT="$(mktemp)"
trap 'rm -f "$OUT"' EXIT

LIST_ARGS=()
if [ -n "$LINE" ]; then
  dotnet restore "$TARGET" --locked-mode "-p:AutomateMajor=$LINE" -v q >&2
  LIST_ARGS+=(--no-restore)
fi

dotnet list "$TARGET" package --vulnerable --include-transitive --format json ${LIST_ARGS[@]+"${LIST_ARGS[@]}"} > "$OUT" || true

python3 - "$OUT" <<'PY'
import json, sys
path = sys.argv[1]
try:
    data = json.load(open(path, encoding="utf-8-sig"))
except Exception as e:  # unparseable output is a failure, not a pass
    print(f"check-vulnerable: could not parse dotnet output: {e}", file=sys.stderr)
    print(open(path, encoding="utf-8", errors="replace").read(), file=sys.stderr)
    sys.exit(1)
if data.get("problems"):
    for p in data["problems"]:
        print(f"check-vulnerable: problem: {p.get('text')}", file=sys.stderr)
    sys.exit(1)
found = []
for proj in data.get("projects", []):
    for fw in proj.get("frameworks", []) or []:
        for kind in ("topLevelPackages", "transitivePackages"):
            for pkg in fw.get(kind, []) or []:
                for v in pkg.get("vulnerabilities", []) or []:
                    found.append(f"{proj['path']}: {pkg['id']} {pkg.get('resolvedVersion')} {v.get('severity')} {v.get('advisoryurl')}")
if not data.get("projects"):
    print("check-vulnerable: no projects in output", file=sys.stderr)
    sys.exit(1)
if found:
    print("check-vulnerable: vulnerable packages found:", file=sys.stderr)
    for f in found:
        print("  " + f, file=sys.stderr)
    sys.exit(1)
print(f"check-vulnerable: no vulnerable packages in {len(data['projects'])} project(s)")
PY
