# Design: Automate deploy hooks and Next.js revalidation

| | |
|---|---|
| Package | `Umbraco.Community.Automate.DeployHooks` |
| Owner | Pascal Meyer (`PascalA94`) |
| Alias prefix | `community.deployHooks.` (frozen at first release) |
| Size | S, about 4-7 h agent time, v18 line only |
| Status | Draft design, 2026-11-04. Nothing built |
| Inputs | `scope.md`, `../README.md`, `../credential-helper-contract.md` (1.0.0, Frozen), `../04-automate-jira/m0-result.md`, `../03-automate-teams/design.md` |

Tags: **[verified]** read or fetched in a session; **[unverified]** recalled or inferred, check while building; **[DATED]** true on 2026-11-04 and likely to move.

---

## 1. Intent and goals

Tell a headless frontend that content changed, from an Automate automation:
- trigger a **Vercel, Netlify or Cloudflare Pages deploy hook** (a full rebuild);
- call a **Next.js on-demand revalidation** route (`revalidatePath` / `revalidateTag`) for just the changed pages;
- **coalesce bulk publishes**, so 50 published pages give one rebuild, not 50.

Real use case: a headless Next.js site (stack to confirm, Q1).

**Goals**
- A typed, package-owned secret for each hook URL and revalidate secret. Nothing secret in the database.
- One rebuild per burst, per connection, with a configurable window.
- Revalidation paths and tags built from bound content values, cleaned and capped.
- Clear errors mapped to Automate categories.

**Non-goals (1.0)**
- Deploy-status polling ("Wait for deployment", Vercel or Netlify API token). Later, if asked (Q3).
- Generic webhooks to any host. That's the built-in HTTP Request action.
- Gatsby, Nuxt, Astro or other revalidation shapes. The revalidate contract is ours (4.5), so any framework can implement it.
- Shared coalescing state across servers.

**Anti-goals**
- Never a worse secret story than HTTP Request: no hook URL in step settings, run data, logs or exports.
- Never let site data fail a step (the circuit breaker turns a flood into an off switch, Teams F01). Bad paths are dropped with a warning.

---

## 2. Constraints

- **v18 line first.** `eng/lines.txt` = `18`. One codebase with Jira's `AutomateMajor=17|18` switch, so a v17 line is a one-line change later.
- **Floors:** `Umbraco.Cms.Core [18.3.0, 19.0.0)` and `Umbraco.Automate.Core [18.5.1, 19.0.0)`. The 17 ranges (`[17.8.0, 18.0.0)` / `[17.5.1, 18.0.0)`) are defined in `Directory.Build.props` but not shipped.
- `net10.0`, plain `Microsoft.NET.Sdk`, no client code, no controllers, no Razor.
- **Secret custody:** the frozen credential helper, copied verbatim from Jira at tag `credential-helper-1.0.0`. Prefix outside `AllowedConfigurationKeyPrefixes`. S7 gate is **GO on both lines** (`m0-result.md`).
- **Pitfalls:** P2 (no statics holding scoped or mutable state; every service a singleton over singleton-safe dependencies), P1 (DI only), P7 (no heavy start-up work), S1/S3/S5 do not apply (no controllers, editors or surface controllers).
- Portfolio CI rules (README "CI and GitHub rules") win over anything here.

---

## 3. MVP spec

### 3.1 Connection: deploy hook

| | |
|---|---|
| Alias | `community.deployHooks.deployHook` |
| Display name | "Deploy hook (Vercel, Netlify, Cloudflare Pages)" |
| Base | `ConnectionTypeBase<DeployHookConnectionSettings>` |

| Setting | Type | Notes |
|---|---|---|
| `Provider` | dropdown `Vercel` / `Netlify` / `CloudflarePages` | Picks the host rule (4.4) |
| `CredentialName` | string, required | Contract name rule. Reads `…:Credentials:{name}:DeployHookUrl` |
| `CoalesceSeconds` | int, default `60`, `0`-`600` | `0` sends inline (3.3) |
| `MaxWaitSeconds` | int, default `300` | A burst never delays the hook longer than this |

**Test connection:** guard, credential, host check. It does **not** fire the hook (a rebuild costs build minutes). Success: "Credential found and the host is a known {Provider} host. The hook was not called." Plus the appsettings-file warning (Teams council condition 7).

### 3.2 Connection: Next.js revalidate

| | |
|---|---|
| Alias | `community.deployHooks.nextRevalidate` |
| Display name | "Next.js revalidation endpoint" |

| Setting | Type | Notes |
|---|---|---|
| `CredentialName` | string, required | Reads `…:{name}:RevalidateUrl` and `…:{name}:RevalidateSecret` |
| `CoalesceSeconds` / `MaxWaitSeconds` | as 3.1, default `10` / `60` | Revalidation is cheap, so a shorter window |

