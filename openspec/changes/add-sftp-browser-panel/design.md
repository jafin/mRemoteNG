# Design

## Context

The only file transfer today is `SSHTransferWindow` + `SecureTransfer`: a standalone form, unattached
to any connection, that moves one file in one direction after the user retypes credentials
mRemoteNG already holds.

This change adds a browser beside the session. Everything it needs from SSH.NET is public and async:
`ListDirectoryAsync`, `DownloadFileAsync`, `UploadFileAsync`, `RenameFileAsync`, `DeleteAsync`,
`DeleteFileAsync`, `DeleteDirectoryAsync`, `CreateDirectoryAsync`. `ObjectListView` is already a
project reference.

## Goals

- A file browser that authenticates itself, from the connection it belongs to.
- Independent of the terminal rewrite, so it can ship first.
- Responsive: no remote call on the UI thread.

## Non-Goals

- SCP. `ScpClient` has no async API and no directory listing.
- Recursive directory transfer, server-to-server transfer.
- Replacing `SSHTransferWindow`.

## Decisions

### D1 — The panel opens its own SSH connection

Sharing the session's transport was the original assumption and it is not available:

- SSH.NET's `ISession`, `IServiceFactory` and `BaseClient.Session` are all `internal`. A second
  channel on the terminal's transport needs an upstream change.
- `ssh.exe` cannot multiplex on Windows. `ControlMaster`/`ControlPath` parse and are echoed by
  `ssh -G`, but `ssh -O check` fails `getsockname failed: Not a socket`. It fails *silently*, which
  is worse than failing loudly — a naive implementation looks correct while every operation
  re-authenticates. Upstream `PowerShell/Win32-OpenSSH#1328` open since 2019.

So: its own connection. The consequence is a second authentication, and the reason that is now
acceptable is the agent work — with an agent or a stored credential it is silent.

It is **not** silent for a server-issued second factor. That is a genuine limitation, not an
oversight, and it is why the panel opens on demand rather than automatically (D3).

The compensation is real independence: the panel works beside a PuTTY SSH2 session, and does not
wait on `add-native-ssh-terminal`.

### D2 — Credentials come from the existing resolver

`SshCredentialResolutionOptions.ForSshNet(agentEnabled)` + `SshNetAuthAdapter.Translate`, exactly as
`SecureTransfer` now does. External providers, agent identities, vault key material and passwords all
work with no new credential code.

This is the whole reason the panel is cheap to build, and it is what stops the transfer window's
defect — making the user retype what mRemoteNG already knows — being carried forward.

### D3 — Opened on demand, not automatically

The request was an automatic split beside every SSH session. Deliberately not doing that initially:
every session would silently open a second connection and authenticate a second time. For agent users
that is invisible; for a second-factor user it is a second prompt on every connection.

Ship it on demand, learn what the second connection actually costs, and revisit automatic opening as
a setting once that is known. Reversing the default later is easy; un-shipping a feature that prompts
twice per connection is not.

### D4 — The split lives *around* `InterfaceControl`, not inside it

**Revised 2026-08-09 after the spike. The original text below was wrong and is kept for the record.**

The split is a `SplitContainer` inside `ConnectionTab`. A session's `InterfaceControl` is parented
into `Panel1`; a side panel goes in `Panel2`, collapsed until asked for. `InterfaceControl` itself is
untouched.

This matters because **ten** protocols reparent a native window onto `InterfaceControl.Handle` —
PuTTY, OpenSSH, PowerShell, WSL, Terminal, AnyDesk, MSRA, VMRC, Winbox and IntegratedProgram — and
RDP and PuTTY additionally drive their resize logic from `InterfaceControl.Size`. Splitting *inside*
`InterfaceControl` would have required changing every one of those `SetParent` targets, and would
have left `InterfaceControl.Size` describing the session area *plus* the panel, quietly breaking the
resize maths in RDP and PuTTY. Splitting *around* it changes no protocol at all and keeps
`InterfaceControl.Size` meaning exactly what it always did: the session area.

