# Design

## Context

SSH2 today is `putty.exe` reparented into a tab via `NativeMethods.SetParent` (`PuttyBase.Connect()`).
This change adds an in-process alternative: SSH.NET for the transport, `xterm.js` in WebView2 for the
emulator.

Verified before committing to the approach — the three things most likely to make it impossible:

| Need | API | Status |
|---|---|---|
| PTY-backed shell | `SshClient.CreateShellStream(string, uint, uint, uint, uint, int)` | public |
| Resize the remote PTY | `ShellStream.ChangeWindowSize(uint, uint, uint, uint)` | public |
| Host key confirmation | `HostKeyEventArgs` | public |

None requires reflection or an upstream change. This is the opposite of the situation in
`add-ssh-agent-credential-resolver`, where `ISession` being `internal` blocked transport sharing
outright — worth stating plainly, because that earlier finding is what makes this change look
riskier than it is.

WebView2 is already a dependency (`Directory.Packages.props:35`) and already in use by the HTTP
protocol, which also gives a working precedent for `CoreWebView2Environment` setup and a per-instance
user data folder (`Connection.Protocol.HTTPBase.cs`).

## Goals

- A usable SSH terminal that mRemoteNG owns end to end.
- Authentication identical to the PuTTY tab beside it, via the existing credential model.
- A transport that a future SFTP panel can share.

## Non-Goals

- Removing PuTTY, or migrating SSH1/Telnet/Rlogin/RAW off `PuttyBase`.
- Session logging, zmodem, serial.
- Matching PuTTY feature-for-feature. The bar is "better for the common case, honest about the rest".

## Decisions

### D1 — A new `ProtocolType`, not a flag on SSH2

Adding a value to `ProtocolType` and a case to `ProtocolFactory.CreateProtocol` keeps the existing
SSH2 path untouched and makes the choice visible and reversible per connection.

*Alternative rejected:* a global "use native terminal" setting that reinterprets SSH2. It silently
changes what every existing SSH connection does, and gives no way to run one connection each way
while comparing them — which is exactly what is needed to find out whether the emulator is good
enough.

The cost is a new protocol in the dropdown, and eventual migration if the native terminal becomes the
default. That is a better problem than a setting that rewrites existing connections.

### D2 — `ShellStream` bridged over the WebView2 web-message channel

Output: read from `ShellStream` on a background loop, post to the page. Input: `postMessage` from the
page, written to `ShellStream`.

The alternative is to expose a .NET object to JavaScript via `AddHostObjectToScript`. Rejected: it
widens what page script can reach into the host, for a channel that only ever carries opaque byte
buffers in two directions.

**Encoding is the trap.** UTF-8 sequences split across two reads must not be corrupted, so decoding
is stateful — a `Decoder` retained across reads, not `Encoding.UTF8.GetString` per buffer. The spec
requires this because it is the defect that will not show up in testing against ASCII output and will
show up immediately for any user writing a non-Latin language.

### D3 — Assets vendored and served locally, never fetched

`xterm.js` is vendored into the repo and served to the WebView with no network access. The terminal
displays output from a remote host inside a browser engine; anything that lets that output reach the
network or be evaluated as markup turns terminal output into a code execution surface.

Delivery mechanism is deliberately left to task 2 rather than decided here:
`SetVirtualHostNameToFolderMapping` needs files on disk next to the executable, and
`NavigateToString` needs everything inlined into one document. The choice affects packaging, the
installer and CSP, and should be made against a working prototype.

Vendoring third-party JavaScript is new for this repo and needs a licence review (`xterm.js` is MIT)
and a recorded provenance and version for the vendored files.

### D4 — Credentials come from the existing resolver

`SshCredentialResolutionOptions.ForSshNet(agentEnabled)` and `SshNetAuthAdapter.Translate` already
exist and are used by `SecureTransfer`. The terminal uses the same path.

This is the payoff for the earlier change: agent identities, vault-supplied keys, key material loaded
from memory and password auth all work here on day one, with no new credential code.

`SshNetAuthentication.UnansweredPrompts` also becomes more valuable here than it is in the transfer
window — a terminal *can* eventually prompt the user for a second factor, which is the natural place
to close the keyboard-interactive gap. Not in this change.

### D5 — Host key verification is now ours

PuTTY owned this. In-process means inheriting it. Silently accepting host keys would remove a
protection users have today, so the spec requires presentation and confirmation.

Where the accepted keys are stored — mRemoteNG's own store, or the existing OpenSSH `known_hosts` —
is deferred to a task. Reusing `known_hosts` is friendlier but means writing a file other tools own.

### D6 — Throughput is a spike, before the UI work

Every byte crosses a JavaScript boundary. If `cat` of a large file stutters, the approach needs
batching, a different channel, or reconsideration — and that must be known before the input handling,
theming and settings work is done on top of it.

Measured against a real session, not a synthetic loop.

## Spike findings

Measured with `spikes/native-ssh-terminal/` against a containerised OpenSSH server
(`linuxserver/openssh-server`, loopback, port 2222, publickey auth), xterm.js 6.0.0 in WebView2
1.0.4129.50, terminal 98x31. Raw numbers in that directory's README.

