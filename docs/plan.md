# Automate Deploy Hooks Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Execution is **native, plus one final whole-branch `qa-reviewer` review** (Task 12). Steps use checkbox (`- [ ]`) syntax for tracking.

**S7 gate result (Jira Task 0):** GO on both lines, 2026-11-04 (`../04-automate-jira/m0-result.md`). Cloud per-environment resolution is UNKNOWN (owner: Pascal) and is carried into the README.

**Goal:** Ship `Umbraco.Community.Automate.DeployHooks` 18.0.0: deploy-hook and Next.js-revalidate connections, two actions, and in-memory coalescing of bulk publishes.

**Architecture:** A plain class library laid out like Teams and Jira. Hook URLs and the revalidate secret live in package-owned config read only through `DeployHooksCredentialStore`, a verbatim copy of Jira's store. Two senders own every outbound request; a `CoalescingDispatcher` singleton debounces requests per connection on a `TimeProvider`.

**Tech Stack:** .NET 10, Umbraco.Cms.Core 18.3.0+, Umbraco.Automate.Core 18.5.1+, xUnit, Umbraco.Automate.Testing, Microsoft.Extensions.TimeProvider.Testing (test-only), GitHub Actions, gitleaks, osv-scanner, CodeQL.

**Spec:** `docs/design.md`. Credential spec: the credential helper contract 1.0.0 (wins where they disagree). The portfolio CI and GitHub rules win over this plan.

`SRC` = `src/Umbraco.Community.Automate.DeployHooks`, `TESTS` = `tests/Umbraco.Community.Automate.DeployHooks.Tests`. Jira repo (copy source): a sibling checkout of `Umbraco.Community.Automate.Jira`.

## Global Constraints

- **CI and GitHub rules (Pascal, 2026-11-04):** the portfolio `README.md` section "Build approach → CI and GitHub rules" overrides this plan's CI and GitHub tasks: public repo before the first CI push, fast PR checks only, e2e and SQL Server in a manual `release-checks.yml`, no credentials in workflows (random per-run passwords), e2e locators scoped to visible elements, and the first GitHub CI run planned as its own step before any release tag.
- Aliases, frozen at first release: `community.deployHooks.deployHook`, `community.deployHooks.nextRevalidate`, `community.deployHooks.triggerDeploy`, `community.deployHooks.revalidate`. Settings and output property names are frozen too.
- `net10.0`. `eng/lines.txt` = `18`. `AutomateMajor` default `18`; `Umbraco.Cms.Core [18.3.0, 19.0.0)`, `Umbraco.Automate.Core [18.5.1, 19.0.0)`. The 17 ranges `[17.8.0, 18.0.0)` / `[17.5.1, 18.0.0)` exist in props, unused. Build conventions are Jira's (`AUTOMATE_$(AutomateMajor)`, `AutomateFloat`, `packages.$(AutomateMajor).lock.json`, `release/<minor>.<patch>[-suffix]` tags).
- Credentials: store `DeployHooksCredentialStore`, prefix `Umbraco:Automate:DeployHooks:Credentials`, enum `DeployHooksCredentialPart { DeployHookUrl, RevalidateUrl, RevalidateSecret }`, namespace `Umbraco.Community.Automate.DeployHooks.Credentials`, folder `CredentialHelper/`. Copied from Jira tag `credential-helper-1.0.0`, never edited.
- Options `Umbraco:Automate:DeployHooks`: `TimeoutSeconds = 15`, `MaxItemsPerCall = 100`, `MaxBatchItems = 2000`, `AllowedRevalidateHosts = []`, `ProviderHosts` defaults per spec 4.4.
- Only `DeployHookSender` and `RevalidateSender` call `TryGet`. No URL or secret in any log, message, output, `Warnings` or `ToString()`. Our exceptions have a null `InnerException`.
- Data never fails a step: bad paths and tags are dropped with a warning that never echoes the value.
- HTTP: named client `community.deployHooks`, no redirects, SSRF handler kept, `ResponseHeadersRead`, at most 1,024 body bytes, timeout on the injected `TimeProvider`.
- Error categories and texts: spec 4.6, verbatim.
- Pitfalls P2 [Critical][Security]: no static mutable state; the dispatcher's map is an instance field. P5, S1, S3, S5: no views, controllers or editors (reflection test, Task 9).
- CI (README rules): PRs run build and tests at floor and latest, gitleaks, osv-scanner (`osv-scanner.toml`), check-vulnerable, CodeQL; no e2e or SQL Server on PRs; `release-checks.yml` is `workflow_dispatch` only; NuGet cache never in `publish.yml`; Dependabot `open-pull-requests-limit: 0`; `// gitleaks:allow` on fake secrets.
- **Claude never commits or pushes.** Every commit is a "Checkpoint (Pascal commits)" with no AI attribution.

