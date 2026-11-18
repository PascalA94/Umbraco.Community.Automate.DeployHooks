# Changelog

Versions follow `<Umbraco major>.<minor>.<patch>`. A `v19.*` tag ships line 19 only; a `v18.*` tag ships 18 and 17 together. Before tagging `vX.Y.Z`, rename "Unreleased" to `## X.Y.Z` (the publish workflow looks for that exact heading line).

## 19.0.0-rc.1

Built against Umbraco 19.0.0-rc1, not yet verified on 19 stable. Release candidate: line 19 only, no Marketplace listing.

- Umbraco 19 line (`Umbraco.Cms.Core [19.0.0-rc1, 20.0.0)`, `Umbraco.Automate.Core [19.0.0-rc1, 20.0.0)`, .NET 11). Same connections, actions, coalescing and settings as 18.
- `Dropdown.Single` for the provider field on 19 (the plain dropdown is multi-select there).
- Credential helper contract 1.0.3 (test-only; the store's behaviour is unchanged).

## 18.0.2

One tag packs both lines, so this release ships `18.0.2` and `17.0.2` together. No functional change from 18.0.1 and 17.0.1.

- Raised the Umbraco CMS floors to 17.8.2 and 18.3.2, above GHSA-348f-9mg8-rhv4 (Examine dashboard and Management API could show sensitive member values; this package does not use them). The 17 and 18 packages now require `Umbraco.Cms.Core [17.8.2, 18.0.0)` and `[18.3.2, 19.0.0)`.
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
