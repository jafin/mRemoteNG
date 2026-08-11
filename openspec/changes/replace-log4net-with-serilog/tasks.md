## 1. Dependencies

- [x] 1.1 Add `Serilog`, `Serilog.Sinks.File`, and `Serilog.Enrichers.Thread` PackageVersion entries to `Directory.Packages.props` (requires explicit approval — off-limits for automated issue-fix agents per repo policy)
- [x] 1.2 Add the corresponding `PackageReference`s to `mRemoteNG\mRemoteNG.csproj`
- [x] 1.3 Remove the `log4net` PackageVersion entry from `Directory.Packages.props`
- [x] 1.4 Remove the `log4net` `PackageReference` from `mRemoteNG\mRemoteNG.csproj`

## 2. Logger core rewrite

- [x] 2.1 Rewrite `mRemoteNG\App\Logger.cs` to build a Serilog `Logger` (rolling file sink: `fileSizeLimitBytes = 10*1024*1024`, `rollOnFileSizeLimit = true`, `retainedFileCountLimit = 6`, `rollingInterval = RollingInterval.Infinite`, `WithThreadId()` enrichment, output template matching `{Timestamp:yyyy-MM-dd HH:mm:ss,fff} [{ThreadId}] {Level,-6:u}- {Message:lj}{NewLine}{Exception}`) instead of `XmlConfigurator.Configure`. **Two values here were corrected in verification** (5.2, 5.5) and no longer read as first written: `retainedFileCountLimit` was `5`, but Serilog counts the file it is writing, so the "1 main log + 5 backups" policy this change set out to preserve needs `6`; and the level was `{Level:u6}`, where the width truncates instead of padding and put `INFORM` and `WARNIN` in the log — `{Level,-6:u}` pads as log4net's `%-6level` did.
- [x] 2.2 Retype `Logger.Log` from log4net's `ILog?` to Serilog's `Serilog.ILogger?`, keeping the `Instance` accessor shape
- [x] 2.3 Reimplement `SetLogPath(string)` to dispose and rebuild the Serilog logger against the new path, guarding against concurrent log writes during the rebuild
- [x] 2.4 Remove `mRemoteNG\log4net.config` and its `CopyToOutputDirectory` wiring in `mRemoteNG.csproj`

## 3. Call site updates

- [x] 3.1 Update `mRemoteNG\Messages\MessageWriters\TextLogMessageWriter.cs` to call Serilog's `Log.Information/Debug/Warning/Error` instead of log4net's `ILog` API (no exception-object overload needed — `MessageCollector` pre-formats exceptions into plain text before they reach this writer)
- [x] 3.2 Update the direct `Logger.Instance.Log?.*` calls in `mRemoteNG\UI\Forms\frmOptions.cs` and `mRemoteNG\UI\Forms\OptionsPages\StartupExitPage.cs` to the Serilog method names (`Warn`→`Warning`; `Debug`/`Error` unchanged)
- [x] 3.3 Confirmed `mRemoteNG\App\CommandLineParser.cs` only calls `Logger.Instance.SetLogPath(...)` (no `.Log?.*` calls) — signature unchanged, no edit needed
- [x] 3.4 Grep the codebase for any remaining `log4net` imports or `ILog` references to confirm none remain outside the files above

## 4. Documentation

- [x] 4.1 Update `docs\CREDITS.md` to credit Serilog instead of log4net
- [x] 4.2 Add a `CHANGELOG.md` entry describing the log4net → Serilog migration and the log line format change

## 5. Verification

- [x] 5.1 Compile `mRemoteNG\mRemoteNG.csproj` and resolve any remaining log4net-typed references
- [x] 5.2 Generate log entries at Debug/Info/Warning/Error and confirm they appear with timestamp, thread id, level, and message. — Passed, and it caught a defect. Covered by `LoggerFileOutputTests.EverySeverityIsWrittenWithTimestampThreadIdLevelAndMessage`, which drives the real `Logger` singleton and the writer stack the app builds in `MessageCollectorSetup` (`MessageTypeFilterDecorator` → `TextLogMessageWriter`) against a temp file, then parses every line back with the output template's own shape. The defect: `{Level:u6}` **truncates** — the log had been recording `INFORM` and `WARNIN`, visible in the local build's own log at `mRemoteNG\bin\x64\Release\mRemoteNG Connection Manager.log` (3,866 `INFORM` and 121 `WARNIN` lines beside the 10,748 `INFO` and 35 `WARN` lines log4net left before the migration). Fixed to `{Level,-6:u}`, which pads without truncating as `%-6level` did. Real-app evidence for the rest of the template is in 5.3.

  A note for whoever reads this next: the log file is **`mRemoteNG Connection Manager.log`**, not `mRemoteNG.log` — `BuildLogFilePath` names it from `Application.ProductName`, and the `mRemoteNG.log` string beside it is an unreachable fallback for a null product name. A stale zero-byte `%APPDATA%\mRemoteNG\mRemoteNG.log` survives from the log4net era, so looking for that name reads as "logging is broken" when it is working.
