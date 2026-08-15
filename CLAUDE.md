# mRemoteNG - Build & Development Notes

> **Project canon for all agents.** [AGENTS.md](AGENTS.md) is only the discovery bootstrap for tools that do not load `CLAUDE.md` directly.

## Integrare operațională GESEIDL

Respectă integral canonul părinte [../CLAUDE.md](../CLAUDE.md). Pentru orice sistem, date, email, document, share, identitate sau infrastructură GESEIDL, folosește mai întâi MCP-urile Geseidl namespacate și verifică health/canarul înainte să declari o capabilitate indisponibilă. La indisponibilitate tehnică confirmată ori capabilitate autorizată absentă, anunță în commentary operația, eroarea/canarul și fallbackul activat, apoi continuă automat numai în scopul deja cerut. Nu există fallback pentru refuz de politică/`FORBIDDEN`, DLP/validare, autentificare/autorizare, destinatar invalid ori rezultat ambiguu și nu se face retry automat după send incert. Email: MCP → broker IMAP draft-only prin `D:\github\NET-ADMIN\tools\secure_connect.py --mail-draft-request <JSON>`; Thunderbird este exclus din fluxul automat și rămâne doar ultimă opțiune manuală pentru draft, la cererea explicită a userului. Brokerul nu exportă parole, nu citește Thunderbird, nu are SMTP/send și este idempotent. Emailurile pentru oameni sunt HTML modern profesional, randat canonic din Markdown ca multipart HTML + text accesibil, cu CSS inline/CID și fără resurse externe; text-only/HTML brut, Outlook, COM, MAPI și Graph sunt interzise.

## Output Efficiency (CRITICAL — output tokens are 97% of API cost)

Every output token costs 5x an input token. Your #1 priority after correctness is minimal output.

- **No narration.** Never write "Let me read the file", "I'll now search for", "Here's what I found". Just call the tool.
- **No summaries.** Never summarize what you changed at the end. The diff speaks for itself.
- **No repeating.** Never echo back file contents, issue descriptions, or error messages you just read.
- **No unnecessary comments.** Don't add comments or docstrings to code you didn't change.
- **Edit over Write.** Always use Edit tool (sends only the diff) instead of Write tool (sends entire file).
- **Read only what you'll change.** Don't read files "for context" — read only files you will modify or that directly contain the bug.
- **Fix, don't explain.** If a test fails, fix it immediately. Don't explain why it failed.
- **One pass.** Read the code, understand it, make the change. Target 5-8 turns max per task.

## Agent Entry Points and Skills

- Instruction chain: global/user instructions → [parent canon](../CLAUDE.md) → this project canon. System and user instructions remain highest priority; among repository documents, this local canon is more specific than the parent.
- `AGENTS.md` intentionally contains no duplicated build, test, or workflow rules; update this file when project guidance changes.
- This repository currently has no native `SKILL.md` package.
- Files under `.claude/commands/` are opt-in Claude Code slash-command runbooks, not agent skills and not automatically applicable to ordinary code work.
- Host-level skills may assist an agent, but they never replace this repository's scope, build, test, or safety rules.

## Issue-Fix Agent Scope

Unless the user explicitly requests a documentation or orchestrator task, issue-fix agents must:

