#!/usr/bin/env bash
# Credential helper copy check (credential-helper-contract section 4 items 12 and 13): the store and both suites must be
# byte-identical to the Jira repo at the frozen tag, once the four package values are put back, and CanonicalHash must
# equal the value the contract publishes for that version. Run from anywhere; exits non-zero on any difference.
# Usage: build/check-contract-copy.sh            (clones the public Jira repo)
#        JIRA_SOURCE=<path or url> build/check-contract-copy.sh
set -euo pipefail
cd "$(dirname "$0")/.."

TAG="credential-helper-1.0.3"
# The published hash and tag commit for this tag (contract header). Change TAG, EXPECTED_HASH and EXPECTED_COMMIT together when the helper is re-copied.
EXPECTED_HASH="5e66dff814796722e4fb61695825476c9fdae14803a7c9de8d89e98889824eb2"
EXPECTED_COMMIT="4425ea5577a44ea0ccb56513b0be9a5977482996"
JIRA_SOURCE="${JIRA_SOURCE:-https://github.com/PascalA94/Umbraco.Community.Automate.Jira}"

pkg="src/Umbraco.Community.Automate.DeployHooks/CredentialHelper"
tests="src/Umbraco.Community.Automate.DeployHooks.Tests/CredentialHelperContract"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

git clone --quiet -c advice.detachedHead=false --depth 1 --branch "$TAG" "$JIRA_SOURCE" "$work/jira"
# A tag can be moved, and git clone --branch prefers a branch of the same name: pin the commit too.
[ "$(git -C "$work/jira" rev-parse HEAD)" = "$EXPECTED_COMMIT" ] || { echo "check-contract-copy: $TAG is at a different commit than $EXPECTED_COMMIT" >&2; exit 1; }
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
