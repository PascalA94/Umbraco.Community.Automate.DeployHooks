#!/usr/bin/env bash
# Credential helper copy check (credential-helper-contract section 4 items 12 and 13): the store and both suites must be
# byte-identical to the Jira repo at the frozen tag, once the four package values are put back, and CanonicalHash must
# equal the value the contract publishes for that version. Run from anywhere; exits non-zero on any difference.
# Usage: build/check-contract-copy.sh            (clones the public Jira repo)
#        JIRA_SOURCE=<path or url> build/check-contract-copy.sh
set -euo pipefail
cd "$(dirname "$0")/.."

TAG="credential-helper-1.0.2"
# The published hash for this tag (contract header). Change both together when the helper is re-copied.
EXPECTED_HASH="4067bab2cc1fd0eb86d2f6bb717869bc44af0dae0cc7a095c206a740bd4d78cd"
JIRA_SOURCE="${JIRA_SOURCE:-https://github.com/PascalA94/Umbraco.Community.Automate.Jira}"

pkg="src/Umbraco.Community.Automate.DeployHooks/CredentialHelper"
tests="src/Umbraco.Community.Automate.DeployHooks.Tests/CredentialHelperContract"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

git clone --quiet -c advice.detachedHead=false --depth 1 --branch "$TAG" "$JIRA_SOURCE" "$work/jira"
jira_store="$work/jira/src/Umbraco.Community.Automate.Jira/CredentialHelper/JiraCredentialStore.cs"
jira_tests="$work/jira/tests/Umbraco.Community.Automate.Jira.Tests/CredentialHelperContract"
status=0

# Put the Jira values back, longest first, then diff. Nothing else may differ, not even a comment.
sed -e 's/Umbraco\.Community\.Automate\.DeployHooks\.Credentials/Umbraco.Community.Automate.Jira.Credentials/g' \
    -e 's/Umbraco:Automate:DeployHooks:Credentials/Umbraco:Automate:Jira:Credentials/g' \
    -e 's/DeployHooksCredentialStore/JiraCredentialStore/g' \
    -e 's/DeployHooksCredentialPart/JiraCredentialPart/g' \
    "$pkg/DeployHooksCredentialStore.cs" > "$work/store.cs"
diff "$work/store.cs" "$jira_store" || { echo "check-contract-copy: the store differs from $TAG" >&2; status=1; }

for f in CredentialHelperStoreContractTests.cs CredentialIsolationContractTests.cs; do
  diff "$tests/$f" "$jira_tests/$f" || { echo "check-contract-copy: $f differs from $TAG" >&2; status=1; }
done

got="$(sed -n 's/.*public const string CanonicalHash = "\([^"]*\)";.*/\1/p' "$tests/CredentialHelperStoreContractTests.cs" | head -1)"
if [ "$got" != "$EXPECTED_HASH" ]; then
  echo "check-contract-copy: CanonicalHash is '$got', the contract publishes '$EXPECTED_HASH'" >&2
  status=1
fi

[ "$status" -eq 0 ] && echo "check-contract-copy: store and suites match $TAG, CanonicalHash matches the contract"
exit "$status"