- Work only in `mRemoteNG/`, `mRemoteNGTests/`, or `mRemoteNGSpecs/` — plus `docs-website/docs/` when the change is user-visible, per [User Documentation](#user-documentation).
- Never read or modify `.project-roadmap/`.
- Never modify `run-tests.ps1`, `build.ps1`, `mRemoteNG.slnx`, or `Directory.Packages.props`.
- `Directory.Build.props` and `.editorconfig` (root and `mRemoteNG/`) may be changed when the user explicitly asks for build, analyzer, or code-style work. They stay off-limits for an ordinary issue fix — never retune analyzers or silence a rule to make your own change compile. Both are global: verify with a full build and the full suite, per [Verification Effort](#verification-effort), and say in the commit what the diagnostic output was before and after.
- `.github/workflows/*` may be changed when the user explicitly asks for CI work. It stays off-limits for an ordinary issue fix — never edit a workflow as a side effect of another task.
- Commit when the work is done and verified — see [Committing](#committing). Never `git push`, force-push, rewrite published history, or open a PR unless the user asks.
- Preserve existing behavior outside the reported issue and never add interactive tests.

### Additional notice for automated `claude -p` agents

- Your only job is the specific issue in the prompt.
- Do not run `iis_orchestrator.py`, `sync`, `analyze`, `update`, or any orchestrator command.
- Output only code changes: no explanations, summaries, or commentary.

## Mandatory Workflow for Issue Fixes

1. **Verify and plan before editing:** inspect every suggested file that exists, search by symptom/error/class, trace the actual call path, and analyze why previous attempts failed. Write a plan of at most five lines naming the root cause and exact files.
2. **Implement only the fix:** make the smallest change that resolves the reported issue without unrelated behavior changes.
3. **Verify proportionately:** see [Verification Effort](#verification-effort). Do not reflexively run a full build + full test suite after every edit.
4. **Repair regressions:** fix any build or test failure caused by the change before finishing.
5. **Document what the user can see:** if the change alters what a user does, sees, or configures, update `docs-website/docs/` in the same commit — see [User Documentation](#user-documentation).

## Verification Effort

Build and test runs are expensive (~70–120s build, ~140s full suite). Match the verification to the change instead of running everything every time.

| Change | Verify with |
|--------|-------------|
| Code comments, or markdown outside `docs-website/` | Nothing |
| Anything under `docs-website/` | `pnpm run typecheck && pnpm run build` in `docs-website/` |
| Single project, small edit | Compile that project only (`msbuild mRemoteNG/mRemoteNG.csproj`) |
| Logic change with existing tests | Compile + the **targeted** test filter (`--filter "FullyQualifiedName~<Fixture>"`) |
| Multi-file/cross-project, or public API change | Full build + affected test group(s) |
| Dependency/package bumps, analyzer config, csproj/props | Full build (restore) + full suite |
| Before finishing a multi-file session, or before commit/PR | Full build; full suite only if logic in tested areas changed |

Rules:
- **Don't re-run what you just ran** when the follow-up edit can't affect the result (e.g. a comment reword after a green build).
- Prefer a targeted `--filter` over the whole suite; run the full suite when the blast radius is unclear.
- A build failing only on **file-copy locks** (running mRemoteNG.exe holds `bin\`) is not a code failure — compile succeeded. Ask the user to close the app, or build to a temp `OutputPath` to verify.
- Never skip verification for the categories that need it, and never report success for a build or test run that was not actually performed.

## User Documentation

The end-user documentation lives in `docs-website/docs/` (Docusaurus, published to GitHub Pages by `.github/workflows/docs.yml`). It is the manual our users actually read — treat it as part of the feature, not as follow-up work.

### When to update it

Update the docs **in the same commit as the code** whenever a change is user-visible:

| Change | Document it |
|--------|-------------|
| New feature, protocol, or connection property | Yes — new page or new section |
| Changed UI: menus, dialogs, panels, defaults | Yes — including any screenshot that is now wrong |
| New or changed option, registry setting, CLI switch, external-tool variable | Yes — in the matching reference page |
| Behavior a user could notice (a default flips, a shortcut moves, a workflow gains a step) | Yes |
| Security change a user must act on (new prompt, re-auth, migration step) | Yes |
| Internal refactor, perf work, test changes, analyzer fixes | No |
| Bug fix restoring already-documented behavior | No — unless the docs described the bug |

If a change makes an existing page wrong, fixing that page is part of the fix. Leaving stale documentation behind is an incomplete change, and outdated instructions cost users more than missing ones.

### How to write it

Write for someone using mRemoteNG, not someone building it. That means:

- **Task-first.** Lead with what the user wants to accomplish, then the steps. Not "the `X` field was added to `ConnectionInfo`" but "To reconnect automatically after a dropped session, set…".
- **Name what they see.** Use the exact on-screen labels and the real menu path (`**Tools → Options → Appearance**`), so the text can be followed without guessing.
- **Concrete over abstract.** Give a worked example with real values. The How-To pages are the model.
- **No internals.** No class names, method names, issue numbers, or implementation detail. Those belong in the code and the changelog.
- **Short sentences, plain words.** Assume a competent sysadmin who has never seen this feature.
- **Say the version.** New in a specific release? Open with `:::info Version` / `Added in vX.Y.Z`.
- **Warn where it matters.** Use `:::warning` for anything that can lose data, break connections, or weaken security; `:::tip` for shortcuts; `:::note` for asides.

### Mechanics

- Pages are **CommonMark `.md`**, not MDX — `markdown.format` is `detect`, so `<user@domain>` and `%Variable%` literals are safe. Use `.mdx` only if a page genuinely needs components.
- Front matter needs at least `title:`; add `sidebar_label:` when the title is long.
- **A new page is invisible until it is listed in `docs-website/sidebars.ts`.** Add it to the right category.
- Screenshots go in `docs-website/docs/images/` and are referenced by **relative** path (`./images/x.png`, `../images/x.png`) — a wrong path fails the build, which is the point. Always give real alt text.
- Cross-link with relative `.md` paths (`../variables-reference.md`); `onBrokenLinks` is `throw`, so a dead link fails CI.
- Verify with `pnpm run typecheck && pnpm run build` in `docs-website/`. Do not commit documentation you have not built.

The Sphinx sources in `mRemoteNGDocumentation/` are the pre-migration upstream copy and are **no longer maintained** — never edit them, and never port a fix there.

## Build Instructions

**Do NOT use `dotnet build`** — fails with `MSB4803` on COM references (`MSTSCLib` RDP ActiveX control). Must use full VS BuildTools MSBuild.

### Commands:
```powershell
# Full build (restore + compile):
pwsh -NoProfile -ExecutionPolicy Bypass -File "D:\github\mRemoteNG\build.ps1"

# Fast incremental (skip restore):
pwsh -NoProfile -ExecutionPolicy Bypass -File "D:\github\mRemoteNG\build.ps1" -NoRestore

# Self-contained (embeds .NET runtime, output: bin\x64\Release\publish\):
pwsh -NoProfile -ExecutionPolicy Bypass -File "D:\github\mRemoteNG\build.ps1" -SelfContained

# CI parity — run the code-style analyzers a local build skips (~3x slower):
pwsh -NoProfile -ExecutionPolicy Bypass -File "D:\github\mRemoteNG\build.ps1" -Analyzers
```

`build.ps1` auto-detects VS installation (VS2026 > VS2022). Self-contained uses `-t:Publish` and restore MUST include `/p:PublishReadyToRun=true` (NETSDK1094).

### Analyzers and build speed

Analyzers are the overwhelming majority of this solution's build time. `Directory.Build.props` splits them in two:

| | Local build | `-Analyzers` / CI |
|---|---|---|
| Correctness and quality (Meziantou, Roslynator, NetAnalyzers) | on | on |
| Code style (`IDE*`, `EnforceCodeStyleInBuild`) | off | on |
| Solution build after a one-file edit | ~18s | ~50s |

The code-style rules report nothing on this tree — every `IDE*` rule is either set to `none` or already clean — so a local build and a CI build produce the **same** diagnostics. Turning them off does not weaken the IDE either: Visual Studio and Rider apply `.editorconfig` style live, independently of this property. Use `-Analyzers` before pushing if you want to confirm CI parity, or `-p:RunAnalyzers=false` for a bare-minimum inner loop.

CI is detected from `$(CI)` / `$(GITHUB_ACTIONS)`. Do **not** switch that gate to `$(ContinuousIntegrationBuild)` — nothing sets it, so the checks would be off everywhere.

:warning: `mRemoteNG/.editorconfig` declares `root = true`, so it does **not** inherit the repository `.editorconfig`. A rule suppressed only at the repository root has no effect on the main project; it must be repeated in `mRemoteNG/.editorconfig`.

## Testing

### Run tests (preferred):
```powershell
# Headless (CI/orchestrator):
pwsh -NoProfile -ExecutionPolicy Bypass -File "D:\github\mRemoteNG\run-tests.ps1" -Headless

# Skip build (fast iteration):
pwsh -NoProfile -ExecutionPolicy Bypass -File "D:\github\mRemoteNG\run-tests.ps1" -Headless -NoBuild

# Bash runner (fastest, no build):
bash run-tests-core.sh
```

### Single test group:
```bash
dotnet test "mRemoteNGTests/bin/x64/Release/mRemoteNGTests.dll" --results-directory /tmp/mrt --verbosity normal --filter "FullyQualifiedName~mRemoteNGTests.Tools"
```

### Critical Rules:
- **`--verbosity normal` ONLY** — minimal/quiet crashes testhost on .NET 10
- **`--results-directory` outside repo** — TestResults inside repo causes cascading crashes
- **DLL path, not .csproj** — `dotnet test --no-build` on .csproj looks in wrong `bin\Release\`
- **No interactive tests** — NEVER create tests with GUI dialogs, message boxes, or user input. Mock all UI dependencies.
- **No `[assembly: Parallelizable]`** — causes race conditions on shared mutable singletons
- **RunWithMessagePump pattern** — for ObjectListView/FrmOptions tests, use `Application.Run(form)` + `Application.ExitThread()` in finally

### The Golden Rule (test failures):
Every test failure MUST be resolved before finishing a task. NO EXCEPTIONS.
1. **Fix the code** if the test caught a real bug
2. **Fix the test** if the test logic is flawed
3. **Remove the test** ONLY if no longer valid
**NEVER use `[Ignore]`** for failing tests.

### 100% DLL Coverage:
`run-tests.ps1` runs parallel groups + sequential Remnants. If coverage gap detected, exit 96. New namespaces: update `$groups` in `run-tests.ps1` or let Remnants handle them.

### Current status: see `test-config.json` (single source of truth for test counts & groups)

## CI/CD
- Runners: `windows-2025-vs2026` with MSBuild 18.x (VS2026)
- Workflows: `pr_validation.yml` (build), `nightly.yml` (rolling `nightly` prerelease on push→main), `Build_mR-NB.yml` (stable release — cut by pushing a `vX.Y.Z` tag; `make_latest`), `sonarcloud.yml` (quality gate), `codeql.yml` (security), `docs.yml` (builds `docs-website/`; deploys to GitHub Pages on push→main, build-only on PRs)
- Platforms: x86, x64, ARM64
- Code signing: SignPath Foundation (mandatory — see `docs/CODE_SIGNING_POLICY.md`)
- Version: read from `mRemoteNG/mRemoteNG.csproj` `<Version>` element

## Code Quality — 5 Levels

| Level | Tool | Scope | Config |
|-------|------|-------|--------|
| 1 | .NET Analyzers + Roslynator + Meziantou | Local build (warnings) | `Directory.Build.props`, `.editorconfig` (root + mRemoteNG/) |
| 2 | SonarCloud | Push to `main` (CI) | `.github/workflows/sonarcloud.yml` |
| 3 | CodeQL | Push to `main` + weekly (CI) | `.github/workflows/codeql.yml` |
| 4 | Roslynator | Included in Level 1 (NuGet) | `Directory.Packages.props` |
| 5 | Qodo Code Review | On-demand (AI review) | GitHub App + `scripts/qodo-review.sh` |

## Branch Strategy

| Branch | Purpose |
|--------|---------|
| `main` | Active development — default branch |
| `release/X.Y` | Historical release branches (frozen) |

### Feature branch naming:
| Prefix | When | Example |
|--------|------|---------|
| `fix/<issue>-<desc>` | Bug fix | `fix/2735-rdp-smartsize-focus` |
| `feat/<issue>-<desc>` | New feature | `feat/1634-protocol-token` |
| `security/<desc>` | Security | `security/ldap-sanitizer` |
| `chore/<desc>` | Infra, deps, CI | `chore/sqlclient-sni-runtime` |

Lowercase, kebab-case, max 50 chars after prefix. No tool prefixes.

### Sync upstream:
```bash
git fetch upstream && git merge upstream/v1.78.2-dev
```

## Committing

Agents commit their own work. Finishing a task means committing it, not leaving a dirty tree for someone else to interpret.

**Commit when:** the change is complete and its [verification](#verification-effort) passed. A commit asserts the tree builds and its tests are green — never commit over a failing build or a failing test.

**Do not commit:** work you have not verified, unrelated files that happened to be dirty, or generated output (`bin/`, `obj/`, `TestResults/`). Stage explicitly by path — `git add -A` sweeps up whatever else the working tree was carrying. If a file you need to touch already had uncommitted changes when you started, they are not yours to commit: keep them out, and say so.

**Never without being asked:** `git push`, force-push, `git rebase`/`git reset --hard` over published history, tags, or PRs. Rewriting what others may have pulled is not reversible; a local commit is.

**Branch:** never commit straight to `main`. If HEAD is `main`, branch first using the [naming table](#feature-branch-naming).

**Message:** subject in the imperative under ~72 chars, prefixed as the branch would be (`fix:`, `feat:`, `security:`, `chore:`, with an optional scope). Then a blank line and a body explaining **why** — what was wrong, and why this is the fix. The diff already shows what changed. Note the verification you actually ran. Do not add AI attribution, `Co-Authored-By`, or tool advertising.

Scope a commit to one coherent change. Several unrelated fixes in one session are several commits.

## Session Discipline — Build Verification

1. **Run a build before ending a session that changed code** — especially for multi-file changes. Skip it only when nothing compilable changed (docs/comments), or when the build already ran green after the last code edit.
2. If build fails, fix BEFORE reporting progress
3. **Never leave uncompilable code** — worse than slower progress
4. Prefer small verified steps over massive unverified refactoring
5. Scale intermediate checks to the change — see [Verification Effort](#verification-effort)

## Developer Guide

For orchestrator operations, release checklists, IIS system, issue tracking,
PR history, and release status, see: **`.project-roadmap/DEVELOPER_GUIDE.md`**

## Evidence & Scientific Documentation

For the complete evidence trail of the AI-assisted modernization process
(metrics, agent performance, CI data, methodology notes), see: **`scientific-paper/EVIDENCE.md`**

## Current Release Status (2026-07-02)

| Metric | Value |
|--------|-------|
| Version | **1.82.0** (stable, released 2026-07-02) |
| Analyzer warnings | 0 (5,247 eliminated) |
| Tests | 6,329 passed, 0 failures |
| Startup time | ≤1s with 200 connections (optimized from ~10-30s) |
| CI status | All workflows GREEN |
| SonarCloud | Quality Gate PASSED (A/A/A) |
| Release model | **2 live releases**: rolling `nightly` (overwritten each push to `main`) + stable `vX.Y.Z` tags (`releases/latest`). Old dated `-NB-` prereleases removed. |
| Update check | In-app checks GitHub `releases/latest` (latest stable) and opens the release page — no Stable/Preview/Nightly channels, no `mremoteng.org` feeds |
| MSI installer | WiX 6 SDK — auto-generated in nightly + release CI ([#24](https://github.com/robertpopa22/mRemoteNG/issues/24)) |
