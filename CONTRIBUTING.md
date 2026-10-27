# Contributing to `PoliPage` (sdk-csharp)

Thanks for your interest. A few short rules:

## Working method

We use **TDD**: write a failing test first, then the minimum code to
pass. Each public method has a corresponding test in
`tests/PoliPage.Tests/`. See `CLAUDE.md` for the full methodology.

## Commit messages

[Conventional Commits](https://www.conventionalcommits.org/):
`feat:`, `fix:`, `docs:`, `refactor:`, `test:`, `chore:`.

## Local development

```bash
dotnet restore
dotnet format --verify-no-changes
dotnet build --configuration Release --no-restore
dotnet test --configuration Release --no-build --logger "trx;LogFileName=test-results.trx"
```

`dotnet format` runs the .NET formatter and the analyzers configured in
`.editorconfig` and `Directory.Build.props`. CI fails on any formatting
or analyzer violation.

## Integration tests

Integration tests live in `tests/PoliPage.IntegrationTests` and hit the
real API. Every test carries the `Category=Integration` trait and is
skipped when `POLI_PAGE_API_KEY` is unset or empty, or is not a `sandbox`
key: they never render into a counted environment. Use a `sandbox` key
(`pp_sandbox_…`) of an organization whose `getting-started` project is
untouched; `POLI_PAGE_BASE_URL` (optional) overrides the API host.

```bash
export POLI_PAGE_API_KEY=pp_sandbox_x   # replace with your full key
dotnet test tests/PoliPage.IntegrationTests --filter "Category=Integration"
```

CI (`ci.yml`, `release.yml`) excludes them with
`--filter "Category!=Integration"`. The `Integration` workflow runs them
nightly and on `workflow_dispatch` with the `POLI_PAGE_API_KEY` (and
optional `POLI_PAGE_BASE_URL`) repo secrets, logs a skip when the key
secret is not set, and fails when it is set but is not a full `pp_sandbox_`
key.

## Releasing

Nothing publishes without a maintainer's approval. Pushing a `vX.Y.Z`
tag runs `.github/workflows/release.yml`: its `build` job checks that the
tag matches `<Version>`, runs format, build, unit tests and pack; its
`publish` job then waits for approval on the `nuget-release`
environment, and only on approval pushes to nuget.org (Trusted
Publishing / OIDC) and creates the GitHub Release from the version's
`CHANGELOG.md` section.

1. Bump `<Version>` in `Directory.Build.props`.
2. Move `[Unreleased]` to `[X.Y.Z] - YYYY-MM-DD` in `CHANGELOG.md`.
3. If the release breaks the public surface (a minor bump while on 0.x,
   a major bump from 1.0 on), add a section to `MIGRATION.md`.
4. Merge the release commit to `main`.
5. Run the pre-flight verification locally:
   ```bash
   dotnet format --verify-no-changes
   dotnet build --configuration Release --no-restore -warnaserror
   dotnet test --configuration Release --no-build --filter "Category!=Integration"
   dotnet pack  --configuration Release --no-build --output ./nupkg
   ```
6. Inspect the `.nupkg` contents (`unzip -l ./nupkg/PoliPage.X.Y.Z.nupkg`).
7. Tag the release commit on `main` and push the tag:
   `git tag vX.Y.Z && git push origin vX.Y.Z`.
8. Approve the `publish` job in the Actions run (environment
   `nuget-release`). It pushes to nuget.org and creates the GitHub Release.

### Stable vs. prerelease channels

NuGet treats any semver with a prerelease suffix
(`-rc.1`, `-beta.2`, `-alpha.0`) as a prerelease. `dotnet add package`
without `--prerelease` ignores them; users opt in explicitly.

#### Cutting a prerelease

1. Bump `<Version>` to e.g. `2.0.0-rc.1`.
2. Move `[Unreleased]` → `[2.0.0-rc.1] - YYYY-MM-DD` in `CHANGELOG.md`.
3. Commit `chore(release): v2.0.0-rc.1`.
4. Tag and approve as above. NuGet routes the prerelease
   automatically, and the GitHub Release is marked as a prerelease.

Users opt in:

```bash
dotnet add package PoliPage --prerelease
dotnet add package PoliPage --version 2.0.0-rc.1
```

#### Promoting a prerelease to stable

When the prerelease is ready, cut a stable release at the same semver
minus the suffix:

1. Bump `<Version>` to `2.0.0` (drop the suffix).
2. Move the prerelease entries in `CHANGELOG.md` under
   `[2.0.0] - YYYY-MM-DD`.
3. Commit, tag, and approve the publish as above.

Stable and prerelease versions must never share a build identifier —
once a prerelease is promoted, the next prerelease starts a new
pre-suffix sequence (e.g. `2.1.0-beta.0`).