The endpoint URL lives in config, not in the connection, so a backoffice admin can't point the secret at another host. **Test connection** sends `{"paths":[],"tags":[],"test":true}` and expects 2xx (our contract says a test call revalidates nothing).

### 3.3 Action: Trigger deploy hook

| | |
|---|---|
| Alias | `community.deployHooks.triggerDeploy` |
| Settings | `Reason` (string, bindable, optional, max 200 chars, only logged as a length, never sent) |
| Output | `DeployHookOutput`: `Status` (`Sent` / `Queued` / `Joined`), `BatchId` (guid), `StatusCode` (int?, only when `Sent`), `ProviderJobId` (string?, only when `Sent`), `Warnings` (string[]) |

- `CoalesceSeconds = 0`: send now, map errors, `Status = Sent`.
- Otherwise: enqueue (4.3). First request in a window gets `Queued`, later ones `Joined`, all with the same `BatchId`. The step succeeds at once.

### 3.4 Action: Revalidate Next.js paths and tags

| | |
|---|---|
| Alias | `community.deployHooks.revalidate` |
| Settings | `Paths` (multi-line, bindable, one per line, e.g. `${ content.url }`), `Tags` (multi-line, bindable) |
| Output | `RevalidateOutput`: `Status`, `BatchId`, `StatusCode?`, `PathCount`, `TagCount`, `Warnings` |