Two constraints came out of building it:

- **The split must exist before the session does.** Reparenting a managed control destroys and
  recreates its handle, which would orphan every native window a protocol had already `SetParent`ed
  onto it. So `ConnectionTab` builds the `SplitContainer` in its constructor and never rebuilds it;
  showing a panel only un-collapses `Panel2`. Adding a splitter lazily, when the user first opens the
  panel, is not an option.
- **Collapsing and expanding does not resize the tab.** Protocols take their resize cue from the
  tab's `Resize`, and only `Panel1` changes size here, so the session would keep its old dimensions
  and be clipped by the panel that just appeared. `ProtocolBase.NotifyHostResized` exists for the
  host to say so explicitly.

Six places assumed `InterfaceControl.Parent` was the tab; they now ask `ConnectionTab.OwnerOf`, which
walks up. `InterfaceControl.FindInterfaceControl` indexed `tab.Controls[0]`/`[1]` at a fixed depth —
that fixed depth is what broke, so it now searches the tab's descendants and returns the last match,
preserving the SSH-tunnel case where a tab holds two.

---

### D4 (original, superseded) — The split lives in `InterfaceControl`, not in each protocol

The panel and the session view need to share the tab. Putting the split in `InterfaceControl` means
protocols stay unaware of it.

The complication is that protocols do not all host a managed control. `PuttyBase` and
`ProtocolOpenSSH` reparent a native window with `NativeMethods.SetParent(_handle,
InterfaceControl.Handle)`. Splitting means parenting into a child panel instead, and getting that
wrong reintroduces the window-chrome and focus artifacts that make the current embedding awkward.

This is the main integration risk and is a spike (task 1.1), not an assumption. If it proves messy,
the fallback is a dockable window bound to the active connection — worse ergonomics, no reparenting
risk.

### D5 — Progress is counted from the stream

Neither `UploadFileAsync` nor `DownloadFileAsync` takes a progress callback. Uploads already solve
this with `ProgressReportingStream`, which counts bytes as the uploader reads them. Downloads need
the mirror image — counting bytes as they are written — so that class grows a write path or a
counterpart.

The sync `DownloadFile(string, Stream, Action<ulong>)` *does* take a callback, but taking it would
mean a synchronous download on a background thread and a different progress mechanism per direction.
Not worth the asymmetry.

### D6 — Every remote call is async, off the UI thread

A listing over a slow link takes seconds; on the UI thread that freezes the application including the
session beside the panel. All operations are async with a `CancellationToken`, and results marshal
back to the UI thread to update the list.

Cancellation is required by the spec rather than optional: a transfer that cannot be cancelled leaves
the user with a frozen panel and no recourse.

### D7 — Editing a remote file writes it to local disk

Download to a temporary location, launch the local editor, watch for changes, offer to upload back,
delete the temporary copy when done.

The file is unencrypted on local disk for the duration. `add-ssh-agent-key-injection` exists because
of exactly this hazard with private keys; the same care applies here, and the same caveat holds —
a best-effort cleanup is skipped entirely if the process dies.

## Risks

- **Reparenting the native session window into a split** (D4). The main one.
- **A second authentication per panel** (D1). Mitigated by the agent, unavoidable for MFA.
- **Temporary local copies of remote files** (D7).
- **Scope.** A file browser attracts features indefinitely — recursive transfer, bookmarks, dual-pane,
  permission editing, archives. The listed scope is what makes the panel useful; everything else is a
  later change.

## Open Questions

- Should the panel eventually open automatically, as a setting? Depends on what D1's second
  connection costs in practice.
- Should it be reachable for a connection that has no open session — a standalone browser for a saved
  connection?
- Does `SSHTransferWindow` have a future once the panel exists, or does it become the panel's
  one-shot mode?
