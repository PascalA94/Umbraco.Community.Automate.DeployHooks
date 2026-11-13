# Changelog

Versions follow `<Umbraco major>.<minor>.<patch>`. Before tagging `vX.Y.Z`, rename "Unreleased" to `## X.Y.Z` (the publish workflow looks for that exact heading line).

## 18.0.0-beta.1

- Connections `community.deployHooks.deployHook` (Vercel, Netlify, Cloudflare Pages) and `community.deployHooks.nextRevalidate`, on package-owned credentials (`Umbraco:Automate:DeployHooks:Credentials:{name}`).
- Actions `community.deployHooks.triggerDeploy` and `community.deployHooks.revalidate`.
- Per-connection coalescing of bulk publishes, with a ceiling, retries and a flush on shutdown.
- `appsettings` section `Umbraco:Automate:DeployHooks` (`TimeoutSeconds`, `MaxItemsPerCall`, `MaxBatchItems`, `AllowedRevalidateHosts`, `ProviderHosts`).
