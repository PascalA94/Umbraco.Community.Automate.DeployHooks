# Security Policy

## Supported versions

| Package version | Umbraco | Supported |
|---|---|---|
| 18.x | 18 (18.3.0 or later) | Yes |

Security fixes are released as patch versions on each supported line.

## Reporting a vulnerability

Please **do not** open a public issue for a security problem.

Report it privately through GitHub's private vulnerability reporting: go to the repository's **Security** tab
(https://github.com/PascalA94/Umbraco.Community.Automate.DeployHooks/security) and choose **Report a vulnerability**.

Please include the affected package version and Umbraco CMS version, steps to reproduce or a proof of concept, and the
impact as you understand it. You should get an acknowledgement within 5 working days. Once a fix is released, the
advisory is published with credit to you, unless you would rather not be named.

## What the package protects, and what it does not

The package calls deploy hooks and a revalidation endpoint on your behalf. A hook URL and a revalidate secret are
credentials.

What it does:
- Keeps the hook URL, revalidate URL and revalidate secret in configuration under
  `Umbraco:Automate:DeployHooks:Credentials`, outside the database. A connection stores only a name.
- Reads the values in two places only (the two senders, and the credential source check that Test connection runs, which loads a value into memory only to find which
  source holds it and then discards it),
  and never writes them to a step setting, output, run record, the package's own log lines, error message or export.
  Leak tests cover each failure path through the package's code. They do not cover the HTTP client's own logging and
  tracing (see below). Test connection shows the revalidate host, and the names of missing credentials, to anyone who can run it.
- Sends only to a known provider host (checked on the resolved URL, `https` only, no userinfo), or to a revalidate
  host in `AllowedRevalidateHosts` when you set it. Never follows a redirect. Reads at most 1,024 bytes of an answer.
- Cleans bound paths and tags, and never echoes a dropped value.
- Bounds memory (one pending batch per connection and kind, capped) and limits cost amplification: a flood of publishes
  gives about one call per window per process.

What it does not do:
- **It cannot stop an operator widening Automate's `AllowedConfigurationKeyPrefixes` over `Umbraco:Automate`.** That
  would let any automation author read the credentials through the HTTP Request action. The package detects it
  (a Critical log at start-up, and the store refuses to read) but cannot prevent the exposure. If it happens, rotate
  every credential and purge the automation run records. Changes to the list need a restart.
- It does not share coalescing state between servers or survive a crash with a batch pending.
- Coalesced failures are logged at Warning and are not in the run log.
- Whether Umbraco Cloud resolves a separate credential per environment has not been verified.
- **The hook URL path can reach host logs and telemetry.** The .NET HTTP client logs the request path at Information
  (a hook ID is in the path), and its tracing sets `url.full` (query redacted, path not) for any listener such as
  OpenTelemetry, Azure Monitor or Application Insights. Umbraco's default log level is fine, but exported dependency
  telemetry is not covered: filter or redact the provider hook hosts, or restrict who can read it. The package shares
  Automate's HTTP client and does not turn this off. Recorded as an accepted residual for 18.0.0.
- The outbound protection (no redirects, blocking private addresses) is Automate's, through its no-redirect client. A
  test pins that the package asks for that client, but the protection itself has not been exercised here.
- Deploy hook calls are not idempotent. Only transient failures are retried, but a timeout can mean the provider already
  started the build, so one coalesced batch can start up to three builds (the first call and two retries). A host that adds
  resilience handlers to every HTTP client can multiply requests further.
- Credential names are site-wide: anyone who can create an Automate connection can use any configured credential.

## Rotating a leaked credential

A leaked **hook URL** lets anyone start builds (cost and deploy loops). A leaked **revalidate secret** lets anyone
refresh your cached pages. Rotate, then update the configuration value. Credentials are read on each call, so no restart is needed (only allow-list and `ProviderHosts` changes need one).

| Credential | Rotate |
|---|---|
| Vercel deploy hook | Project, Settings, Git, Deploy Hooks: delete the hook and create a new one |
| Netlify build hook | Site configuration, Build and deploy, Build hooks: delete the hook and create a new one |
| Cloudflare Pages deploy hook | Settings, Builds, Deploy hooks: delete the hook and create a new one |
| Revalidate secret | Generate a new random value, set it on the Next.js site (for example `REVALIDATE_SECRET`) and in the credential, and deploy both |

If the old value could have reached a run record or a log, purge those as well.

## Scope

In scope: this package's code. Out of scope: vulnerabilities in Umbraco CMS or Umbraco Automate themselves; report
those to Umbraco through its
[security reporting process](https://umbraco.com/trust-center/security-and-umbraco/how-to-report-a-vulnerability-in-umbraco/).
