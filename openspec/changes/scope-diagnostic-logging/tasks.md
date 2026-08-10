# Tasks

## 1. Configurable level

- [ ] 1.1 Add a log level setting to `OptionsNotificationsPage.settings`, defaulting to information.
- [ ] 1.2 Replace the hardcoded `.MinimumLevel.Verbose()` in `Logger.SetLogPath` (`Logger.cs:48`) with the setting. Serilog's `LoggingLevelSwitch` avoids rebuilding the logger on every change; `SetLogPath` already rebuilds under `_rebuildLock` for path changes, so the level must survive that rebuild either way.
- [ ] 1.3 Expose the level on the notifications options page, next to the log path that is already there.
- [ ] 1.4 Tests: the default suppresses debug and verbose; raising the level takes effect without a restart; a path change preserves the level.

## 2. Command line sanitising

- [ ] 2.1 Lift `SanitizeText` out of `DebugReportBuilder` (`DebugReportBuilder.cs:287`) into somewhere both callers can reach.
- [ ] 2.2 Route `StartupDataLogger.LogCmdLineArgs` (`StartupDataLogger.cs:132`) through it.
- [ ] 2.3 Check the rest of `StartupDataLogger` for the same pattern while it is open — it logs paths in several places.
- [ ] 2.4 Tests: a command line containing the profile path is redacted identically by both callers.

## 3. Confirm no secrets are logged

- [ ] 3.1 Add a test asserting no password-bearing property is written to the message collector or the log. This encodes the current state, which is already correct, so that enabling verbose does not quietly become an extraction path later.
- [ ] 3.2 Record in the proposal what was checked, so a future audit does not re-derive it: no mRemoteNG argument accepts a password, `CreateQuickConnect` parses `user@host:port` only, and no password value reaches a log call today.

## 4. Verification

- [ ] 4.1 Full build; zero new analyzer warnings.
- [ ] 4.2 Full test suite; zero failures, no `[Ignore]`.
- [ ] 4.3 `openspec validate scope-diagnostic-logging --strict`.
- [ ] 4.4 Manual: fresh profile, run, confirm the log holds startup information and no debug noise.
- [ ] 4.5 Manual: switch to verbose, confirm debug messages appear without restarting, switch back, confirm they stop.
- [ ] 4.6 Manual: launch with `-qc:user@host`, confirm the logged command line is redacted the same way the debug report renders it.