### S1.1 — Throughput (task 1.1): the pipe is not the bottleneck

| Scenario | Bytes | Wall | Rendered | Max render lag | Max hitch |
|---|---|---|---|---|---|
| `cat` 5.3 MB ASCII | 5,395,102 | 205 ms | **25.1 MB/s** | 190 ms | 185 ms |
| `cat` 2.2 MB UTF-8/CJK | 2,260,099 | 136 ms | **15.9 MB/s** | 90 ms | 86 ms |
| 200 full-screen redraws | 913,561 | 471 ms | **2.4 ms per repaint** | 5 ms | 5 ms |

"Rendered" is measured at xterm's `write()` completion callback, not at the point bytes are handed
to the page — bytes accepted are not bytes on screen, and only the latter is what a user sees.

**Verdict: proceed, with no batching layer.** 25 MB/s sustained over the web-message channel is an
order of magnitude more than a real SSH session delivers; the transport will starve the renderer long
before the renderer becomes the limit. The full-screen redraw case — the one that decides whether an
editor feels alive — repaints in 2.4 ms with a worst-case 5 ms hitch, comfortably inside a frame.

Two honest caveats:

- This is loopback. Real latency makes the renderer's job *easier*, not harder, so the result holds
  directionally, but absolute figures are a ceiling rather than a prediction.
- The 185–190 ms "max hitch" on the bulk `cat` runs is a single event at the start of the burst, not
  sustained stutter: median render gap is 0.0 ms and p95 is 0.0 ms across 1,160 chunks. It is the
  first-chunk reflow, and it is the one thing worth re-checking once real theming and font loading
  are in place.

The decoder held: **zero U+FFFD** across 518 chunk boundaries of multi-byte CJK/Greek/Cyrillic
output (1,240,099 chars from 2,260,099 bytes). This is the defect D2 predicts, and it is only absent
because the `Decoder` is retained across reads.

### S1.2 — Input fidelity (task 1.2): the emulator is fine, the binding layer is the work

All eleven checks pass. Function keys, Alt combinations, Ctrl-C, Ctrl-Z with `fg`, bracketed paste,
clipboard both directions, IME composition, resize during a full-screen application, and remote
markup rendering as literal text.

**Nothing here invalidates the approach.** No input case was found that `xterm.js` cannot express,
which is the question section 1 existed to answer.

That result is less reassuring than it looks, because of *how* the passes were reached:

- **Two apparent failures were bad test procedure, not defects.** Ctrl-Z was tested at an idle
  prompt, where doing nothing is correct; bracketed paste was tested inside `cat -v`, where bash has
  already cleared mode 2004 so not wrapping is correct. Both now have unambiguous procedures, and
  bracketed paste is asserted automatically by the spike rather than left to a human getting it
  right.
- **Two were real defects in the binding layer**, and both are work items for section 5 rather than
  facts about xterm:

  1. **The clipboard is entirely the host's job.** xterm does not copy on selection, and inside
     WebView2 only Shift+Insert pastes unaided — Ctrl+V and middle-click do nothing at all. Copy on
     select, Ctrl+Insert, Ctrl+Shift+C, Ctrl+Shift+V, Ctrl+V and middle-click each had to be built.
  2. **Binding a paste key double-pastes unless the browser is suppressed.** Returning `false` from
     `attachCustomKeyEventHandler` stops xterm handling the key but not the browser's default paste
     and not key auto-repeat; either lands a second paste. It presented as an intermittent Ctrl+V
     quirk and was neither. A duplicated paste of a command line is a command run twice, so this is
     a correctness issue, not a polish one.

Both confirm the risk this document already named: *"xterm.js handles these well, but the binding
layer is ours and is where the defects will be."* It was right, and section 5 should be estimated on
that basis — the emulator is not the work, the wiring is.

**Carry into section 5, established here rather than to be rediscovered:**

- Clipboard belongs on the host, not `navigator.clipboard` — the web API is gesture- and
  permission-gated in WebView2, and a WinForms app already owns the Windows clipboard.
- Paste must be routed back *through the page* so xterm applies bracketed-paste wrapping. Writing
  it straight to `ShellStream` silently drops that protection.
- Paste key handlers must `preventDefault()` and ignore `event.repeat`.
- Ctrl+C must stay SIGINT; copy goes on Ctrl+Insert, as PuTTY does.

**One decision deliberately left open:** Ctrl+V is readline's quoted-insert (`^V`), so binding it to
paste removes the only way to type a literal control character. PuTTY declines that trade; a
Windows-native application probably should not. The spike binds it so the behaviour is visible.
This likely wants to be a setting rather than a default chosen by whoever writes 5.2.

### S1.3 — Asset delivery (task 1.3): virtual host mapping

Both mechanisms were implemented and measured. Performance is a wash — inline was ~15% faster to
first paint (894 ms vs 909 ms) and marginally faster on bulk throughput, all within run-to-run
noise. **The decision is therefore made entirely on CSP and packaging, as D3 anticipated.**

