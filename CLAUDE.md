# CLAUDE.md

> Instructions for Claude Code agents working in this repository.

## 1. Repo at a glance

| Field        | Value |
| ------------ | ----- |
| Repository   | `poli-page/sdk-csharp` |
| Type         | Core SDK |
| Language     | C# |
| Ecosystem    | .NET 8 LTS + .NET 10 LTS |
| Registry     | NuGet — `PoliPage` |
| Depends on   | `Microsoft.Extensions.Logging.Abstractions`, `System.Diagnostics.DiagnosticSource` (BCL adjacent) |
| Roadmap slot | P5.0 |

The full roadmap, the public API contract, and the reasoning behind the
multi-repo split live in the platform repo (`poli-page/poli-page`) under
`docs/onboarding/micka/`. Xavier will share the briefings with you. Read
them before starting on a new repo:

- `agent-guide.md` — the master version of this file. If you want to
  update conventions, change it there first; this file is its inlined
  derivative.
- `project-briefing.md` — what Poli Page is, develop credentials,
  expected repo layout.
- `sdk-specification.md` — the API contract every SDK must implement.
- `sdk-roadmap.md` — what to build, in which order, why.

---

## 2. Working language

- **Code, comments, file names, commit messages, PR descriptions,
  repository documentation**: English.
- **Day-to-day conversation with Xavier**: French, tutoiement.
- **Conversation in this Claude Code session**: French is fine for the
  chat; the artifacts you produce (code, commits, READMEs) stay
  English.

---

## 3. Test-Driven Development is mandatory

TDD is the working method, not a "nice to have". The cycle is
**RED → GREEN → refactor**:

1. **RED** — write the smallest possible failing test that captures the
   next bit of behavior.
2. **GREEN** — write the minimum code to make that test pass. No
   speculative generality, no extra branches.
3. **Refactor** — clean up the just-written code (or the call site)
   while the test stays green.

Every pull request lands as a sequence of these cycles, never as a
"I wrote it all then added tests".

### What to test

- **Every public method** of `PoliPageClient`, `Render`, `Documents`.
- **Every error path** — 4xx mapping to `PoliPage*Exception`, 5xx
  retry behaviour, `HttpRequestException`, timeout, malformed JSON,
  `text/html` vs `application/json` Content-Type handling.
- **Every retry edge case** — exponential backoff with jitter, max
  attempts, never retrying 4xx (except 429), `Retry-After` honoured,
  `CancellationToken` cancels the sleep mid-flight.
- **Every input variant** — `ProjectModeInput` (`project + template + version`)
  vs `InlineModeInput` (`template`), each rendering endpoint
  (PDF, stream, preview, document, thumbnails).
- **DI registration** — `services.AddPoliPage(...)` produces a usable
  singleton client.

### What NOT to over-test

- Don't test `HttpClient`, `System.Text.Json`, or
  `Microsoft.Extensions.Logging` — assume the BCL works.
- Don't test internal helpers in isolation if a public-method test
  already exercises them.
- Don't snapshot massive objects when an assertion on the field that
  matters would be clearer.

### Test layout

- Tests live in `tests/PoliPage.Tests/`.
- One test class per source class, mirroring the structure
  (`src/PoliPage/RenderNamespace.cs` →
  `tests/PoliPage.Tests/RenderNamespaceTests.cs`).
- Group integration tests in `tests/PoliPage.IntegrationTests/` (own
  project) so they're runnable separately from the unit suite.
- **Unit tests** mock the HTTP transport with `WireMock.Net` or a
  custom `DelegatingHandler`; assert request shape and response
  handling. These are 90 %+ of the suite.
- **Integration tests** hit the real develop API with a `pp_test_*` key
  from `POLI_PAGE_API_KEY`. Render a known template, verify the PDF is
  non-empty and `Content-Type: application/pdf`. Keep them few and
  idempotent. Tag them `[Trait("Category", "Integration")]`.

---

## 4. Robustness over shortcuts

Xavier's hard rule: **no hacks to make a test pass or a corner case go
away.** If something is broken, fix the underlying cause. If a
workaround is genuinely required (a BCL bug, an API quirk), document it
inline with a one-line comment starting with `Why:` that explains the
constraint — not the symptom.

Concrete corollaries:
- Don't catch `Exception` to silence a test.
- Don't add test-environment branches in production code.
- Don't add fallbacks for cases that can't happen — trust the BCL.
- Validate at boundaries (`PoliPageClientOptions` constructor, user
  input), not at every internal layer.

---

## 5. Code conventions

- **Style**: standard .NET conventions enforced by `.editorconfig`,
  `dotnet format`, and Roslyn analyzers
  (`Microsoft.CodeAnalysis.NetAnalyzers`, `Meziantou.Analyzer`,
  `Roslynator.Analyzers`). Pin analyzer versions in
  `Directory.Packages.props`.