## Review Focus

1. **A bulk publish of 50 pages** gives one hook call, not 50. Test: Task 6 `FiftyEnqueues_InWindow_SendOnce`.
2. **A trickle that never pauses** still fires by `MaxWaitSeconds`. Test: Task 6 `Trickle_FiresAtCeiling`.
3. **A request during a send** is not lost. Test: Task 6 `EnqueueDuringSend_StartsNewBatch`.
4. **A bound path that is an absolute URL, relative, or hostile** (`//evil.example`, control characters). Test: Task 8 `PathCleaner_Theory`.
5. **A hook URL on a look-alike host** (`api.vercel.com.evil.example`, IDN). Test: Task 3 `HostRules_LookAlikes_Blocked`.

---

### Task 1: Scaffold the repo with CI and gitleaks

**Files:** `global.json`, `Directory.Build.props`, `Directory.Packages.props`, `.gitignore`, `LICENSE` (MIT), `.slnx`, `SRC/*.csproj`, `SRC/DeployHooksAssemblyMarker.cs`, `TESTS/*.csproj`, `TESTS/ScaffoldTests.cs`, `eng/lines.txt`, `scripts/test.sh`, `osv-scanner.toml`, `.github/workflows/build.yml`, `.github/workflows/release-checks.yml`, `.github/dependabot.yml`, `docs/{design,plan,spikes}.md`

- [ ] **Step 1 (Pascal):** create the empty **public** repo `PascalA94/Umbraco.Community.Automate.DeployHooks` (no README, no licence). Set About: description, NuGet link, topics `umbraco`, `umbracopackage`, `dotnet`, `automate`, `vercel`, `netlify`, `nextjs`. Turn on secret scanning with push protection, CodeQL default setup and squash-merge only. Add this package's row to contract section 2 in the workspace.
- [ ] **Step 2:** `gh repo clone PascalA94/Umbraco.Community.Automate.DeployHooks`; copy `design.md` and `plan.md` into `docs/`; start `docs/spikes.md`. Expected: `ls docs` shows exactly `design.md plan.md spikes.md`.
- [ ] **Step 3:** copy `Directory.Build.props`, `eng/lines.txt` and `scripts/test.sh` from Jira `main`; change only package values; set `eng/lines.txt` to `18`. Tags per spec 7.
- [ ] **Step 4:** write `ScaffoldTests.Assembly_IsBuiltForTheSelectedAutomateMajor` (asserts `AssemblyMetadata("AutomateMajor") == "18"`). Run `dotnet test --filter ScaffoldTests`. Expected: FAIL, marker missing. Add `internal sealed class DeployHooksAssemblyMarker`. Run `./scripts/test.sh`. Expected: `Passed!`; `dotnet list SRC package` shows `Umbraco.Cms.Core` 18.3.0+.
- [ ] **Step 5:** write `build.yml` from Jira's, applying the README CI rules: matrix `floor` (locked restore) and `latest` (`AutomateFloat=true`), NuGet cache, gitleaks over every PR commit, osv-scanner with `--config=./osv-scanner.toml`, check-vulnerable, a `contract-copy` job (contract section 4 items 12 and 13: diff against Jira tag, compare `CanonicalHash`), `permissions: {}` at top, SHA-pinned actions. `release-checks.yml`: `workflow_dispatch` only. Run `actionlint`. Expected: no output.
- [ ] **Step 6 (Pascal):** pre-public check (`git log --all --name-only --format= | grep -iE "scope\.md|review|council"` prints nothing), push a branch, open a PR. Expected: all checks green in about 5 minutes. Add the branch ruleset requiring them.
- [ ] **Checkpoint (Pascal commits):** `chore: scaffold with v18 line, CI and gitleaks`