| | `SetVirtualHostNameToFolderMapping` | `NavigateToString` |
|---|---|---|
| Origin | real (`https://terminal.spike.invalid`) | opaque |
| `script-src` | **`'self'`** — no inline script can run | must be `'unsafe-inline'` |
| `style-src` | `'self' 'unsafe-inline'` (see below) | `'unsafe-inline'` |
| Size ceiling | none | 2 MB document limit; assets are 488 KB today (24%) |
| Packaging | ships an `assets/` folder beside the executable | single binary, document rebuilt per session |

**Decision: `SetVirtualHostNameToFolderMapping`, with `CoreWebView2HostResourceAccessKind.Deny`.**

An opaque origin makes `'self'` match nothing, so the inline route cannot express "scripts may only
come from the application" — it can only say "inline script is allowed", which is precisely the
grant you least want on a surface whose whole threat model is remote output inside a browser engine.
Verified working: the spike's mode A page loads and runs under `script-src 'self'`. The cost is an
`assets/` folder in the installer (task 2.3), which is the cheaper half of the trade.

**`style-src` cannot be `'self'`, and the failure is silent.** xterm's DOM renderer builds its
colour rules into `<style>` elements it injects at runtime, so a strict `style-src 'self'` blocks
every one of them. Nothing errors and nothing is logged where a user would see it: the terminal
renders, accepts input and reports correct throughput, but every cell loses its colour and the text
comes out near-black on the host background. Measured directly — seven `style-src-elem` violations
at startup, `canvases 0`, and cell spans carrying only `letter-spacing`.

This is worth stating plainly because an earlier revision of this section claimed mode A could run
with "no inline anything", which was wrong. Inline *style* is a much narrower concession than
inline script — it cannot execute code — and `script-src 'self'` plus `default-src 'none'` still
holds, so the decision above does not change. But the policy shipped in task 2.2 must include
`style-src 'unsafe-inline'` or the terminal will be unreadable, and the symptom will not point at
the CSP.

Untested alternative, if the concession is ever judged too expensive: a canvas or WebGL renderer
addon paints cells rather than styling them and may need no injected stylesheet at all. That trades
a CSP grant for another vendored dependency.

### S1.4 — WebView2 runtime absence (task 1.4, confirmed)

`CoreWebView2Environment.CreateAsync(null, userDataFolder)` throws
`WebView2RuntimeNotFoundException` (namespace `Microsoft.Web.WebView2.Core`) when no compatible
runtime is installed. Confirmed present in the pinned SDK, `Microsoft.Web.WebView2` 1.0.4129.50.
Passing `null` for `browserExecutableFolder` selects the machine-wide runtime first, then per-user;
the exception is raised only when neither is found.

The failure is therefore **distinguishable by type**, which is what the spec's "not reported as an
authentication or network error" scenario needs — no message-text matching required.

The existing HTTP protocol does not distinguish it.
`Connection.Protocol.HTTPBase.InitializeWebView2Async` wraps the call in `catch (Exception ex)` and
reports everything as `Language.HttpSetPropsFailed`
(`mRemoteNG/Connection/Protocol/Http/Connection.Protocol.HTTPBase.cs:250`). Copying that pattern into
the terminal would violate the spec. The terminal must catch `WebView2RuntimeNotFoundException`
specifically, ahead of the general handler, and fail the connection with a message that names the
WebView2 runtime and where to get it.

Out of scope here, but worth noting: the same blind catch means a missing runtime today surfaces to
HTTP users as an unexplained "set properties failed". Fixing that belongs to its own change.

## Risks

- **Emulator fidelity.** Function keys, Alt combinations, bracketed paste, IME, mouse reporting and
  the clipboard are where terminal emulators are judged. `xterm.js` handles these well, but the
  binding layer is ours and is where the defects will be.
- **WebView2 runtime absence.** Already assumed by the HTTP protocol, but a missing runtime failing an
  SSH session is much worse than failing a browser tab. Needs detection and a clear diagnostic.
- **Scope creep toward replacing PuTTY.** `PuttyBase` also backs SSH1, Telnet, Rlogin and RAW.
  Nothing here should touch them.
- **Vendored assets going stale.** A pinned copy of a JavaScript library in-tree needs a recorded
  version and a way to notice security updates.

## Open Questions

- ~~Serve assets from disk (`SetVirtualHostNameToFolderMapping`) or inline (`NavigateToString`)?~~
  **Resolved by S1.3 — virtual host mapping, on CSP grounds.**
- ~~Store accepted host keys in mRemoteNG's own store, or reuse OpenSSH `known_hosts`?~~
  **Resolved in 6.2 — mRemoteNG's own file.** Writing `known_hosts` means mutating a file another
  tool owns, in a format carrying hashed hostnames, CA markers and revocation entries that we could
  corrupt while meaning well. Owning a small file is reversible; adopting someone else's is not.
  Reading `known_hosts` to skip a first-connection prompt remains available as a purely additive
  follow-up and does not disturb this.
- Should the native terminal eventually replace SSH2, and if so, how do existing connections migrate?
- Can the keyboard-interactive prompt gap be closed here, given a terminal has somewhere to prompt?
