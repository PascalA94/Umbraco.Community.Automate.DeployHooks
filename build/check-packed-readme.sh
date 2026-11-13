#!/usr/bin/env bash
# Usage: build/check-packed-readme.sh <nupkg>
# Fails when the README packed into the package differs from the repo README, when the package carries the demo
# site, tests or docs, or when it holds anything but one package DLL.
set -euo pipefail

if [[ $# -ne 1 || ! -f "$1" ]]; then
  echo "usage: $0 <nupkg>" >&2
  exit 2
fi

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
pkg="$1"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

listing="$(unzip -Z1 "$pkg")"

unwanted='(^|/)(demo|docs|tests?)/|\.Tests?\.dll$'
if grep -qiE "$unwanted" <<<"$listing"; then
  echo "check-packed-readme: the package contains demo, test or docs files:" >&2
  grep -iE "$unwanted" <<<"$listing" >&2
  exit 1
fi

dlls="$(grep -E '\.dll$' <<<"$listing" || true)"
if [[ "$(grep -c . <<<"$dlls")" -ne 1 ]]; then
  echo "check-packed-readme: expected exactly one DLL in the package, found: ${dlls:-none}" >&2
  exit 1
fi

readme="$(grep -E '(^|/)README\.md$' <<<"$listing" || true)"
if [[ "$(grep -c . <<<"$readme")" -ne 1 ]]; then
  echo "check-packed-readme: expected exactly one README.md in the package, found: ${readme:-none}" >&2
  exit 1
fi

unzip -p "$pkg" "$readme" > "$work/README.md"
if ! cmp -s "$work/README.md" "$root/README.md"; then
  echo "check-packed-readme: the packed $readme differs from the repo README.md" >&2
  exit 1
fi

echo "check-packed-readme: ok ($readme matches, one DLL, no demo, test or docs files)"
