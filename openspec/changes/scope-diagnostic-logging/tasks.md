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
- [x] 4.2 Full test suite; zero failures, no `[Ignore]`. — Done 2026-08-12. 7,800 passed, 0 failed, 0 crashes across all nine groups plus the isolated `FrmOptions` phase.
- [x] 4.3 `openspec validate scope-diagnostic-logging --strict`. — Done 2026-08-12 — valid.
- [ ] 4.4 Manual: fresh profile, run, confirm the log holds startup information and no debug noise.
- [ ] 4.5 Manual: switch to verbose, confirm debug messages appear without restarting, switch back, confirm they stop.
- [ ] 4.6 Manual: launch with `-qc:user@host`, confirm the logged command line is redacted the same way the debug report renders it.

4.4–4.6 are the only work left in this change. They need the application on a desktop and 4.6 opens a
connection, so they were not run from the automated session that closed 3.1 and 4.1–4.3.
