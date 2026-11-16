# Spikes and gates

Results of the M0 spikes and gates. Tags: **[verified]** run or read in a session; **[docs]** read in public provider docs only; **[PENDING]** needs a person.

## Gate: credential helper tag (Task 2)

- **Tag used:** `credential-helper-1.0.1` (the newest `credential-helper-*` tag in the Jira repo). 1.0.1 is a test-only patch of 1.0.0: the store changed only in its version marker, and the isolation suite's I01 and I05 oracles were updated for Automate 17.6 and 18.6. **[verified]**
- **Copy check:** `build/check-contract-copy.sh` puts the four package values back and diffs the store and both suites against the tag (no output), and compares `CanonicalHash` with the contract's published value `84ec091a…212e2`. It also runs on every PR (`contract-copy` job). **[verified]** (a tampered copy makes it exit 1).
- **Conformance suites:** C01-C31 and I01-I09 all pass on the 18 line, C17 (hash) and C18 (version) included. **[verified]**
- **Plan text superseded:** plan Task 2 Step 1 quotes the 1.0.0 hash `39aebb90…cdba`. The 1.0.1 hash replaces it.

## Spike: provider hook hosts and paths (Task 3 Step 4)

The defaults in `HostRules` are the design 4.4 values. Public docs agree with their shape **[docs]**:

| Provider | Host | Path starts with | Source read |
|---|---|---|---|
| Vercel | `api.vercel.com` | `/v1/integrations/deploy/` (then project id, then hook id) | Vercel docs, "Deploy Hooks" |
| Netlify | `api.netlify.com` | `/build_hooks/` (then one id) | Netlify docs, "Build hooks" |
| Cloudflare Pages | `api.cloudflare.com` | `/client/v4/pages/webhooks/deploy_hooks/` (then one id) | Cloudflare blog (2021) and Pages docs. Workers Builds hooks use a different path (`/client/v4/workers/builds/deploy_hooks/`); not this package |

- Vercel and Netlify docs allow query parameters on the hook URL (for example `?buildCache=false`). The rule checks host and path only, so a query is allowed and is sent as stored.
- **[PENDING] Pascal:** from a real Vercel and a real Netlify hook, paste **only the host and the first path segments, never the id**, here. If either differs from the table, change the default in `HostRules.RuleFor` and its tests, then re-run the suite. Until then the defaults stand on documentation alone.

  ```
  Vercel hook:  https://<host>/<first segments>/<id omitted>
  Netlify hook: https://<host>/<first segments>/<id omitted>
  ```

## Later spikes (not M0)

- **Task 8 Step 3:** see "Spike: Content Published runs and the trigger output" below.

## Rulings made in M0

- **Layout:** the template's layout is kept (`src/<pkg>`, `src/<pkg>.Tests`, `src/<pkg>.IntegrationTests`, `demo/`, `tests/e2e`), not the plan's `tests/<pkg>.Tests`. `TESTS` in the plan means `src/Umbraco.Community.Automate.DeployHooks.Tests`.
- **Scripts:** `build/test.sh` (template convention) replaces the plan's `scripts/test.sh`. `bash build/test.sh` runs every line in `eng/lines.txt`.
- **Switch:** `AutomateMajor` (default 18) as in Jira, with `AutomateFloat=true` for the latest leg. The template's `UmbracoTestVersion` and `UmbracoDemoVersion` now follow it.
- **Central package versions** (`Directory.Packages.props`) with Jira's three transitive pins. MessagePack is pinned at 3.1.11, not Jira's 3.1.9, because `Umbraco.Cms.Tests.Integration` 18.3.0 needs 3.1.11 (restore failed with NU1109 at 3.1.9).
- **Options:** `DeployHooksOptions` is created in Task 2 (the contract subclass needs the type) and gains `ProviderHosts` in Task 3. `ProviderHosts` is three typed members, not a dictionary: a dictionary is a catch-all that contract test C31 rejects.
- **Revalidate 404 text:** design 4.6 gives only the deploy-hook text for 404. A revalidate 404 says "The revalidation endpoint was not found. Check the route and the credential." (same `ConfigurationError` category).
- **Other 4xx:** codes not in design 4.6 (for example 410, 422) map to `InvalidResponse` with "The endpoint rejected the request ({code})."
- **Composer pin:** `DeployHooksComposer` binds the options from a parent section of the credential prefix, so it carries a C31 pin in `PinnedConfigurationReaders` with its reason, as Jira's composer does.
- **Design/plan overrides:** the "Resolved", "Security review changes" and "Plan review changes" sections the brief mentions do not exist in `design.md` or `plan.md` (both dated 2026-11-04), and neither does an M0 label. Pascal chose M0 = Tasks 1 to 3.

