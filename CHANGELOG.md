# Changelog

Versions follow `<Umbraco major>.<minor>.<patch>`. Before tagging `vX.Y.Z`, rename "Unreleased" to `## X.Y.Z` (the publish workflow looks for that exact heading line).

## Unreleased

- Tests and CI only: an integration test that Automate's no-redirect client refuses loopback and private destinations and does not follow redirects, and Release checks now cover every line in `eng/lines.txt`.

## 18.0.1

One tag packs both lines, so this release ships `18.0.1` and `17.0.1` together. `18.0.1` has no functional change from `18.0.0`.

- New `17.0.1` package for Umbraco CMS 17 and Umbraco Automate 17 (`Umbraco.Cms.Core [17.8.0, 18.0.0)`, `Umbraco.Automate.Core [17.5.1, 18.0.0)`). Same connections, actions, coalescing and settings as 18.
- Credential helper contract 1.0.2 (test-only; the store's behaviour is unchanged).

## 18.0.0

- Connections `community.deployHooks.deployHook` (Vercel, Netlify, Cloudflare Pages) and `community.deployHooks.nextRevalidate`, on package-owned credentials (`Umbraco:Automate:DeployHooks:Credentials:{name}`).
- Actions `community.deployHooks.triggerDeploy` and `community.deployHooks.revalidate`.
- Per-connection coalescing of bulk publishes, with a ceiling, retries and a flush on shutdown.
- `appsettings` section `Umbraco:Automate:DeployHooks` (`TimeoutSeconds`, `MaxItemsPerCall`, `MaxBatchItems`, `AllowedRevalidateHosts`, `ProviderHosts`).
