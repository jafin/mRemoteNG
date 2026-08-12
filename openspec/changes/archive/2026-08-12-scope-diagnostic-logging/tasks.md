# Tasks

## 1. Configurable level

- [x] 1.1 Add a log level setting to `OptionsNotificationsPage.settings`, defaulting to information. — **No new setting.** `TextLogMessageWriterWriteDebugMsgs` already exists, already says what this controls, and already defaults to false. Adding a second knob would have left two options disagreeing about the same thing.
- [x] 1.2 Replace the hardcoded `.MinimumLevel.Verbose()` in `Logger.SetLogPath` with the setting, via `LoggingLevelSwitch` so it survives the rebuild `SetLogPath` performs. — Done, and it fixed a second defect: the existing debug checkbox only gated whether the *message collector* forwarded debug messages, while ~38 direct `Log.Debug` calls wrote unconditionally because the minimum was hardcoded. The option now means the same thing everywhere.
- [x] 1.3 Expose the level on the notifications options page, next to the log path that is already there. — Already exposed: `chkLogDebugMsgs`. `SaveLoggingSettings` now calls `ApplyConfiguredLevel()` so it takes effect without a restart.
- [x] 1.4 Tests: the default suppresses debug; raising the level takes effect immediately; a path change preserves the level. — `LoggerLevelTests`. Nothing calls `Log.Verbose` anywhere in the codebase, so the real distinction is Debug versus Information and the level is set accordingly.

## 2. Command line sanitising

- [x] 2.1 Lift `SanitizeText` out of `DebugReportBuilder` into somewhere both callers can reach. — New `DiagnosticTextSanitizer` with two strengths: `RedactUserPaths` and full `Redact`. `DebugReportBuilder` delegates and its behaviour is unchanged.
- [x] 2.2 Route `StartupDataLogger.LogCmdLineArgs` through it. — **Deviation:** through `RedactUserPaths` only, not full redaction. Full redaction also strips hostnames, and removing them from one startup line while every connection message below still carries them hides nothing and costs the log its use. The profile path is the part that is genuinely additive: it carries the Windows account name.
- [x] 2.3 Check the rest of `StartupDataLogger` for the same pattern — the other entries log framework, culture and hardware facts, not user paths.
- [x] 2.4 Tests: `DiagnosticTextSanitizerTests` covers both strengths, including that path redaction leaves hostnames intact and full redaction does not.

## 3. Confirm no secrets are logged

- [x] 3.1 Add a test asserting no password-bearing property is written to the message collector or the log. This encodes the current state, which is already correct, so that enabling verbose does not quietly become an extraction path later. — `LogSecretExclusionTests`. Drives the one unit-testable path that holds a secret and then reports on it: resolve the credential, translate it for the OpenSSH backend (which cannot use a password and so always has something to say), replay the diagnostics as the protocols do, with every message filter on. The secret-bearing properties are filled by reflection rather than listed, so a password property added to `ConnectionInfo` later is covered without anyone returning here. Confirmed non-vacuous by mutation: appending `RevealSecret()` to the credential rendering fails all three tests, naming the property that leaked. **Boundary, recorded rather than glossed:** only `Password` travels this path. `RDGatewayPassword` and `VNCProxyPassword` reach the log through `RdpProtocol`, which needs the RDP ActiveX control and a message pump; their assertions are a tripwire for a future leak into this path, not coverage of their own.
- [x] 3.2 Record in the proposal what was checked, so a future audit does not re-derive it — done in proposal.md.

## 4. Verification

- [x] 4.1 Full build; zero new analyzer warnings. — Done 2026-08-12. Clean; the only warnings in the build are the pre-existing MA0002/MA0006 in the SFTP integration tests, untouched by this change.
- [x] 4.2 Full test suite; zero failures, no `[Ignore]`. — Done 2026-08-12. Zero failures, zero crashes across all nine groups plus the isolated `FrmOptions` phase. `run-tests.ps1` reported 7,800 for this run, but that figure is a **sum across overlapping group filters, not a count of distinct tests** — the assembly holds 4,025 discoverable tests and the `Remaining` group alone accounts for 4,004 of them, so the named groups largely re-run what `Remaining` already covered. Recorded here because the number reads like a test count and is not one.
- [x] 4.3 `openspec validate scope-diagnostic-logging --strict`. — Done 2026-08-12 — valid.
- [x] 4.4 Manual: fresh profile, run, confirm the log holds startup information and no debug noise. — Done 2026-08-12 against a renamed-aside profile. Zero DEBUG lines; the log holds the `[Startup]` timings and the startup facts.
- [x] 4.5 Manual: switch to verbose, confirm debug messages appear without restarting, switch back, confirm they stop. — Done 2026-08-12. Confirmed with the options dialog's own `[BtnOK_Click]`/`[SaveOptions]` traces, which go straight to `Logger.Instance.Log` and so depend on nothing but the level switch. **Worth knowing for the next person:** the Notifications page carries three checkboxes all labelled "Debug" — notification panel, logging, popup — and only the one in the Logging group drives the log level. The first attempt here ticked the wrong one and read as a broken switch.
- [x] 4.6 Manual: launch with `-qc:user@host`, confirm the logged command line is redacted the same way the debug report renders it. — Done 2026-08-12, confirmed by the maintainer. **The task as written was stale on two counts, and what was checked is this instead:** the `Command Line:` line shows `%USERPROFILE%` in place of the profile path, and leaves `user@host` intact. (1) The two redactions deliberately differ — task 2.2 routes the startup log through `RedactUserPaths` only, while `DebugReportBuilder` applies the full `Redact` that also strips hostnames; asking them to match would undo that decision. (2) There is no debug report to compare against from a running application: `DebugReportBuilder.BuildReport` has no caller anywhere in the repo. The equivalence of the two strengths is covered by `DiagnosticTextSanitizerTests`. Note that the exe must be launched with an argument under the profile — `-cons:"%APPDATA%\mRemoteNG\confCons.xml"` — or there is nothing on the command line to redact.

All tasks are complete. Two things this change's verification turned up, both handled:
`frmMain.Diag118` logged at information from `WM_LBUTTONDOWN` (fixed, then removed outright once
issue #118 was confirmed fixed), and `DebugReportBuilder.BuildReport` is unreachable from the running
application — noted here rather than fixed, since giving the debug report an entry point is a
feature, not part of scoping the log.

The 4.4 run turned up one defect, fixed separately: `frmMain.Diag118`, a temporary trace for
issue #118, ran from `WM_LBUTTONDOWN` at information severity — several lines per left-click,
permanently, in every user's log. It is what this change exists to stop, and it survived review
because the hardcoded `Verbose()` minimum made it indistinguishable from everything else being
written. First lowered to debug, then removed outright once #118 was confirmed fixed and the trace
had done its job.