Cleaning (data can't fail the step):
- Path: an absolute URL is cut to its path; query and fragment dropped; must start with `/`; max 1,024 chars; no control characters. Anything else is dropped with "A path was dropped: not a site path" (value never echoed).
- Tag: `^[A-Za-z0-9:_\-./]{1,256}\z` [unverified: Next.js tag max 256]. Others dropped with a warning.
- Union per batch, de-duplicated, ordinal. Over `MaxItemsPerCall` (default 100) splits into several calls.
- Nothing left after cleaning: succeed, `Status = Sent`, no request, warning "Nothing to revalidate".

### 3.5 Triggers

None. Automate's own Content Published / Unpublished triggers drive the actions.

### 3.6 Acceptance criteria

- **Given** a Vercel connection with a 60 s window, **when** 50 pages publish within 20 s, **then** exactly one POST reaches the hook about 60 s after the last one **and** all 50 runs succeed with one shared `BatchId`.
- **Given** a steady trickle of publishes every 30 s, **when** 5 minutes pass, **then** the hook fires at least once per `MaxWaitSeconds`.
- **Given** `CoalesceSeconds = 0`, **when** the hook returns 404, **then** the step fails with `ConfigurationError` "The deploy hook was deleted. Create a new one and update the credential."
- **Given** a credential holding `https://evil.example/hook` on a Vercel connection, **when** I test or run, **then** it fails with `ConfigurationError` **and** no request is sent.
- **Given** paths `https://site.example/news/a?x=1`, `news/b` and `/news/c`, **when** revalidation runs, **then** the body holds `/news/a` and `/news/c` **and** `Warnings` has one dropped-path line.
- **Given** two revalidate steps in one window, **when** they flush, **then** one call carries the union of paths and tags.
- **Given** the site shuts down with a batch pending, **when** `StopAsync` runs, **then** the batch is sent once, within the shutdown timeout.
- **Given** any failure, **when** I read the run log, site logs or export, **then** no hook URL, revalidate URL or secret appears.

---

## 4. Technical design

### 4.1 Layout (mirrors Teams and Jira)

```
src/Umbraco.Community.Automate.DeployHooks/
  CredentialHelper/  DeployHooksCredentialStore.cs (verbatim copy), DeployHooksCredentialPart.cs, DeployHooksCredentialSourceCheck.cs
  Connection/        DeployHookConnectionType.cs (+Settings), NextRevalidateConnectionType.cs (+Settings), CredentialNameSavingHandler.cs
  Actions/           TriggerDeployAction.cs, RevalidateAction.cs, outputs, PathCleaner.cs
  Coalescing/        CoalescingDispatcher.cs (singleton + IHostedService), PendingBatch.cs
  Http/              DeployHookSender.cs, RevalidateSender.cs, HostRules.cs, ErrorClassifier.cs, DeployHooksException.cs, Scrubber.cs
  Configuration/     DeployHooksComposer.cs, DeployHooksOptions.cs
tests/Umbraco.Community.Automate.DeployHooks.Tests/
  CredentialHelperContract/  two verbatim suites + DeployHooksCredentialStoreContractTests.cs
```

### 4.2 Credential helper

| Token | Value |
|---|---|
| `@@Namespace@@` | `Umbraco.Community.Automate.DeployHooks.Credentials` |
| `@@Store@@` | `DeployHooksCredentialStore` |
| `@@Prefix@@` | `Umbraco:Automate:DeployHooks:Credentials` |
| `@@Part@@` | `DeployHooksCredentialPart`: `DeployHookUrl`, `RevalidateUrl`, `RevalidateSecret` |

- Example key: `Umbraco:Automate:DeployHooks:Credentials:mysite:DeployHookUrl`.
- Only the two senders call `TryGet`. The source check calls `KeyFor` (pinned in `PinnedKeyForCallers` with its message) and reads providers (pinned in `PinnedConfigurationReaders`).
- `DeployHooksOptions` is bound from `Umbraco:Automate:DeployHooks`, so it goes in `ParentSectionBoundTypes` and has no catch-all member (C31).
- Pascal adds a row for this package to contract section 2 (doc-only, no version bump).

### 4.3 Coalescing

`CoalescingDispatcher`, one singleton, registered also as an `IHostedService`.
- **Key:** connection ID plus action kind (`deploy` or `revalidate`). Two connections that share a credential name coalesce separately (accepted: rare, documented).
- **State:** a `ConcurrentDictionary<string, PendingBatch>` instance field. `PendingBatch` holds `BatchId`, `FirstAt`, `LastAt`, the path and tag sets, and a timer from the injected `TimeProvider`.
- **Rule:** trailing debounce with a ceiling. Fire at `min(LastAt + CoalesceSeconds, FirstAt + MaxWaitSeconds)`. Each new request moves `LastAt` and re-arms the timer.
- **Swap on fire:** the batch is removed atomically before sending, so a request arriving mid-send starts a new batch.
- **Send result:** logged (status code and counts only). A failed send retries twice with 5 s and 30 s back-off, then logs one Warning "Deploy hook batch {BatchId} failed: {category}". The steps already succeeded, so a coalesced failure is not in the run log. That's the price of coalescing, and the README says to use `CoalesceSeconds = 0` where a visible failure matters.
- **Shutdown:** `StopAsync` flushes every pending batch once, bounded by the host's shutdown token.
- **Memory:** at most one batch per connection and kind. Path and tag sets capped at 2,000 entries each; beyond that, further items are dropped with a log line (count only).
- **Single server:** correct. **Multi-server or load-balanced back offices:** each process coalesces on its own, so N servers give up to N calls per window. Automate runs steps on whichever node picks them up [unverified], so this is a bound, not a guarantee. Documented. Shared state (DB or distributed lock) is later, only if asked.
- **Restart:** a pending batch lost to a crash (not a clean stop) is lost. Documented: the next publish triggers again.

### 4.4 HTTP

- Named client `community.deployHooks`, built like Automate's `NoRedirect` client: Automate's SSRF handler as primary handler [unverified: confirm the handler type is public, else use `Constants.HttpClients.NoRedirect` as Teams does], `AllowAutoRedirect = false`, `HttpCompletionOption.ResponseHeadersRead`, read at most 1,024 bytes of any body.
- Timeout `TimeoutSeconds` (default 15) through a `CancellationTokenSource` on `TimeProvider`.
- **Host rules on the resolved URL**, before any request (lower case, trailing dot stripped, `Uri.IdnHost`, `https` only, no userinfo):

| Provider | Host | Path must start with |
|---|---|---|
| Vercel | `api.vercel.com` | `/v1/integrations/deploy/` |
| Netlify | `api.netlify.com` | `/build_hooks/` |
| Cloudflare Pages | `api.cloudflare.com` | `/client/v4/pages/webhooks/deploy_hooks/` |

[unverified: hosts and paths recalled from provider docs; Task 3 checks them against a real hook.] Overridable only through `DeployHooksOptions.ProviderHosts`, never per connection.
- **Revalidate URL:** `https`, absolute, no userinfo; host must be in `AllowedRevalidateHosts` when that list is set (default empty = any https host, with a Test warning naming the host, admin only). The SSRF handler still blocks private and loopback addresses.
- Requests: deploy hook `POST` with an empty body. Revalidate `POST` JSON `{"paths":[...],"tags":[...]}`, `Authorization: Bearer <secret>`.
- `ProviderJobId`: Vercel answers `{"job":{"id":…}}` [unverified]; take `job.id` only if it matches `^[A-Za-z0-9_-]{1,64}\z`, else null.

### 4.5 Revalidate contract (ours, versioned)

- Version 1, documented in the README with a sample App Router route handler: compares the bearer token with `crypto.timingSafeEqual`, calls `revalidatePath(p)` per path and `revalidateTag(t)` per tag, answers `200 {"revalidated":true}`. A `"test":true` body returns 200 and does nothing.
- [unverified, DATED] Next.js 16 changed `revalidateTag` to take a second argument; the sample targets the current major, and the README says which.
- The JSON shape is frozen like an alias. A new field is additive; a breaking change is a new action.

### 4.6 Error mapping

| Situation | Category | Message (never a URL or secret) |
|---|---|---|
| Guard, bad name, missing part | `ConfigurationError` | Contract texts |
| Host or path rule fails | `ConfigurationError` | "The credential isn't a {Provider} deploy hook URL." |
| 3xx | `InvalidResponse` | "The endpoint answered with a redirect. Check the URL." |
| 400 | `InvalidResponse` | "The endpoint rejected the request (400)." |
| 401 / 403 | `Authentication` | "The endpoint refused the secret." |
| 404 | `ConfigurationError` | "The deploy hook was deleted. Create a new one and update the credential." |
| 429 | `RateLimiting` | "The provider is throttling this hook. Use the step's Retry setting." |
| 5xx / network | `ServiceUnavailable` | "The endpoint returned {code}." / "Couldn't reach the endpoint." |
| Our timeout | `Timeout` | "No answer within {n} seconds." |

Our own exception type, `InnerException` always null, never `HttpRequestException.Message` or `RequestUri` (Teams 7.5).

### 4.7 Logging

- One Information line per send: kind, status code, path and tag counts, `BatchId`. Never the host, URL, secret, paths or tags.
- Paths aren't personal data, but member-area URLs can carry names, so they stay out of logs too.

---

## 5. Security notes

Inherited from Teams and Jira: package-owned credentials, the frozen store and guard, isolation suites I01-I09, no redirects, bounded body reads, scrubbed messages, null `InnerException`, appsettings-source warning, release hardening (trusted publishing, SHA-pinned actions, tag ruleset).

Package risks:
- **Hook URL leak = free rebuilds.** Anyone with it can burn build minutes or keep a site in a deploy loop. Rotation runbook per provider in `SECURITY.md`.
- **Revalidate secret sent to a wrong host.** Mitigated: URL and secret both in config, `https` only, optional `AllowedRevalidateHosts`, SSRF handler.
- **Cost amplification from a flood.** Mitigated: coalescing is on by default and `MaxWaitSeconds` caps the rate at about one call per window per process.
- **Coalesced failures are silent in the run log.** Logged at Warning; documented.
- **Memory growth.** Bounded batches (4.3).
- **Untrusted bound paths.** Cleaned, never echoed, capped.

---

## 6. Test strategy

- Unit (xUnit, `Umbraco.Automate.Testing` 18.5.1): actions through `ActionTestHarness` with pre-bound settings and a recording handler.
- Coalescing on a fake `TimeProvider`: 50 enqueues give one send; ceiling fires on a trickle; mid-send arrivals start a new batch; shutdown flush; retry back-off; caps.
- Host rules: every provider, look-alike and IDN hosts, `http`, userinfo, wrong path.
- Path cleaner: a fixed theory of awkward inputs (control characters, `//evil.example`, `\\`, encoded `%2F`, 2,000-char paths).
- Contract suites C01-C31 and I01-I09, verbatim. Package leak tests: sentinels in every part never reach logs, `ActivityListener` tags, failure text, outputs or `ToString()`; a 307 makes no second request.
- Manual: a Vercel Hobby test project with a deploy hook and the sample revalidate route.

---

## 7. Release

- `0.0.1-alpha` placeholder to reserve the NuGet ID (Pascal), then unlisted.
- First real release `18.0.0-beta.1`, then `18.0.0`.
- NuGet and Marketplace tags: `umbraco`, `umbraco-marketplace`, `automate`, `vercel`, `netlify`, `cloudflare-pages`, `nextjs`, `headless`, `deploy`.
- Repo `PascalA94/Umbraco.Community.Automate.DeployHooks`, **public at scaffold**, with CodeQL, a branch ruleset, gitleaks and secret scanning with push protection. `docs/` holds `design.md`, `plan.md`, `spikes.md` only.

---

## 8. Open questions for Pascal

| # | Question | Default |
|---|---|---|
| Q1 | What does the headless frontend run on (framework, version, host)? Check framework config only | Assume Next.js App Router on Vercel; the sample route handler targets that |
| Q2 | Coalesced mode returns at once and logs failures. Should a step instead wait for its batch and report the real result? | No: return at once. `CoalesceSeconds = 0` gives a visible result |
| Q3 | Add "Wait for deployment" status polling (Vercel/Netlify token) in 1.0? | No: later, if users ask |
