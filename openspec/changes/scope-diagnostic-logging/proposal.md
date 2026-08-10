## Why

The application log is written at maximum verbosity with no way to lower it:

```csharp
Log = new LoggerConfiguration()
    .MinimumLevel.Verbose()
```

`App/Logger.cs:48`

`MinimumLevel.Verbose()` is hardcoded in `SetLogPath`, which is the only place the logger is built.
Every debug message from every subsystem lands in `%LOCALAPPDATA%\mRemoteNG\mRemoteNG.log` on every
run, for every user, forever — the file rolls at a size limit rather than being something the user
opted into.

Separately, `StartupDataLogger.LogCmdLineArgs` writes the command line verbatim:

```csharp
string data = $"Command Line: {string.Join(" ", Environment.GetCommandLineArgs())}";
```

`App/Initialization/StartupDataLogger.cs:132`

while `DebugReportBuilder.cs:88` puts the same data through `SanitizeText` first, which replaces the
user profile path with `%USERPROFILE%`. Two paths log the same thing, one redacted and one not.

Raised as L-2 in the upstream security audit ([mRemoteNG#3416](https://github.com/mRemoteNG/mRemoteNG/issues/3416)).
The file it names does not exist here — `replace-log4net-with-serilog` removed log4net — but the
substance survived the migration.

**The audit's stated worry does not hold and is worth recording.** It asks whether command-line
arguments carry secrets. They do not: no mRemoteNG argument accepts a password, and `CreateQuickConnect`
parses `user@host:port` only (`ConnectionsService.cs:120-131`) — a username, never a secret. A search
for password values reaching any log or message-collector call returns nothing. So this is verbosity
and path hygiene, not credential leakage, and it is INFO rather than the LOW the audit implies.

## What Changes

- The log level is a setting rather than a constant, defaulting to information. Verbose stays
  available for troubleshooting, chosen rather than imposed.
- `LogCmdLineArgs` routes through the same sanitiser `DebugReportBuilder` already uses, so the two
  paths that log the command line agree.
- No change to what is logged, only to how much of it and how the path is written.

## Capabilities

Adds `diagnostic-logging`. `replace-log4net-with-serilog` moved the implementation without ever
stating what the log is for or who decides its level, which is why the verbosity came across
unexamined; this states it.

## Impact

`mRemoteNG/App/Logger.cs`, `mRemoteNG/App/Initialization/StartupDataLogger.cs`,
`mRemoteNG/Properties/OptionsNotificationsPage.settings`, and the notifications options page.

The change is small and the risk is a support one rather than a technical one: **a lower default log
level means the next bug report arrives with less in it.** Anyone diagnosing a problem from a user's
log today gets everything without asking. That is the actual trade, and it argues for making the
verbose setting easy to reach and obvious in the options rather than for keeping it as the default —
a log nobody asked for is not a diagnostic tool, it is a file that accumulates.
