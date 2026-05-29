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

Integration tests hit the API. They are gated behind a category trait
and the `POLI_PAGE_API_KEY` env var:

```bash
export POLI_PAGE_API_KEY=pp_test_...
export POLI_PAGE_BASE_URL=https://api-develop.poli.page   # optional
dotnet test --filter "Category=Integration"
```

To skip integration tests on push (the default), simply run without the
filter — the test runner excludes the `Integration` category unless
selected.

## Releasing

Releases are **manual**. There is no CI workflow that auto-publishes —
by design. Pushing a tag does not publish; pushing a build artifact
does.

1. Bump `<Version>` in `Directory.Build.props`.
2. Move `[Unreleased]` to `[X.Y.Z] - YYYY-MM-DD` in `CHANGELOG.md`.
3. If a MAJOR bump, add a section to `MIGRATION.md`.
4. Commit `chore(release): vX.Y.Z` on `main`.
5. Run the pre-flight verification locally:
   ```bash
   dotnet format --verify-no-changes
   dotnet build --configuration Release --no-restore -warnaserror
   dotnet test --configuration Release --no-build
   dotnet pack  --configuration Release --no-build --output ./nupkg
   ```
6. Inspect the `.nupkg` contents (`unzip -l ./nupkg/PoliPage.X.Y.Z.nupkg`).
7. Tag locally: `git tag vX.Y.Z`.
8. Push the tag when ready: `git push origin vX.Y.Z`.
9. Push the package: `dotnet nuget push ./nupkg/PoliPage.X.Y.Z.nupkg --source https://api.nuget.org/v3/index.json --api-key $NUGET_API_KEY`.
10. Optionally create a GitHub Release from the tag for the changelog
    excerpt — `gh release create vX.Y.Z --notes-from-tag`.

### Stable vs. prerelease channels

NuGet treats any semver with a prerelease suffix
(`-rc.1`, `-beta.2`, `-alpha.0`) as a prerelease. `dotnet add package`
without `--prerelease` ignores them; users opt in explicitly.

#### Cutting a prerelease

1. Bump `<Version>` to e.g. `2.0.0-rc.1`.
2. Move `[Unreleased]` → `[2.0.0-rc.1] - YYYY-MM-DD` in `CHANGELOG.md`.
3. Commit `chore(release): v2.0.0-rc.1`.
4. Run the pre-flight verification, pack, and push the `.nupkg` as
   above. NuGet routes the prerelease automatically.

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
3. Commit, tag, pack, and push.

Stable and prerelease versions must never share a build identifier —
once a prerelease is promoted, the next prerelease starts a new
pre-suffix sequence (e.g. `2.1.0-beta.0`).
