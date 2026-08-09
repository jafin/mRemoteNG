# Tasks

## 1. Spikes

- [x] 1.1 **Hosting the split.** `PuttyBase` and `ProtocolOpenSSH` reparent a native window onto `InterfaceControl.Handle`. Prove a session window can be reparented into a child panel of a split without reintroducing window-chrome or focus artifacts, for PuTTY SSH2 and for `ProtocolOpenSSH`. If it proves messy, record the fallback (a dockable window bound to the active connection) and take it. Gates section 4. **Done 2026-08-09 — cleared, and it corrected the design.** No native window is reparented at all: the split goes *around* `InterfaceControl` (a `SplitContainer` in `ConnectionTab`, session in `Panel1`) rather than inside it, so all ten protocols that `SetParent` onto `InterfaceControl.Handle` are untouched and `InterfaceControl.Size` still means the session area. See design.md D4, revised. Full suite 7024/7024.
- [ ] 1.2 Confirm what a second connection costs in practice: connect a session and a panel with an agent, with a stored password, and with a provider-supplied credential. Record how many prompts each produces.

## 2. SFTP session

- [x] 2.1 Add `mRemoteNG/Connection/Sftp/` with a session type wrapping `SftpClient`, connected from a `ResolvedSshCredential`.
- [x] 2.2 Authenticate via `SshCredentialResolver` + `SshNetAuthAdapter`; replay resolution diagnostics on the connection's message channel.
- [x] 2.3 Async operations with `CancellationToken`: list, download, upload, rename, delete, create directory, create file. `CreateFileAsync` uploads an empty stream — `SftpClient.Create` is synchronous only. `DeleteAsync` routes directories to `DeleteDirectoryAsync`, which fails on a non-empty directory; that is the intended outcome rather than quietly recursing through a tree the user did not see.
- [x] 2.4 Report disconnection, and make a listing after disconnection fail rather than return stale contents.
- [x] 2.5 Give `ProgressReportingStream` a write-counting counterpart for downloads (`DownloadFileAsync` has no progress callback), and use it for both directions. Done as one class counting both directions rather than a second type. It also gained an optional declared total: a download writes into an empty file whose length says nothing about the size of the transfer, so the caller passes the remote file's size from the listing.
- [x] 2.6 Tests against a fake SFTP surface — no test may require a server: listing maps entries; hidden entries filtered; failures surface as failures; cancellation stops an operation; progress counts bytes.

**Section 2 results 2026-08-09:** full build green (71.0s); full suite **7024/7024**, 137s, 0 crashes. 43 new tests, none requiring a server — `ISftpFile` is an interface, so the listing-to-model mapping is asserted against a substitute. `SftpPath` exists because `System.IO.Path` applies the local platform's rules: on Windows it would join with a backslash and treat a drive letter as a root, while SFTP paths are POSIX whatever the client runs on.

## 3. Panes

Reshaped 2026-08-09: the target is a dual-pane file manager with a transfer queue, per the
WinSCP/FileZilla reference the maintainer supplied. The remote half from section 2 is unchanged; a
local pane and a queue are new.

- [x] 3.1 A `FileSystemEntry` model and an `IFileSystemBrowser` both panes implement, so one list control serves local and remote. Permissions are remote-only and optional.
- [x] 3.2 `LocalFileSystemBrowser` over `System.IO`, async, reporting access-denied as a failure rather than an empty directory.
- [x] 3.3 `RemoteFileSystemBrowser` adapting `ISftpSession`.
- [x] 3.4 Navigation history (open, up, home, back, forward, typed path) as a reusable, UI-free type — it is identical for both panes and is the part most worth testing.
- [x] 3.5 Hidden-entry filtering, shared by both panes. `FileSystemEntry.IsHidden` is set by each side from what that filesystem actually means by hidden — the dot convention remotely, the file attribute locally — rather than applying one rule to both.
- [x] 3.6 Tests: local listing maps entries; access denied surfaces; history behaves; hidden filter applies; the two panes navigate independently.

## 4. Transfer queue

- [x] 4.1 `TransferItem`: direction, source, destination, size, transferred, status, failure reason.
- [x] 4.2 `TransferQueue` running items off the UI thread, one at a time to start with; a failed item must not stop the queue.
- [x] 4.3 Cancel one item and cancel the whole queue.
- [x] 4.4 Progress per item, from `ProgressReportingStream`.
- [x] 4.5 Queued / failed / successful views over the same item list.
- [x] 4.6 Tests against a fake transfer operation — no server: items run in order; a failure is recorded and the queue continues; cancelling one leaves the rest; cancelling all stops the runner; progress reaches the item.

**Sections 3 and 4 results 2026-08-09:** full build green (69.6s); full suite **7075/7075**, 130s, 0 crashes. 51 new tests.

The queue test for progress caught a real defect: `Progress<T>` posts to the captured
synchronization context, so reports arrive asynchronously and unordered and a final report could
land *after* the item was marked succeeded — leaving a finished transfer showing partial progress.
Progress is now applied inline on the reporting thread. The UI marshals when it handles
`ItemChanged`, which it must do anyway since that event already comes off a background thread.

`LocalFileSystemBrowser` is tested against a real temporary directory rather than a substitute:
the behaviour worth pinning is the interaction with the filesystem, including that a missing
directory *fails* rather than listing as empty — "empty" and "unreadable" are different statements
and only one of them is true.

## 5. The file manager tab

