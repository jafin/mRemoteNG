# Tasks

Found while running `replace-default-connection-file-key` §8 by hand: a migrated store kept coming
back with no machine protector, and the cause was that `build.ps1` produces a portable build.

## 1. Runtime detection

- [x] 1.1 Add `PortableEdition` resolving the edition from a `portable.flag` beside the executable, cached for the run. — `mRemoteNG/App/Info/PortableEdition.cs`. Beside the executable rather than in the settings folder, because the settings folder's own location is one of the things this decides.
- [x] 1.2 Point `Runtime.IsPortableEdition` at it. — One expression, so there is one answer rather than two.
- [x] 1.3 ~~Keep the compiled constant honoured, as a transition.~~ **Overtaken by 4.1 and 4.3 in the same session and recorded rather than deleted, because the reasoning is what made the ordering safe.** The transitional `#if PORTABLE` existed so that removing the constant could not turn `build.ps1 -Portable` into an installed build before packaging wrote the marker — this defect in reverse, and worse, since it moves an existing user's settings. Permission to edit `build.ps1` arrived, the marker is written, and the branch is gone. The ordering mattered even though the window turned out to be minutes.
- [x] 1.4 Expose a test seam. — `HasMarker(directory)` for the rule and `OverrideForTests` for the answer. The real directory is the test runner's, which no test should be dropping marker files into.

## 2. Convert the compile-time sites

- [x] 2.1 `ChooseProvider` derives unconditionally from `LocalFileSettingsProvider`; `PortableSettingsInitializer` constructs `PortableSettingsProvider` or `ChooseProvider` from the edition. — **This was the one site that genuinely needed a compiler**, because it selected a base class. The initializer already ran before any setting was read and already wired one instance to every settings class, so the fix is two lines in the one place that could always have made the choice.
- [x] 2.2 `DockPanelLayoutLoader` and `ExternalAppsLoader` — the pre-`Settings`-folder path becomes null for portable rather than absent from the compilation.
- [x] 2.3 `IeBrowserEmulation` — both cleanup methods compile always; `Unregister` returns early when not portable.
- [x] 2.4 `FrmAbout` — `[Conditional("PORTABLE")]` becomes a runtime check.
- [x] 2.5 Remove `PORTABLE` from `Debug|*` and `Release|*` in the csproj, and then — under 4.3 — from every remaining configuration. Verified per configuration with `-getProperty:DefineConstants`: `Release` is `TRACE;RELEASE`, `Release Portable` is `TRACE;RELEASE_PORTABLE`, `Release Self-Contained` is `SELF_CONTAINED;RELEASE_SELF_CONTAINED`. The constant is now defined by nothing and referenced by no code.

## 3. Tests

- [x] 3.1 `PortableEditionTests` — marker present, absent, empty, case-insensitive, a similarly-named file that is not the marker, an unresolvable directory, the marker's location, and `Runtime` reading through the same seam.
- [x] 3.2 Full suite green with the edition flipped. **This is the assertion that mattered most and it is not a new test:** the whole suite previously ran with `PORTABLE` defined, because it built as `Release`. It now runs as the installed edition, exercising every settings, layout and external-tools path that the constant had been hiding. 4132 passed, 0 failed.

## 4. Packaging

- [x] 4.1 `build.ps1 -Portable` writes `portable.flag` into its output. — Done, with permission granted for that file. It also **stops passing `-p:DefineConstants=PORTABLE`**, which was doing quiet harm beyond the edition switch: that property *replaces* the constant list rather than adding to it, so every portable build was compiled without `TRACE` and `RELEASE`. The marker's text says what it is and that deleting it makes the same binaries the installed edition — contents are never read, so it costs nothing to be useful to whoever opens it. Verified: `Wrote portable.flag marker`, and the file sits beside `mRemoteNG.exe` in `bin\x64\Portable\`.
- [ ] 4.2 **The self-contained release asset is advertised as portable and no longer is.** `Build_mR-NB.yml` builds both matrix legs with plain `Configuration=Release` and never calls `build.ps1`, so the self-contained leg publishes to `bin\x64\Release\publish\` with no marker — while the release notes call it "**Self-Contained** - Portable, no installation required". It was portable by accident of the constant; now it is the installed edition, writing settings to `%APPDATA%` and giving connection files a machine-bound protector. Either the workflow writes `portable.flag` into that publish folder, or the notes stop calling it portable. **This is the only item here that changes a shipped artefact's behaviour, and it needs a workflow edit, which is out of scope without an explicit request.**
- [x] 4.2a The MSI must not carry the marker. — Correct by construction: step 09a harvests `mRemoteNG\bin\x64\Release`, which has no marker in it.
- [x] 4.3 Remove `PORTABLE` from the remaining configurations and delete the `#if PORTABLE` branch in `PortableEdition.Detect`. — Done, and it did not need 4.2 after all: **CI never uses `build.ps1` and never builds those configurations.** Both matrix legs of `Build_mR-NB.yml` run plain `Configuration=Release`, so `Release Portable`, `Debug Portable` and `Release Self-Contained` were dead as far as any artefact is concerned. The constant is now defined by nothing and the marker is the only answer. Verified per configuration with `-getProperty:DefineConstants`.
- [ ] 4.4 Release note for existing users: a portable installation that upgrades by copying only the executable, rather than extracting the zip, will come up as the installed edition and appear to have lost its settings. The zip is the supported upgrade path and the marker is why.

## 5. Verification

- [x] 5.1 Full build; zero new analyzer warnings.
- [x] 5.2 Full suite; 4132 passed, 0 failed.
- [x] 5.3 `openspec validate detect-portable-edition-at-runtime --strict`.
- [x] 5.4 Manual: a `Release` build no longer reports "Portable Edition" at startup, and a connection file it migrates inside the user profile is given a machine-bound protector. — Confirmed by the reporter during `replace-default-connection-file-key` §8.4: `machine=yes, recovery=yes`, where every previous attempt returned `machine=no`.
- [ ] 5.5 Manual: dropping `portable.flag` beside that same executable brings the portable edition back — settings beside the exe, "Portable Edition" in the title and startup log, and no machine protector on a newly migrated store. One build, both editions, nothing rebuilt.
- [ ] 5.6 Manual: an existing portable installation with settings beside the executable keeps reading them once the marker is added, and does not silently start a fresh set.
