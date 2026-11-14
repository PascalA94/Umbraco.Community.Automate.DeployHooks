# Changelog

Versions follow `<Umbraco major>.<minor>.<patch>`. Before tagging `vX.Y.Z`, rename "Unreleased" to `## X.Y.Z` (the publish workflow looks for that exact heading line).

## Unreleased

- Credential helper contract 1.0.2 (test-only; the store's behaviour is unchanged).

## 17.0.0

- Same connections, actions, coalescing and settings as 18.0.0, built against Umbraco CMS 17 and Umbraco Automate 17 (`Umbraco.Cms.Core [17.8.0, 18.0.0)`).

## 18.0.0

- Connections `community.deployHooks.deployHook` (Vercel, Netlify, Cloudflare Pages) and `community.deployHooks.nextRevalidate`, on package-owned credentials (`Umbraco:Automate:DeployHooks:Credentials:{name}`).
- Actions `community.deployHooks.triggerDeploy` and `community.deployHooks.revalidate`.
- Per-connection coalescing of bulk publishes, with a ceiling, retries and a flush on shutdown.
- `appsettings` section `Umbraco:Automate:DeployHooks` (`TimeoutSeconds`, `MaxItemsPerCall`, `MaxBatchItems`, `AllowedRevalidateHosts`, `ProviderHosts`).