- **No commented-out code.** Delete it; git remembers.
- **No `TODO` without a linked GitHub issue** — `// TODO(#42): refactor`
  is fine, `// TODO: refactor` is not.
- **No `Console.WriteLine` / `Debug.WriteLine` debug prints** in
  committed code.
- **Default to no comments.** Identifiers and short methods should
  explain themselves. XML doc comments (`///`) on every **public**
  symbol — they feed IntelliSense and the auto-generated API
  reference. Add inline comments only when the *why* is non-obvious.
- **Nullable reference types are mandatory** (`<Nullable>enable</Nullable>`).
  Use the BCL `?` annotation, never `[CanBeNull]`-style attributes.
- **`Async` suffix on every async method** — even when there is no sync
  counterpart. Standard BCL convention.

---

## 6. Commits and Pull Requests

- **Conventional Commits** for every commit:
  - `feat:` new behaviour visible to users.
  - `fix:` bug fix (link an issue when it exists).
  - `docs:` documentation only.
  - `refactor:` no behaviour change, no test change.
  - `test:` only adds/changes tests.
  - `chore:` build, deps, tooling.
- **One concern per PR.** A reviewer should be able to land it in
  under 30 minutes.
- **PR description** includes: what changed, why, how it was tested.
  Link issues; mention any follow-ups deliberately deferred.
- **CI must be green** before merge.

---

## 7. Continuous Integration

The workflow lives at `.github/workflows/ci.yml`. The contract is
identical across all 12 SDK repos:

- **Triggers**: every `push` (any branch) and every `pull_request`
  targeting `main`.
- **Matrix**: `net8.0` and `net10.0` on Ubuntu; `net10.0` also on
  Windows and macOS.
- **Jobs**: a single `test` job doing
  *Restore → Format → Build → Test → Pack → Install smoke* in order.
- **Auto-skip is built in**: each step short-circuits with a friendly
  message when the relevant project or test directory does not yet
  exist. This means a freshly scaffolded repo has a green pipeline
  from day one, and the pipeline starts running real work as soon as
  you add the manifest, lint config, and tests.

When working in this repo with Claude Code:
- After adding the manifest (`PoliPage.csproj`), the restore and build
  steps light up.
- After adding `.editorconfig`, the format step lights up.
- After adding the first test in `tests/`, the test step lights up.

If you change the workflow, the change MUST stay compatible with this
auto-skip behaviour — never make CI fail because of "missing setup".

---

## 8. Per-language specifics for this repo

- **Test framework**: xUnit + FluentAssertions
- **Mock HTTP**: `WireMock.Net` or a custom `DelegatingHandler`
- **Lint / format**: `dotnet format` + `.editorconfig` + analyzers
- **Manifest file**: `src/PoliPage/PoliPage.csproj`
- **Common targets**: `<Directory.Build.props>` at repo root
- **Target frameworks**: `net8.0` (LTS) and `net10.0` (LTS). CI matrix:
  `net8.0` and `net10.0` on Ubuntu; `net10.0` also on Windows and
  macOS. Tracks .NET's own LTS support window.
- **Run tests locally**: `dotnet test`
- **Run lint locally**: `dotnet format --verify-no-changes`

---

## 9. End-to-end "ship a feature" walk-through

This is what a single working day looks like:

1. **Pick** the next sliver from `sdk-specification.md` (or the open
   issue you're assigned to).
2. **Branch** from `main`: `git switch -c feat/<short-description>`.
3. **RED**: write one failing test that captures the slice. Run the
   suite — it fails on this test only.
4. **GREEN**: write the minimum code to pass. Run the suite — green.
5. **Refactor**: clean up. Suite stays green.
6. Repeat 3–5 for the next sliver of behaviour.
7. **Commit** with a Conventional Commits message.
8. **Push**. CI runs.
9. **Open a PR** with a clear description.
10. **Merge** when green and approved.

If a step takes more than half a day without a test going green, stop
and talk to Xavier — the slice is probably too big.

---

## 10. Adding a new dependency

- Justify it. "We could write this in 20 lines" usually means we
  should write it in 20 lines.
- Pin the version in `Directory.Packages.props` (Central Package
  Management).
- Prefer first-party Microsoft packages (`Microsoft.Extensions.*`)
  over third-party where the surface is comparable.
- Run the test suite before committing the manifest change.
- Mention the new dependency and its purpose in the commit message.

---

## 11. When stuck

- Re-read `sdk-specification.md` — many "open questions" are already
  answered there.
- Compare with the Node SDK reference implementation:
  `github.com/poli-page/sdk-node` (npm: `@poli-page/sdk`).
- Ask Xavier early. A two-line message is faster than half a day
  rebuilding the wrong thing.
- If a CI failure looks unrelated to your change, look for the same
  failure on `main` first before assuming you caused it.