### Task 2: Copy the credential helper and its conformance suites

**Files:** `SRC/CredentialHelper/DeployHooksCredentialStore.cs` (copied), `SRC/CredentialHelper/DeployHooksCredentialPart.cs`, `TESTS/CredentialHelperContract/{CredentialHelperStoreContractTests,CredentialIsolationContractTests}.cs` (verbatim), `TESTS/CredentialHelperContract/DeployHooksCredentialStoreContractTests.cs`, `SRC/Configuration/DeployHooksComposer.cs`

**Interfaces:** Produces `DeployHooksCredentialStore : ICredentialStore<DeployHooksCredentialPart>` (contract 1.1) and its startup check.

- [ ] **Step 1:** confirm the contract says Frozen with hash `39aebb90…cdba`, and `git -C <jira-repo> rev-parse credential-helper-1.0.0` prints a commit. If not, stop.
- [ ] **Step 2:** follow contract section 4 items 2 to 8 exactly: `git show` the store and both suites from the tag, replace only the four tokens in the store (longest first), write the enum (members in the Global Constraints order), the 5.3 subclass (`SamplePart => DeployHookUrl`; `ParentSectionBoundTypes = [typeof(DeployHooksOptions)]`; pins added in Tasks 3 and 5), the three `EmbeddedResource` lines, `InternalsVisibleTo`, and the two registration lines. Run `diff` of both suites against the tag. Expected: no output.
- [ ] **Step 3:** run `./scripts/test.sh`. Expected: every C01-C31 and I01-I09 passes, C17 and C18 included. If I04 or I05 fails, stop and raise it with Jira; never edit a copied file.
- [ ] **Step 4:** add `DeployHooksComposerTests.Composer_RegistersStoreAndStartupCheck` on a real `UmbracoBuilder` (contract 4 item 11). Expected: PASS.
- [ ] **Checkpoint (Pascal commits):** `feat: package-owned credentials from credential-helper-contract 1.0.0`

### Task 3: Options, host rules and error classifier

**Files:** `SRC/Configuration/DeployHooksOptions.cs`, `SRC/Http/HostRules.cs`, `SRC/Http/ErrorClassifier.cs`, `SRC/Http/DeployHooksException.cs`, tests for each

**Interfaces:** Produces `HostRules.CheckDeployHook(DeployProvider provider, string url, out Uri? uri, out string? error)`, `HostRules.CheckRevalidate(string url, out Uri? uri, out string? error, out string? testWarning)`, `ErrorClassifier.Classify(HttpStatusCode code, string kind) -> (StepRunErrorCategory, string)`, `enum DeployProvider { Vercel, Netlify, CloudflarePages }`.

