# Tasks

## 1. Spikes

- [ ] 1.1 **Hosting the split.** `PuttyBase` and `ProtocolOpenSSH` reparent a native window onto `InterfaceControl.Handle`. Prove a session window can be reparented into a child panel of a split without reintroducing window-chrome or focus artifacts, for PuTTY SSH2 and for `ProtocolOpenSSH`. If it proves messy, record the fallback (a dockable window bound to the active connection) and take it. Gates section 4.
- [ ] 1.2 Confirm what a second connection costs in practice: connect a session and a panel with an agent, with a stored password, and with a provider-supplied credential. Record how many prompts each produces.

## 2. SFTP session

- [x] 2.1 Add `mRemoteNG/Connection/Sftp/` with a session type wrapping `SftpClient`, connected from a `ResolvedSshCredential`.
- [x] 2.2 Authenticate via `SshCredentialResolver` + `SshNetAuthAdapter`; replay resolution diagnostics on the connection's message channel.
- [x] 2.3 Async operations with `CancellationToken`: list, download, upload, rename, delete, create directory, create file. `CreateFileAsync` uploads an empty stream — `SftpClient.Create` is synchronous only. `DeleteAsync` routes directories to `DeleteDirectoryAsync`, which fails on a non-empty directory; that is the intended outcome rather than quietly recursing through a tree the user did not see.
- [x] 2.4 Report disconnection, and make a listing after disconnection fail rather than return stale contents.
- [x] 2.5 Give `ProgressReportingStream` a write-counting counterpart for downloads (`DownloadFileAsync` has no progress callback), and use it for both directions. Done as one class counting both directions rather than a second type. It also gained an optional declared total: a download writes into an empty file whose length says nothing about the size of the transfer, so the caller passes the remote file's size from the listing.
- [x] 2.6 Tests against a fake SFTP surface — no test may require a server: listing maps entries; hidden entries filtered; failures surface as failures; cancellation stops an operation; progress counts bytes.

**Section 2 results 2026-08-09:** full build green (71.0s); full suite **7024/7024**, 137s, 0 crashes. 43 new tests, none requiring a server — `ISftpFile` is an interface, so the listing-to-model mapping is asserted against a substitute. `SftpPath` exists because `System.IO.Path` applies the local platform's rules: on Windows it would join with a backslash and treat a drive letter as a root, while SFTP paths are POSIX whatever the client runs on.

## 3. The panel

- [ ] 3.1 `ObjectListView`-based list: name, size, modified time, permissions, folders distinguishable from files, sortable.
- [ ] 3.2 Navigation: open, up, home, back, forward, path box. Back/forward keep their own history.
- [ ] 3.3 Hidden-entry toggle.
- [ ] 3.4 Context menu: download, upload, rename, delete, new folder, new file, refresh.
- [ ] 3.5 Deletion confirmation.
- [ ] 3.6 Drag and drop from the local file manager uploads to the current directory.
- [ ] 3.7 Transfer progress with cancellation.
- [ ] 3.8 Connection state indicator.
- [ ] 3.9 Theme and language resources, matching the other windows.
- [ ] 3.10 Tests: navigation history behaves; the hidden toggle filters; a failed listing leaves the previous listing in place.

## 4. Hosting

- [ ] 4.1 Implement the split chosen in 1.1, in `InterfaceControl` if that spike cleared.
- [ ] 4.2 Open and close the panel without disturbing the session; reopening reconnects.
- [ ] 4.3 Confirm every protocol that is not SSH is unaffected.
- [ ] 4.4 Persist the split position per connection or globally, whichever matches existing layout handling.

## 5. Editing a remote file

- [ ] 5.1 Download to a temporary location and launch the local editor.
- [ ] 5.2 Watch the local copy and offer to upload when it changes.
- [ ] 5.3 Remove the temporary copy when editing finishes; record what happens if the process dies first, rather than implying it is handled.
- [ ] 5.4 Tests: a changed copy prompts; declining leaves the remote file alone; the temporary copy is removed.

## 6. Completion

- [ ] 6.1 Full build.
- [ ] 6.2 Full test suite; zero failures, no `[Ignore]`.
- [ ] 6.3 Zero new analyzer warnings.
- [ ] 6.4 `openspec validate add-sftp-browser-panel --strict`.
- [ ] 6.5 Manual: browse a deep tree; upload and download a large file and watch progress; cancel mid-transfer; rename, delete, create; edit a file and write it back; drag files in; disconnect the session with the panel open.
- [ ] 6.6 Confirm `SSHTransferWindow` still works unchanged.
