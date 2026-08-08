## Why

mRemoteNG's only file transfer is `SSHTransferWindow`: a standalone form where the user retypes the
host, port, username and password, picks one local file and one remote path, and clicks Transfer.
It is not attached to any connection, so nothing it needs is already known, and it can move exactly
one file in one direction.

What is missing is a file browser beside the session — list a remote directory, navigate it, move
files both ways, rename and delete, edit a file in place. That is the standard shape of an SSH
client and the reason people keep a second tool open next to mRemoteNG.

Two things that previously blocked this are now resolved:

1. **Credentials.** `add-ssh-agent-credential-resolver` produced a backend-neutral credential model,
   so a panel can authenticate exactly as the session beside it does — including external credential
   providers and SSH agent identities — instead of asking the user to retype anything.
2. **The transport question.** It was assumed the panel would share the terminal's connection. It
   cannot: SSH.NET's `ISession` is `internal`, and `ControlMaster` multiplexing is absent on
   Win32-OpenSSH and fails *silently* (verified against `OpenSSH_for_Windows_9.5p1`). The panel
   therefore opens its **own** SSH connection — which is what makes it independent of the terminal
   rewrite, and buildable now.

The agent work is what makes a second connection acceptable: with an agent, the second
authentication is silent.

## What Changes

An SFTP file browser hosted beside an SSH session, on its own SSH.NET connection.

- A file list for the remote host: name, size, modified time, permissions, with folders distinguished
  from files, sorted and navigable.
- Navigation: open a folder, up, back, forward, home, and a path box for typing a path.
- Transfers: upload and download with progress and cancellation.
- File management: rename, delete, create folder, create file.
- Open a remote file for editing: download to a temporary location, launch the local editor, and
  offer to upload it back when it changes.
- Drag and drop from Explorer to upload.
- A toggle for hidden files.
- Connection state shown in the panel, and a way to close and reopen it without dropping the session.

**Explicitly out of scope:** SCP (SFTP only — `ScpClient` has no async API and no directory listing),
recursive directory upload and download, server-to-server transfer, and replacing
`SSHTransferWindow`, which keeps working as it does today.

## Impact

- Affected specs: `sftp-browser-panel` (new)
- Affected code: new `mRemoteNG/UI/Window/Sftp/` and `mRemoteNG/Connection/Sftp/`,
  `mRemoteNG/Connection/InterfaceControl.cs` for the split host, a new options page, language
  resources. `ProgressReportingStream` is reused and needs a write-counting counterpart for
  downloads, since `SftpClient.DownloadFileAsync` has no progress callback — the same gap already
  worked around for uploads.
- Dependencies: none new. SSH.NET is already referenced and `SftpClient` exposes async list,
  download, upload, rename, delete and create-directory operations. `ObjectListView` is already a
  project reference and is the natural list control.

## The costs, stated up front

- **A second connection per session.** Twice the authentications, twice the transports. With an
  agent or a stored credential this is invisible. With a server-issued second factor it means a
  second prompt, and there is no way around that without transport sharing, which is unavailable.
  The panel is therefore opened on demand, not automatically, until this is understood in real use.
- **Splitting the tab touches how protocols are hosted.** PuTTY's window is reparented straight onto
  `InterfaceControl` with `NativeMethods.SetParent`. Introducing a split means parenting it into a
  child panel instead, and getting that wrong reintroduces exactly the window-chrome and focus
  artifacts that make the current embedding awkward. This is the main integration risk and is scoped
  as a spike.
- **Editing a remote file means writing it to local disk.** Temporary, but real, and it needs the
  same care the credential work applied to temporary key files.
