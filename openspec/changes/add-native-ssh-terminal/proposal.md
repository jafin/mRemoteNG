## Why

SSH2 sessions are PuTTY windows reparented into an mRemoteNG tab.
`PuttyBase.Connect()` starts `putty.exe` and `NativeMethods.SetParent`s its top-level window into
the connection tab. That approach has costs that cannot be fixed from this side of the boundary:

1. **The terminal is a foreign process.** mRemoteNG owns a window handle and nothing else. It cannot
   read the scrollback, cannot inject or intercept text reliably, cannot theme the terminal beyond
   what PuTTY's own settings expose, and cannot tell a hung session from a busy one. Window chrome
   artifacts, focus quirks and repaint glitches are all symptoms of embedding a top-level window
   somewhere it was never designed to live.
2. **PuTTY's terminal is dated.** No true-colour or 256-colour by default, weak UTF-8 and CJK
   handling, no ligature or emoji support, and font rendering that ignores the host's DPI story.
3. **It cannot share anything with a second SSH feature.** `add-sftp-browser-panel` needs an SFTP
   channel beside the terminal. A PuTTY process cannot provide one, and `ssh.exe` cannot either:
   `ControlMaster` multiplexing is absent on Win32-OpenSSH and fails *silently* (verified against
   `OpenSSH_for_Windows_9.5p1`; see `add-ssh-agent-credential-resolver` design.md D3). Only a
   transport mRemoteNG owns can carry both.

Point 3 is the structural one. The other two are quality-of-life; this is the one that blocks a
feature outright.

The prerequisite work is already done. `add-ssh-agent-credential-resolver` produced a
backend-neutral credential model and `SshNetAuthAdapter`, so an SSH.NET-hosted terminal authenticates
identically to the PuTTY tab beside it, including agent identities.

## What Changes

Add a native SSH terminal: an SSH.NET transport driving an `xterm.js` front end hosted in WebView2,
as an **alternative** to the PuTTY-backed SSH2 protocol rather than a replacement for it.

- A new `ProtocolType` value, so a connection opts in per-connection and the existing SSH2 path is
  untouched. PuTTY remains the default until the native terminal has been exercised in real use.
- An SSH.NET `ShellStream` bridged to `xterm.js` over the WebView2 host/web message channel, with
  the front-end assets served from a mapped virtual host so nothing loads from the network.
- Terminal resize propagated to the remote PTY.
- Host key presentation and confirmation, which PuTTY previously owned.
- Font, colour scheme and scrollback settings, sourced from the existing options infrastructure.

**Explicitly out of scope:** removing PuTTY, migrating SSH1/Telnet/Rlogin/RAW (which share
`PuttyBase`), serial, and session logging. Each is a separate change once the terminal is proven.

## Impact

- Affected specs: `native-ssh-terminal` (new)
- Affected code: new `mRemoteNG/Connection/Protocol/SSH/Native/`, `ProtocolType`, `ProtocolFactory`,
  a new options page, embedded web assets, `mRemoteNG.csproj`
- Dependencies: **WebView2 is already referenced** (`Directory.Packages.props:35`,
  `Microsoft.Web.WebView2 1.0.4129.50`) and already used by the HTTP protocol
  (`Connection.Protocol.HTTPBase.cs`), so this adds no new NuGet dependency. It does add vendored
  `xterm.js` assets, which are new third-party code in-tree and need a licence review.
- **Runtime requirement:** WebView2 Runtime must be present. The HTTP protocol already assumes this,
  so the requirement is not new, but it becomes load-bearing for SSH rather than for an optional
  protocol — a terminal that fails to start because a runtime is missing is a much worse experience
  than a browser tab that does. Detection and a clear diagnostic are part of this change.

## Feasibility

Checked before writing this, because two of the three risks I expected turned out not to exist:

- `SshClient.CreateShellStream(string, uint, uint, uint, uint, int)` is **public**, so a PTY-backed
  shell is available without touching internals.
- `ShellStream.ChangeWindowSize(uint, uint, uint, uint)` is **public**, so terminal resize reaches
  the remote PTY. This was the risk most likely to sink the change, since a terminal that cannot
  resize is not usable.
- `HostKeyEventArgs` is public, so host key verification can be presented in the UI.

What is *not* settled, and is scoped as spikes rather than assumed:

- **Throughput.** Every byte crosses a JavaScript boundary. A terminal that stutters under
  `cat` of a large file is not a terminal. Needs measuring before the UI work, not after.
- **Input fidelity.** Function keys, Alt combinations, bracketed paste, IME and the clipboard are
  where terminal emulators are actually judged.
- **Asset delivery.** `SetVirtualHostNameToFolderMapping` needs files on disk; the alternative is
  `NavigateToString` with everything inlined. The choice affects packaging and CSP.