## Spike: Automate's SSRF handler (Task 4 Step 3)

- `Umbraco.Automate.Core.Security.SsrfProtectionHandler` is **not public** (checked by reflection on 18.5.1: `IsPublic = false`; its `Create(allowProxy, allowAutoRedirect)` is the only factory). The senders therefore use Automate's public `Constants.HttpClients.NoRedirect` named client, which is built on that handler with redirects off. **[verified]**
- **Residual:** that client keeps the framework's default HTTP client logging, so a host that enables `System.Net.Http.HttpClient` logging at Information would log the request path, and a Vercel or Netlify hook id is in the path. Umbraco's default Serilog config sets `System` to Warning, so a default site is unaffected. Removing the loggers would change Automate's own HTTP Request action, so the package does not. For the final security-analyst pass; the README (Task 10) says to keep that category above Information.
- Automate's client has not been exercised against a real redirect or a private address in this repo (the tests use a recording handler). Task 13 covers a real call.

## Rulings made in M1 (Tasks 4 to 6)

- **Milestone grouping:** the plan names no milestones. M1 = Tasks 4 to 6 (senders, connections, coalescing); M2 = Tasks 7 to 9 (actions, leak and isolation tests); M3 = Tasks 10 to 12 (README, release workflow, final review). Task 13 is Pascal's.
- **No milestone review for M1:** the plan names none (the only review is Task 12). The final review is asked to look hard at: the sender secret path, the `Scrubber`, the reflection read in `CredentialNameSavingHandler`, and the SSRF client residual above.
- **Senders return failures as values:** `SendResult.Failure` plus `SendResult.ToException()` (null `InnerException`), not thrown exceptions. Only caller cancellation escapes a sender.
- **Only the senders call `TryGet`:** the plan says so, and Jira's source check calls `TryGet`. `DeployHooksCredentialSourceCheck` instead applies the store's guard (`TryGetExposingPrefix`) and name rule (`TryNormaliseName`) and then `KeyFor`, so the Task 9 IL scan can hold. Pinned in the contract suite's C28 and C31 lists with reasons.
- **Test connection goes through the senders:** `IDeployHookSender.Check` (no request) and `IRevalidateSender.TestAsync` (the contract's test body, plus the admin-only unlisted-host warning), so the connection types never read a credential.
- **`CredentialNameSavingHandler` reads `ConnectionSavingNotification.Target` by reflection:** Automate 18.5.1 declares it protected and exposes nothing public. A test fails if the member disappears. If it can't be read the handler fails open with a Warning, because the senders and Test connection still apply the name rule and never echo the name. Cost if wrong: a pasted secret could be stored in the connection until the next Test or run refuses it.
- **Provider dropdown:** stored as a string (`"Vercel"`, `"Netlify"`, `"CloudflarePages"`) with `Umb.PropertyEditorUi.Dropdown` and an `items` config guessed from Umbraco's dropdown editor. **[PENDING]** confirm it renders on the demo site (Task 13 or earlier); the connection type parses the string either way.
- **`Scrubber`:** scrubs the whole value, host, path and the last two path segments of 8+ characters. An earlier "any segment of 6+ characters" rule mangled the fixed text ("deploy"), caught by `Deploy_FailureStatus_IsClassified`.
- **Coalescing retries:** a failed send retries at 5 s and 30 s (three attempts) for every category, as design 4.3 says; shutdown flushes once with no retries and cancels back-off waits.

## Rulings made in M2 (Tasks 7 to 9)

- **Actions resolve services per run from a scope** (Jira's pattern): the senders and dispatcher are internal, so a public action can't take them as constructor parameters. The coalescing delegate captures the singleton sender, which outlives the scope.
- **A coalesced step fails at once on a bad credential:** `IDeployHookSender.Check` already existed; `IRevalidateSender.Check` was added in Task 8 (the same resolve steps, no request, no secret in the message). Otherwise a wrong credential name would fail minutes later in a log nobody reads. Cost if wrong: none; a run that would have queued now fails.
- **`CoalesceSeconds` is clamped to 0 to 600 and `MaxWaitSeconds` to 1 to 3600 in the actions**, because the connection setting has no validation of its own and the ceiling must be positive.
- **Output `Status` is a string** (`Sent`, `Queued`, `Joined`), not an enum, so bindings and run data show plain text. A revalidate with nothing left to send reports `Sent` with no `StatusCode`, per design 3.4.
- **Dropped-value warnings are aggregated:** one value gives "A path was dropped: not a site path" (the design's text); several give "N paths were dropped: not site paths". Values are never echoed.
- **Path cleaning keeps the path as given** (no decoding, so `/%2Fx` stays), cuts query and fragment, and drops any backslash, control character, `//host` form, non-http(s) scheme, empty result or path over 1,024 characters. An absolute URL with no path becomes `/`. A tag such as `https://evil.example` is a valid tag (`:`, `/` and `.` are allowed); it travels in the body and can't change the destination.
- **Reason** is only measured: the run log gets its length. Over 200 characters is not an error (data never fails a step).
- **Leak tests mutate-checked:** forwarding `HttpRequestException.Message` from `HttpSend` makes `Sentinels_NeverInStepOutputs_...(network-exception)` fail; the mutation was reverted.
- **The IL scan** (`OnlySenders_CallTryGet`) asserts the callers are exactly the two senders (including their async state machines), so an empty scan can't pass.

## Spike: Content Published runs and the trigger output (Task 8 Step 3)

Not run on the demo site: the demo does not reference `Umbraco.Automate`, and wiring it, a licence, document types, a tree of 11 pages and an automation by hand is a Pascal-side session. **Read statically instead**, by decompiling `Umbraco.Automate.Core` 18.5.1 (ilspycmd). **[verified by reading the source; runtime count PENDING]**

- **Runs:** `ContentPublishedTrigger.MapEvent` yields one `TriggerEvent` per entity in `ContentPublishedNotification.PublishedEntities`, so "publish with descendants" of a page with 10 children is expected to start **11 runs** (one per item), each with its own idempotency key. A publish-with-descendants raises a single notification holding every published item (Umbraco core behaviour, not re-checked here).
- **Trigger output has no `url`:** `ContentPublishedTriggerOutput` holds `ContentKey`, `ContentName`, `ContentTypeKey`, `ContentTypeAlias` and `Cultures` only. The design's `${ content.url }` example does not exist. A path comes from a **Get content** step, whose `GetContentOutput.Url` is the item's URL (culture aware). The Paths field description and the README (Task 10) say so.
- **There is also `umbracoAutomate.contentBatchPublished`:** one event per notification with an `Items` list and a `Count`. A revalidate step behind it runs once, but it has no item-level path either, so it still needs Get content per item (a For each loop). Coalescing remains the way to turn the 11 runs of the per-item trigger into one call. Worth stating in the README.
- **PENDING (Pascal, optional):** confirm the 11-runs figure on a real site during Task 13.

## Rulings made in M3 (Tasks 10 to 12, final review)

- **Tags and versions:** publishing follows Jira's `publish.yml`: a `vX.Y.Z[-suffix]` tag, a `## X.Y.Z` heading in `CHANGELOG.md`, and `build/pack.sh`. The plan's `release/<minor>.<patch>` tags are superseded by the portfolio README. `build/check-release-version.sh` was removed (the version comes from the tag, so there is no version file to check). The tag ruleset and the nuget.org Trusted Publishing policy must cover `v*`.
- **Final review fixes (each test-first):** a coalescing timer that fired a few ms before the wall clock was dropped and the batch never sent (it now re-arms); only transient failures are retried, and a 2xx with an unreadable body is a success (a deploy hook POST is not idempotent); an inline revalidate is capped at `MaxBatchItems`; tests now pin that both senders ask for Automate's `NoRedirect` client and that the hosted service is the dispatcher singleton.
- **Accepted residual (security-analyst H1, High):** the package shares Automate's HTTP client, so the .NET HTTP client logs the hook path at Information and records `url.full` in traces for any listener (OpenTelemetry, Azure Monitor, Application Insights). The query is redacted, the path (which holds the hook ID) is not. The leak tests use a bare `HttpClient` over a recording handler, so they cannot see either. A real fix is the package's own named client (`community.deployHooks`, `RemoveAllLoggers`) over its own SSRF-checking `ConnectCallback`, which is a design 4.4 change and security-sensitive code. Ruling: document it (README and SECURITY.md) and ship 18.0.0 with the residual; **Pascal decided on 2026-11-13: ship 18.0.0 with the residual documented**; the own client is a later minor if asked for. Cost if wrong: anyone who can read exported telemetry can copy a hook URL and start builds.
- **Not tested against real Automate (QA F4, security L3):** nothing runs the package inside a booted Umbraco and Automate. The saving handler's assumed settings key (`credentialName`), the Provider dropdown's stored shape and attribute discovery are covered by unit tests only. If the key differs, every connection save is refused (fails closed). Close in Task 13.

## Task 13: run on a real Umbraco, Automate and Vercel (2026-11-13)

Demo site (Umbraco 18.3.x, Automate 18.5.x, SQLite) against a throwaway Next.js 15 site on Vercel Hobby. **[verified]** unless tagged.

- **Real Automate accepts the package.** Both connection types appear, the Provider dropdown shows Vercel, Netlify and Cloudflare Pages, and a save with `credentialName` is accepted. Both actions appear under "Deploy hooks" in the action picker. Closes QA F4 and security L3 for the save path.
- **Test connection.** Deploy hook: "Credential found and the host is a known Vercel host. The hook was not called." Revalidate: "Credential found and the endpoint accepted the test call. Nothing was revalidated", plus the host warning when `AllowedRevalidateHosts` is empty. No URL in either.
- **Trigger output.** The Content Published binding list is `contentKey`, `contentName`, `contentTypeKey`, `contentTypeAlias`, `cultures`: no `url`. Get Content exposes `url`, which is what the README says to bind.
- **Runs.** One run per published item (8 runs for 8 publishes). Coalesced steps complete in tens of milliseconds, an inline Get Content plus enqueue about half a second. **[PENDING]** the 11-runs figure for a page with 10 changed children: Umbraco's Publish does not republish unchanged descendants, so it was not reached.
- **Revalidate without a rebuild.** One publish of a page refreshed both the path and the tag on the live site about 14 seconds later (10 s coalescing plus the next request), with no deployment.
- **Coalescing.** Six publishes in about 50 seconds produced one new Vercel deployment (about 80 seconds after the last, with a 60 s window).
- **Deleted hook.** With `Coalesce seconds` 0 the run fails in about 5 s with "The deploy hook was deleted. Create a new one and update the credential." and no URL.
- **No leak.** The revalidate secret, the full hook URL, the hook ID and the revalidate URL are in no Umbraco log file and nowhere in the demo database (WAL included), across both successful and failed runs. Serilog's default overrides keep `System.Net.Http` at Warning, so the accepted telemetry residual (H1) was not exercised.
- **Vercel Deployment Protection looks like a wrong secret.** A project with Vercel Authentication on answers 401 before the route runs, so the package reports "The endpoint refused the secret." The README now says so. A distinct message is not possible without parsing the body.
- **Demo fixes found by the first Release checks run:** the demo needed `Umbraco.Automate` and the `OpenTelemetry.Api` pin, and `Umbraco:Automate:UseNamedConnectionString`. Fixed in #2.
- **Still unverified:** Umbraco Cloud per-environment credentials; real Netlify and Cloudflare Pages hosts; a real private-address or redirect check on Automate's client; the 11-runs figure.

## Release sign-off for 18.0.0 (2026-11-14)

`security-analyst` (fresh context, full read of source, release path and claims). Verdict: **approve with conditions**; no Critical or High finding apart from the accepted telemetry residual. The release path (tag handling, `id-token` scope, `nuget` environment, rulesets, Actions permissions) had no finding.

- **Medium, retry of a timed-out deploy POST** (`CoalescingDispatcher`): a timeout can mean the provider already started the build, so a batch can start up to three. **Ruling: documented, not changed** (README, SECURITY.md). Why: a missed deploy leaves published content off the live site, which is worse than a bounded duplicate build (at most 3 per window, per process). Cost if wrong: extra build minutes on a slow provider. The alternative, never retrying a deploy on Timeout or Unknown, is a one-line change plus tests and is the first candidate for 18.0.1 if anyone reports duplicate builds.
- **Low, SECURITY.md misdescribed the source check:** corrected. Test connection shows the host and missing-credential names to anyone who can run it, not only admins: corrected.
- **Deferred (Low or Info), none blocks release:** `%2f`, `%5c` and `%2e` in a hook path pass `HostRules` (config-author only; same provider host); dispose race on the retry token source (known, F6); no upper bound on `TimeoutSeconds` (known, F9); `release-checks.yml` lacks `persist-credentials: false` and pins no SQL Server image digest (manual workflow, read-only token); publish job's `setup-dotnet` floats `10.x`.
- **Not verified by the sign-off:** Automate's no-redirect client blocking private addresses and redirects (load-bearing because the revalidate host defaults to any https host: set `AllowedRevalidateHosts`); real Netlify and Cloudflare Pages hook hosts; the integration tests; the telemetry residual.


## Credential helper re-copied at 1.0.2

- **Tag used:** `credential-helper-1.0.2` (Jira repo, commit `af52800`). A test-only patch of 1.0.1: I07 becomes an IL member-reference scan with 37 pins scoped to Automate minor versions, and I05b and I05c are new. The store is behaviourally identical; only its version marker changed. **[verified]**
- **Hash:** `CanonicalHash` is `4067bab2…d78cd`; `build/check-contract-copy.sh` now points at the 1.0.2 tag and the same hash, and reports no diff. **[verified]**
- **Conformance suites:** `--filter "ContractId~C|ContractId~I"` passes 60 of 60 on the 18 line, on the floor leg (locked restore) and on the latest leg (`-p:AutomateFloat=true`). C17 (hash), C18 (version), I05b, I05c and I07 each pass on their own. The full floor run passes (256 unit, 2 integration). I07 needed no new pin for DeployHooks. **[verified]**
- **Line 17:** the same filter passes 60 of 60 on the floor leg (Automate 17.5.1) after rebasing onto the merged 17 line. The latest leg on 17 is left to the PR's `build (17, latest)` check. **[verified]**

## Manual check on the 17 line (2026-11-14)

Booted demo on Umbraco CMS 17.8.0 and Automate 17.5.1 (HTTPS, SQLite, throwaway user and secrets, fake revalidate credential for a made-up name). Checked in the backoffice.

- Both connection types appear (`deployHook`, `nextRevalidate`). The deploy hook form shows the Provider dropdown (Vercel default). **[verified]**
- Saving a connection with a valid credential name works, so the saving handler's reflection read of `Target` succeeds on 17. **[verified]**
- Saving a URL-shaped credential name is refused: the server log shows `Connection creation was cancelled by a notification handler` and nothing echoes the value. Automate 17's controller turns the cancel into an unhandled `OperationCanceledException`, so the backoffice shows a generic "fatal server error" and not the refusal text. Cosmetic, and not specific to this package. **[verified]**
- Test connection on a deploy hook with no credential set: "No value is configured at Umbraco:Automate:DeployHooks:Credentials:demo:DeployHookUrl". It names the key only. **[verified]**
- Test connection on the revalidate connection, pointed at `https://example.com/api/revalidate`: "The endpoint rejected the request (405)." A real POST reached the host and no secret appears in the message, so Automate 17.5.1 registers the no-redirect client name the package uses. **[verified]** It does not show that the client refuses redirects or private addresses (see the sign-off conditions below). **[PENDING]**
- Leak scan: the fake bearer secret, the demo password and the fake URL-shaped name appear zero times in the server log, the trace logs, the SQLite database and its WAL. **[verified]**
- **Not done:** the trigger and step bindings (`trigger.contentKey`, `steps.getContent.url`) and a run record. A workspace needs a service account key (an API user), user groups and an automation built in the designer, which was not set up. The bindings are unchanged source shared with 18 and covered by unit tests. **[PENDING]**

## Delta review for the 17 line

`security-analyst` (fresh context). Verdict: **approve with conditions** for the code. The only `AUTOMATE_17` guard is in a test, so production code has no line branch and no control differs between the lines.

- **High, release path:** `publish.yml` rejects a tag whose major is not the newest line, and one `v18.M.P` tag packs both lines. So there is no `v17.0.0`: 17 ships as `17.M.P` from the next `v18.M.P` tag. **Ruling: tag `v18.0.1`**, which ships `18.0.1` (no functional change) and `17.0.1` together. Why: it needs no workflow change, and a change to the publish path would need its own security re-review. Cost if wrong: one extra 18 patch version on nuget.org.
- **Low, reflection read:** it fails open with a Warning when the member goes missing, and closed when `credentialName` is missing or not a string. The `Target` member comes from Umbraco CMS (`ObjectNotification<T>`), not Automate: the code comment is corrected.
- **Residual, same as 18:** Automate's no-redirect client is not proven to refuse redirects and private addresses (no decompiler; no repo test). `AllowedRevalidateHosts` defaults to any https host, so production sites should set it (the README says so). Follow-up: an integration test on both lines (a 3xx comes back, and a call to 127.0.0.1 is refused).
- **Low:** `check-vulnerable.sh` audits whichever line was restored last. The CI order is correct today. Follow-up: take the line as an argument.
- **Conditions:** green `build (17, *)`, `deps` and `osv` on the release commit; `gh release create` with `--latest=false` for any release that should not become Latest; tell 17 users to set `AllowedRevalidateHosts`.

## Outbound client check on both lines (2026-11-16)

Closes the residual from the 17 delta review (and from the 18 sign-off): `OutboundHttpClientTests` resolves Automate's named `NoRedirect` client, the one the senders use (`HttpSend.ClientName` is the same constant), from Automate's real `AddUmbracoAutomateCore` registration. On Automate 17.5.1 and 18.5.1, and on the latest floating legs of both lines:

- A GET to a loopback listener fails without a timeout and the listener sees zero connections. **[verified]**
- A GET to a private literal (`10.0.0.1`) fails at once, not by timing out. **[verified]**
- The client's primary handler has `AllowAutoRedirect == false`. **[verified]** A real redirect hop is not exercised, because it needs a public target.
- The test builds the container from Automate's registration, not from a full Automate-booted site, because the integration host never runs that registration. **[verified]**
- `AllowedRevalidateHosts` still defaults to any https host, so production sites should set it.

Release checks now run SQL Server integration and e2e once per line in `eng/lines.txt` (18 and 17), floor and latest for e2e. Dispatched on the PR branch, all six jobs passed: SQL Server integration on 17 and 18, and e2e on 17 and 18 at the floor and at the latest. **[verified]**

## Vulnerability audit now covers each line (2026-11-16)

- **Finding:** `dotnet list package` runs its own implicit restore with the default line (18), so every earlier `check-vulnerable.sh` call audited the 18 graph, including the `deps` step labelled 17 and the per-line audit in `publish.yml`. **[verified]** (after `--line 17` the assets file held Automate 17.5.1 only once the list step stopped restoring).
- **What still covered 17:** `osv-scanner` scans the four `packages.17.lock.json` files explicitly on every PR, and those are the floor versions the 17 package depends on. It passed for `17.0.1`. **[verified]**
- **Fix:** `build/check-vulnerable.sh --line N` does the locked restore for that line and lists with `--no-restore`; `build.yml` loops over `eng/lines.txt` and `publish.yml` passes the line it is packing.
- **Result:** `--line 17` and `--line 18` both report no vulnerable packages in 4 projects, with the right Automate version in the assets file each time (17.5.1 and 18.5.1). **[verified]**
