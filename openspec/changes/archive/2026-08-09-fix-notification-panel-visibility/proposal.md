## Why

The notifications panel does not show most of what mRemoteNG reports, and in one case the reporting
never happens at all. Three independent defects:

1. **Info and warning messages are hidden by default.** `OptionsNotificationsPage.settings` ships
   `NotificationPanelWriterWriteInfoMsgs` and `NotificationPanelWriterWriteWarningMsgs` as `False`.
   The text log writer ships the same two as `True`, so the product's own defaults disagree about
   whether these messages are worth keeping. Every diagnostic added by
   `add-ssh-agent-credential-resolver` is an info or warning message, so that change's core
   behaviour — report a credential the backend cannot honour instead of dropping it silently —
   currently reports into a panel nobody sees.

2. **Messages logged before the writers are attached are lost permanently.**
   `FrmMain_Load` (`mRemoteNG/UI/Forms/frmMain.cs`) calls `SettingsLoader.LoadSettings()` at line 261
   and only attaches the message writers at line 266. `MessageCollector.AddMessages` stores the
   message and raises `CollectionChanged`, which at that point has no subscribers, and nothing ever
   replays the backlog. Everything `SettingsLoader` reports is therefore discarded — including
   `AddExceptionMessage("Loading settings failed", ex)` and
   `AddExceptionMessage("Settings.Upgrade() failed", ex)`. These are errors, they are enabled in
   every writer's default filter, and they are silently dropped from the panel, the popup writer and
   the log file alike. The existing code acknowledges the ordering — line 269 manually re-posts a
   startup timing message with the comment *"Post early phase timing now that messageCollector is
   ready"* — but works around one symptom rather than the cause.

3. **Messages carry no visible timestamp.** `Message.Date` is populated and `IMessage` exposes it,
   but `NotificationMessageListViewItem` sets only `Text`, and `ErrorAndInfoWindow` declares a single
   `clmMessage` column. There is no way to tell whether an entry is from this connection attempt or
   from twenty minutes ago, which makes the panel hard to use for the thing it exists for.

Defect 2 is the significant one: it is silent data loss covering the startup phase most likely to
fail, and it affects every writer rather than just the panel.

## What Changes

- Ship the notification panel's info and warning filters enabled, matching the text log writer.
- Deliver the backlog to writers when they are attached, so no message is lost to ordering. The
  collector gains an operation that subscribes and hands over what it already holds as one step.
- Add a timestamp column to the notifications panel.
- Return a snapshot from `MessageCollector.Messages` rather than the live backing list, so enumerating
  it while a background thread is adding cannot throw.
- Make the SSH agent's "too many identities" warning panel-visible; it is actionable and was marked
  log-only.

## Impact

- Affected specs: `notification-panel` (new)
- Affected code: `mRemoteNG/Properties/OptionsNotificationsPage.settings` and its designer,
  `mRemoteNG/Messages/MessageCollector.cs`, `mRemoteNG/App/Initialization/MessageCollectorSetup.cs`,
  `mRemoteNG/UI/Forms/frmMain.cs`, `mRemoteNG/UI/NotificationMessageListViewItem.cs`,
  `mRemoteNG/UI/Window/ErrorAndInfoWindow.cs` and its designer,
  `mRemoteNG/Security/Ssh/Agent/SshNetAgentProvider.cs`, language resources.
- **User-visible:** the panel becomes considerably busier by default. This is the intended outcome —
  the messages already exist and are already written to the log — but it is a noticeable change for
  anyone used to a panel that only ever showed errors. `OnlyLog` remains the lever for keeping
  genuinely noisy diagnostics out of the panel.