- [ ] **Step 1: failing tests.** `HostRules_EachProvider_Accepted` (spec 4.4 table), `HostRules_LookAlikes_Blocked` (`api.vercel.com.evil.example`, `api-vercel.com`, `xn--` look-alike, upper case and trailing dot accepted), `HostRules_HttpOrUserInfo_Blocked`, `HostRules_WrongPath_Blocked`, `Revalidate_AllowedHosts_Enforced`, `Classify_EveryRow` (spec 4.6 verbatim), `Exception_HasNullInnerException`, `Options_ConfiguredProviderHosts_ReplaceDefaults`.
- [ ] **Step 2:** run them. Expected: FAIL (types missing).
- [ ] **Step 3:** implement. Host normalisation: lower case, one trailing dot stripped, `Uri.IdnHost`. Messages never include path, query or userinfo.
- [ ] **Step 4 (Pascal):** from a real Vercel and Netlify hook, paste **only the host and the first path segments** (not the id) into `docs/spikes.md`. Update the defaults if they differ. Expected: tests green with the confirmed values.
- [ ] **Checkpoint (Pascal commits):** `feat: host rules and error mapping`

### Task 4: Senders

**Files:** `SRC/Http/DeployHookSender.cs`, `SRC/Http/RevalidateSender.cs`, `SRC/Http/Scrubber.cs`, tests

**Interfaces:** Consumes Tasks 2 and 3. Produces `IDeployHookSender.SendAsync(DeployProvider provider, string credentialName, CancellationToken ct) -> Task<SendResult>` and `IRevalidateSender.SendAsync(string credentialName, IReadOnlyList<string> paths, IReadOnlyList<string> tags, bool test, CancellationToken ct) -> Task<SendResult>`; `record SendResult(bool Ok, int? StatusCode, string? ProviderJobId, StepRunErrorCategory? Category, string? Message)`.

- [ ] **Step 1: failing tests** with a recording handler: `Deploy_PostsEmptyBodyToResolvedUrl`, `Deploy_MissingCredential_NoRequest`, `Deploy_GuardTripped_NoRequest`, `Revalidate_SendsBearerAndJson`, `Revalidate_SplitsOverMaxItemsPerCall`, `Redirect307_NoSecondRequest_NoLocationInMessage`, `Body50Mb_ReadsAtMost1024Bytes`, `Timeout_FakeClock_GivesTimeout`, `JobId_OnlyWhenSafeShape`, `Logs_StatusAndCountsOnly` (capture logger at Trace: no sentinel host, path, secret).
- [ ] **Step 2:** run. Expected: FAIL.
- [ ] **Step 3:** implement. Register the named client in the composer with `AllowAutoRedirect = false` over Automate's SSRF handler (or `Constants.HttpClients.NoRedirect` if the handler isn't public; record which in `spikes.md`).
- [ ] **Step 4:** run. Expected: PASS.
- [ ] **Checkpoint (Pascal commits):** `feat: deploy hook and revalidate senders`

### Task 5: Connections, Test connection and the saving handler

**Files:** `SRC/Connection/*`, `SRC/CredentialHelper/DeployHooksCredentialSourceCheck.cs`, tests

- [ ] **Step 1: failing tests:** `Deploy_Test_DoesNotCallHook` (zero requests; success text from spec 3.1), `Deploy_Test_BadHost_Failure`, `Revalidate_Test_SendsTestBody`, `Revalidate_Test_UnlistedHost_WarnsWithHost`, `Saving_PastedUrlOrReference_RefusedWithoutEcho` (`https://api.vercel.com/…`, `$Umbraco:Automate:Secrets:X`, `my-site`), `SourceCheck_AppsettingsJson_Warns`, `SourceCheck_EnvVar_Silent`, `NoSettingsType_HasSensitiveOrUrlField`.
- [ ] **Step 2:** run. Expected: FAIL.
- [ ] **Step 3:** implement as Teams' connection (spec 3.1, 3.2). Add the C28 pin for the source check's `KeyFor` call and the C31 pin for its `IConfiguration`, one-line reasons.
- [ ] **Step 4:** run `./scripts/test.sh`. Expected: PASS, contract suites still green.
- [ ] **Checkpoint (Pascal commits):** `feat: deploy hook and revalidate connections`

### Task 6: Coalescing dispatcher

**Files:** `SRC/Coalescing/CoalescingDispatcher.cs`, `SRC/Coalescing/PendingBatch.cs`, `TESTS/CoalescingDispatcherTests.cs`

