# Contributing

Issues and pull requests are welcome. For anything bigger than a small fix, open an issue first so we can agree the approach.

## Branches

- `main` is the Umbraco 18 line (package 18.x). Another Umbraco line, if there is one, lives on its own branch (for example `v17`).
- Open pull requests against `main`. Fixes that apply to every line are then ported. Code under `src/Umbraco.Community.Automate.DeployHooks/Core/` stays identical on every line.

## Build and test


```bash
dotnet restore src/Umbraco.Community.Automate.DeployHooks.slnx --locked-mode
dotnet build src/Umbraco.Community.Automate.DeployHooks.slnx -c Release --no-restore
dotnet test src/Umbraco.Community.Automate.DeployHooks.slnx -c Release --no-build
```

## Try it on the demo site

`demo/` is an Umbraco site for trying the package. It runs on HTTPS only:

```bash
dotnet user-secrets set "Umbraco:CMS:Unattended:UnattendedUserPassword" "<pw>" --project demo/Umbraco.Community.Automate.DeployHooks.DemoSite
dotnet dev-certs https
dotnet run --project demo/Umbraco.Community.Automate.DeployHooks.DemoSite   # https://localhost:44331/umbraco
```

## Try it in your own site

Pack with a unique version each time, because NuGet's global cache keeps serving the first build of any version number:

```bash
dotnet pack src/Umbraco.Community.Automate.DeployHooks -c Release -o ~/local-nuget -p:Version=18.0.0-local.$(date +%s)
dotnet nuget add source ~/local-nuget --name local   # once
dotnet add package Umbraco.Community.Automate.DeployHooks --version 18.0.0-local.<timestamp>
```

## Before you open a pull request

- Add or update tests for what you changed, and run them.
- Keep personal data out of logs.
- Security issues go through private reporting (the Security tab), never a public issue or PR.
