# CLAUDE.md

`Umbraco.Community.Automate.DeployHooks`: Automate actions that trigger Vercel, Netlify and Cloudflare Pages deploy hooks and Next.js revalidation, with coalescing of bulk publishes.
Plain class library in `src/Umbraco.Community.Automate.DeployHooks/` (no client code, controllers or Razor). Tests: `src/*.Tests` (xUnit), `src/*.IntegrationTests` (NUnit, SQLite), `tests/e2e`.
Design `docs/design.md`, plan `docs/plan.md`, spikes `docs/spikes.md`. Maintainers keep local, untracked notes (`COMMITS-TODO.md`, `HANDOVER.md`) on where work stopped.

**Never commit, push or publish.** Pascal commits, pushes, tags and releases.


## Lines and versions

- One codebase, three Automate lines: `-p:AutomateMajor=17|18|19` (default 18) picks the Automate and CMS ranges, the version, the `AUTOMATE_17`/`AUTOMATE_18`/`AUTOMATE_19` constant, the TFM (net11.0 on 19, net10.0 otherwise) and the lock file (`packages.<line>.lock.json`). `eng/lines.txt` lists the lines that build and ship (18, 17 and 19). 19 builds with the .NET 11 SDK (put it first on PATH), 17 and 18 with the .NET 10 SDK. The 18 ranges are `Umbraco.Cms.Core [18.3.2, 19.0.0)` and `Umbraco.Automate.Core [18.5.1, 19.0.0)`; the 17 ranges are `[17.8.2, 18.0.0)` and `[17.5.1, 18.0.0)`; the 19 ranges are `[19.0.0-rc1, 20.0.0)` for both. Adding a line means adding it to `eng/lines.txt`, committing its lock files, adding them to the `osv` job in `build.yml` and adding its `build (<line>, floor|latest)` checks to the `main` ruleset. A `v18.M.P` tag packs 17 and 18 (a 17 package ships as `17.M.P` from the 18 tag); a `v19.M.P[-suffix]` tag packs line 19 only. `publish.yml` rejects any other major.
- `-p:AutomateFloat=true` is the CI latest leg: floating `<line>.*`, no lock file, never committed.
- Versions: `publish.yml` derives `<line>.<minor>.<patch>[-suffix]` from the tag `vX.Y.Z[-suffix]` and passes it to `build/pack.sh`; `Directory.Build.props` only holds the defaults. The tag needs a `## X.Y.Z` heading in `CHANGELOG.md`.
- The NuGet ID, Automate aliases (`community.deployHooks.*`), settings and output property names are permanent once released.
- The credential helper (`CredentialHelper/DeployHooksCredentialStore.cs` and the two suites in `*.Tests/CredentialHelperContract/`) is a verbatim copy from the Jira repo at `credential-helper-1.0.3`. Never edit it; `bash build/check-contract-copy.sh` proves it.

## Commands

```bash
bash build/test.sh                  # restore, build and test every line in eng/lines.txt (SQLite); add `-- --filter Name` for one class
dotnet restore src/Umbraco.Community.Automate.DeployHooks.slnx --locked-mode   # CI floor restore
bash build/check-contract-copy.sh   # credential helper copy vs the Jira tag (JIRA_SOURCE=<path> to use a local clone)
# SQL Server integration run: set Tests__Database__DatabaseType=SqlServer and Tests__Database__SQLServerMasterConnectionString
# Demo site: HTTPS only (OpenIddict refuses HTTP)
dotnet user-secrets set "Umbraco:CMS:Unattended:UnattendedUserPassword" "<pw>" --project demo/Umbraco.Community.Automate.DeployHooks.DemoSite
dotnet dev-certs https && dotnet run --project demo/Umbraco.Community.Automate.DeployHooks.DemoSite   # https://localhost:44331/umbraco
# e2e (demo running; env vars as in tests/e2e/.env.example)
cd tests/e2e && npm ci --ignore-scripts && npx playwright install chromium && npx playwright test --reporter=line
bash build/check-vulnerable.sh --line 17   # restores that line (locked) and audits it; a bare call audits 18 only
```

## Releasing

- Add the `## X.Y.Z` heading to `CHANGELOG.md`.
- `bash build/check-vulnerable.sh --line <line>` exits 0 for every line, and `bash build/pack.sh 18 <dir> <minor> <patch>` then `bash build/check-packed-readme.sh <dir>/*.nupkg` pass.
- Run the **Release checks** workflow by hand on the release branch (SQL Server integration, and e2e if present). Never on PRs. Re-run after a new Umbraco minor.
- Squash-merge, tag `vX.Y.Z` on the merge commit; `publish.yml` publishes with Trusted Publishing. Then `gh release create`, and mark the newest Umbraco line's release as Latest.
- Stable from the first real public release unless the API is still moving (`.github/REPO-SETUP.md` step 10); after that, no breaking changes within a major.
- "Done" means green on GitHub. The first CI run is its own step with Pascal.

## Keep

- Floors never below 17.8.2 / 18.3.2; the 19 line floor is 19.0.0-rc1 until 19.0.0 ships. The repo is public: no reviews, plans of record or local paths in it.
- `docs/` holds only `design.md`, `plan.md`, `spikes.md`. Fake test secrets carry `// gitleaks:allow` from their first commit.