- [x] 5.3 Trigger an exception-producing path and confirm the stack trace is written to the log file. — Passed, observed while verifying `retire-legacy-rijndael-for-settings` task 5.6. A corrupted settings secret produced:

  ```
  2026-08-11 09:57:37,939 [2] ERROR- Options page "Credentials" could not load all of its settings
  Decryption failed.
     at ...AeadCryptographyProvider.SimpleDecrypt(...) in ...AeadCryptographyProvider.cs:line 326
     at ...SettingsSecretProtector.Unprotect(...) in ...SettingsSecretProtector.cs:line 94
     at ...CredentialsPage.LoadSettings() in ...CredentialsPage.cs:line 59
  ```

  Demystified, with file and line. The same line also evidences most of 5.2 — the output template's timestamp, thread id, level and message are all present — but at `ERROR` only, so 5.2 stays open until the other levels are seen.
- [x] 5.4 Change the log file path/directory at runtime and confirm subsequent messages go to the new location with no app restart and no dropped messages. — Passed, `LoggerFileOutputTests.ChangingTheLogPathRedirectsSubsequentMessagesWithoutLosingAny`: the message before the switch is in the old file and only there, the message after is in the new one, neither file gained or lost a line. This is the whole of what the Options page does — `NotificationsPage.SaveLoggingSettings` writes the setting, calls `Logger.Instance.SetLogPath(...)`, then `ApplyConfiguredLevel()`, which is exactly the sequence the test drives; what is left untested is the checkbox-to-setting plumbing, not the logging behaviour.
- [x] 5.5 Verify size-based rotation and the backup cap. — Passed at the real 10 MB threshold, no lowered constant needed: `LoggerFileOutputTests.TheLogRollsOnSizeAndRetainsTheActiveFilePlusFiveBackups` writes ~70 MB of 64 KB lines through the real logger (390 ms) and finds six files, none past the limit, with the original file discarded once retention was passed. It caught the second defect: `retainedFileCountLimit` counts the file being written, so the configured `5` kept four backups where log4net's `maxSizeRollBackups=5` kept five. Now `6`, restoring "1 main log + 5 backups" — the policy `CHANGELOG.md` has claimed since 2013.

  **The open question is answered, and the answer is reassuring: a launch does not burn a roll counter.** `ReopeningTheSamePathAppendsInsteadOfStartingANewFile` builds a fresh logger over an existing file and it appends; retention therefore counts full files, not launches, and the log a user is asked to send survives more than five restarts. What does start a new file is a *collision* — `AFileHeldByAnotherInstanceIsLeftAloneAndLoggingRollsToTheNextFile` shows Serilog leaving a held file untouched and rolling to `_001`. That, not size, is what left `.log`, `_001`, `_002` and `_003` at 1.1 MB, 164 KB, 22 KB and 133 KB beside the local build: three occasions with a second copy of mRemoteNG already holding the log. Reconfiguring the path to its current value was the other suspect and is not one — the file sink opens lazily, on first write, by which time the outgoing logger has released the file.
- [x] 5.6 Verify Debug-class message filtering still suppresses/allows log lines per the existing `LogMessageTypeFilteringOptions` settings. — Passed, both layers. `DisablingDebugMessagesKeepsThemOutOfTheLogFile` drives the real `MessageTypeFilterDecorator` over `LogMessageTypeFilteringOptions` with the setting off: the debug message never reaches the file, the informational one does. `DisablingDebugMessagesAlsoSilencesDirectLoggerCalls` covers what the migration added — the options pages log straight to `Logger.Instance.Log` and used to escape the filter entirely, and the level switch now catches them too.
- [x] 5.7 Run the full test suite (`run-tests-core.sh`) — 6553/6555 passed; the 2 failures are both the same pre-existing test (`ConnectionsServiceStartupPathTests.StartupConnectionPathReturnsSavedPathWhenItIsTheSoleCandidate`), confirmed unrelated by reproducing it against unmodified `dev` HEAD (stash/rebuild/retest) — fails identically with log4net still in place, so not a regression from this change. **Re-run after the 5.2/5.5 fixes and the new fixture: 7,657 passed, 0 failures** (`bash run-tests-core.sh`, 150s) — the count has grown with work merged since, and the startup-path test now passes too.

- [x] 5.8 Leave the runtime behaviour these verification tasks exercised under test rather than in a task note — `mRemoteNGTests\App\LoggerFileOutputTests.cs`, seven tests over the real `Logger` singleton and the production writer stack: line format and every severity, both filtering layers, runtime path change, size rotation and retention, append-on-reopen, and the held-file collision. Both defects found here would have shipped silently and neither would have been caught by any existing test