**Interfaces:** Produces `CoalescingDispatcher.Enqueue(CoalesceKey key, CoalesceSettings settings, IReadOnlyCollection<string> paths, IReadOnlyCollection<string> tags, Func<PendingBatchSnapshot, CancellationToken, Task<SendResult>> send) -> EnqueueResult(Guid BatchId, bool Joined)`; `record CoalesceKey(Guid ConnectionId, string Kind)`; `record CoalesceSettings(int CoalesceSeconds, int MaxWaitSeconds)`. Also `IHostedService`.

- [ ] **Step 1: failing tests** on `FakeTimeProvider`: `FiftyEnqueues_InWindow_SendOnce`, `Trickle_FiresAtCeiling`, `EnqueueDuringSend_StartsNewBatch`, `Union_DedupesPathsAndTags`, `BatchCap_DropsBeyond2000_LogsCountOnly`, `FailedSend_RetriesAt5And30Seconds_ThenWarns`, `StopAsync_FlushesPendingOnce`, `DifferentConnections_DoNotShareBatch`, `Dispatcher_HasNoStaticMutableFields` (P2).
- [ ] **Step 2:** run. Expected: FAIL.
- [ ] **Step 3:** implement spec 4.3. Atomic remove-then-send (`TryRemove` with the batch instance). Fire time `min(LastAt + window, FirstAt + maxWait)`.
- [ ] **Step 4:** run. Expected: PASS.
- [ ] **Checkpoint (Pascal commits):** `feat: per-connection coalescing`

### Task 7: Trigger deploy hook action

**Files:** `SRC/Actions/TriggerDeployAction.cs` (+ settings, `DeployHookOutput`), tests

- [ ] **Step 1: failing tests:** `Inline_Success_StatusSent`, `Inline_404_FailsConfigurationError` (spec text), `Inline_429_FailsRateLimiting`, `Coalesced_FirstQueued_ThenJoined_SameBatchId`, `Reason_NeverSentOrLogged`, `ContractSnapshot_AliasesAndPropertyNames` (frozen names, D1 by analogy).
- [ ] **Step 2:** run. Expected: FAIL.
- [ ] **Step 3:** implement spec 3.3 through `ActionTestHarness` patterns.
- [ ] **Step 4:** run. Expected: PASS.
- [ ] **Checkpoint (Pascal commits):** `feat: trigger deploy hook action`

### Task 8: Path cleaner and revalidate action

**Files:** `SRC/Actions/PathCleaner.cs`, `SRC/Actions/RevalidateAction.cs` (+ settings, output), tests

- [ ] **Step 1: failing tests:** `PathCleaner_Theory` (`https://site.example/a?x=1#f` → `/a`; `news/b` dropped; `//evil.example/x` dropped; `/a\u0000b` dropped; 1,025 chars dropped; `/%2Fx` kept as given), `TagRule_Theory`, `Revalidate_DroppedValues_WarnWithoutEcho`, `Revalidate_NothingLeft_SucceedsNoRequest`, `Revalidate_Coalesced_UnionInOneCall`, `Revalidate_TestFlagOnlyFromConnectionTest`.
- [ ] **Step 2:** run. Expected: FAIL.
- [ ] **Step 3:** implement spec 3.4. **Spike step:** on the demo site, publish a page with 10 children ("publish with descendants") and record in `spikes.md` how many Content Published runs Automate starts, and whether the trigger output has a `url`.
- [ ] **Step 4:** run. Expected: PASS.
- [ ] **Checkpoint (Pascal commits):** `feat: revalidate action`

### Task 9: Isolation and leak tests

**Files:** `TESTS/LeakTests.cs`, `TESTS/ArchitectureTests.cs`