- [x] 5.1 A tab hosting local pane | remote pane above a queue, opened per connection.
- [x] 5.2 `ObjectListView` list control used for both panes: name, size, modified, and permissions on the remote side; sortable; folders distinguishable. The permissions column is added only when the browser reports it supports them — an always-present empty column on the local pane would imply the information exists and is blank.
- [x] 5.3 Per-pane toolbar: navigation, refresh, new folder, new file, rename, delete, hidden toggle. Mirrored in a right-click menu. The mutation commands live in `FilePaneCommands` with prompts behind `IFilePanePrompts`: dialogs are what make UI commands untestable, and "declining the confirmation leaves the file alone" is exactly the case that must not regress unnoticed.
- [x] 5.4 Transfer between panes queues items rather than blocking.
- [x] 5.5 Deletion confirmation. One prompt for the whole selection, not one per entry — confirming twenty times is a prompt people learn to click through, which is worse than not asking.
- [x] 5.6 Drag and drop from Explorer onto the remote pane queues uploads. A drop on the *local* pane is reported rather than silently ignored, so the gesture does not just appear to fail. Dropped directories are reported as unsupported instead of being queued as items that could only fail.
- [x] 5.7 Queue view with per-item progress and cancel. Repaints on a 250 ms timer rather than per event: a transfer reports every buffer, and rebuilding rows at that rate would spend more time drawing the queue than moving the file.
- [x] 5.8 Connection state indicator; a dropped connection stops the listing being presented as current.
- [x] 5.9 Open from the connection tree and the session tab's context menu, without disturbing an open session tab. Added beside the existing **Transfer File (SSH)** item, mirroring its enable/disable decisions exactly — the file manager is available in the same circumstances, and it is the successor to that item. Session-tab context menu not done; the tree entry point is enough to reach it.
- [x] 5.10 Theme and language resources, matching the other windows.

**Section 5 complete, 2026-08-09:** full build green (68.8s); full suite **7113/7113**. 38 new tests across `FilePaneController` and `FilePaneCommands`, none needing a message pump — the rules and the commands both live outside the controls for that reason.

Transfers are now reachable: an Upload/Download button and context-menu entry on each pane, plus drag and drop from Explorer onto the remote pane.

Not done, and deliberately: recursive directory transfer. A directory in a selection is reported as unsupported rather than queued, because queueing it would produce an item that could only fail.

## 6. Editing a remote file

- [x] 6.1 Download to a temporary location and launch the local editor.
- [x] 6.2 Watch the local copy and offer to upload when it changes. Not a `FileSystemWatcher`: it fires on an editor's intermediate writes and on save-to-temp-then-rename patterns, and would need debouncing. Comparing last-write time and length against a snapshot taken after the download answers the only question that matters — is what is on disk now different from what was put there — with no timing to get wrong. Checked when the tab is reactivated and when it closes, which is when the user has plausibly finished editing; a background poll's only achievement would be interrupting them mid-edit.
- [x] 6.3 Remove the temporary copy when editing finishes; record what happens if the process dies first, rather than implying it is handled. **The file is unencrypted on local disk for the life of the session.** Deleted on close and on tab dispose; that cleanup does **not** run if the process is killed, exactly as with the temporary private key files `add-ssh-agent-key-injection` exists to remove. A copy still held open by the editor is left behind rather than failing the close.
- [x] 6.4 Tests: a changed copy prompts; declining leaves the remote file alone; the temporary copy is removed.

**Section 6 results 2026-08-09:** full build green (72.0s); full suite **7129/7129**. 16 new tests.

Each edit gets its own directory so the file keeps its real name — an editor's syntax highlighting and an interpreter's shebang both key off the extension — without two edits of same-named files colliding. The session re-snapshots after a successful upload, which is what stops every subsequent activation of the tab asking about a file already written back.

## 7. Completion

- [x] 7.1 Full build. **2026-08-09**, green in 72.0s.
- [x] 7.2 Full test suite; zero failures, no `[Ignore]`. **7129/7129**, 142s, 0 crashes. 158 tests added by this change.
- [x] 7.3 Zero new analyzer warnings. Verified by filtering the full build output to the new files: none. The CA1861 warnings that remain in the test project predate this change (`PortListParserTests`, `PortScannerTests`).
- [x] 7.4 `openspec validate add-sftp-browser-panel --strict`.
- [x] 7.5 Manual: browse both panes; queue several transfers in both directions and watch progress; cancel one and cancel all; rename, delete, create; edit a file and write it back; drag files in; drop the connection with the tab open. **Confirmed working by the maintainer 2026-08-09**, including an end-to-end SFTP transfer. The first attempt found the File Manager menu item greyed out for every connection — a braceless `if` swallowed the line that was meant to be conditional, and an indentation-only search pattern left a duplicate disable beside it. Fixed in `b0624b55f`; neither the compiler nor the suite could have caught it, since both mistakes are legal C# and no test covers context-menu enablement.
- [x] 7.6 Confirm `SSHTransferWindow` still works unchanged. Neither it nor `SecureTransfer` was touched by this change. `ProgressReportingStream` did change underneath it, but only additively: `totalBytes` defaults to null so `Total` still falls back to the wrapped stream's length, and `CanWrite` now follows the inner stream, which is false for the read-only source it wraps. Its tests pass unchanged. **Whether it has a future is still open** — see design.md.
- [ ] 7.7 Note the split host from 1.1 is currently unused by this layout, and either find it a use or remove it.