- [ ] **Step 1:** write `HttpRequestStep_PackageReference_NeverResolves` (each part, URL and header, through the contract's `AutomateResolverHost`), `ForeignHost_ViaBinding_CannotRedirectSenders` (no setting can change the destination), `Sentinels_NeverInStepOutputs_Errors_Logs_ActivityTags` (every failure path, both actions, both modes), `NoControllersViewsOrEditors`, `OnlySenders_CallTryGet` (IL scan).
- [ ] **Step 2:** run `./scripts/test.sh`. Expected: PASS. If one fails, fix the owning task's code, not the test.
- [ ] **Checkpoint (Pascal commits):** `test: package leak and isolation tests`

### Task 10: README, SECURITY.md and Marketplace files

- [ ] **Step 1:** README: what it does vs HTTP Request; setup per provider (env var `Umbraco__Automate__DeployHooks__Credentials__mysite__DeployHookUrl`, Cloud secret, Key Vault); the revalidate contract v1 and the sample App Router route handler; coalescing, the single-server bound and multi-server behaviour; `CoalesceSeconds = 0` for visible failures; Cloud UNKNOWN; the allow-list residual from `m0-result.md`.
- [ ] **Step 2:** SECURITY.md: honest claims, rotation runbook per provider, reporting. `umbraco-marketplace.json` with spec 7 tags.
- [ ] **Step 3:** run `./scripts/test.sh` and `dotnet pack -c Release`. Expected: the packed README renders; the pack holds one DLL and no test or docs files.
- [ ] **Checkpoint (Pascal commits):** `docs: README, SECURITY and Marketplace`

### Task 11: Release workflow and ID reservation

- [ ] **Step 1 (Pascal):** reserve the ID: scratch `classlib`, README "Placeholder. Not functional yet.", pack `0.0.1-alpha`, push, unlist. 2FA on.
- [ ] **Step 2:** copy Jira's `publish.yml` (no NuGet cache, trusted publishing or `nuget` environment, `id-token: write` only there, `release/*` tags only). Run `actionlint`. Expected: no output.
- [ ] **Checkpoint (Pascal commits):** `ci: release workflow`

### Task 12: Final whole-branch review

- [ ] **Step 1:** re-run the contract diff and C17; if a newer `credential-helper-*` tag exists, re-copy first.
- [ ] **Step 2:** dispatch `qa-reviewer` (fresh context, opus) with the branch diff, `docs/*`, the contract and `~/.claude/references/umbraco-pitfalls.md`. Ask for findings by severity against Global Constraints and Review Focus.
- [ ] **Step 3:** fix under the owning task's tests; `./scripts/test.sh`. Expected: `Failed: 0`, CI green.
- [ ] **Checkpoint (Pascal commits):** `fix: final review findings`

### Task 13: Exercise it for real, then release

- [ ] **Step 1 (Pascal):** a Vercel Hobby test project with the sample revalidate route deployed, a deploy hook, and a revalidate secret. Set the credentials as env vars on an 18.3.0+ demo site with Automate 18.5.1+. Values never go into chat or the repo.
- [ ] **Step 2:** Test both connections. Expected: spec 3.1 and 3.2 success texts; the hook was not called.
- [ ] **Step 3:** publish 50 pages in one go (bulk publish). Expected: one Vercel deployment within `CoalesceSeconds` + build time; 50 green runs sharing one `BatchId`.
- [ ] **Step 4:** revalidate: change a page, publish, see the new content on the deployed page without a rebuild.
- [ ] **Step 5:** point a credential at a deleted hook with `CoalesceSeconds = 0`. Expected: the 404 text. Then `grep -rc "api.vercel.com\|<secret prefix>" umbraco/Logs` and an automation export. Expected: no hits.
- [ ] **Step 6:** release: `release/0.0-beta.1`, install the beta on the demo site, repeat Step 3; then `security-analyst` release sign-off on the secret path; then `release/0.0`, Marketplace listing, forum post.
- [ ] **Checkpoint (Pascal commits):** `docs: acceptance results`
