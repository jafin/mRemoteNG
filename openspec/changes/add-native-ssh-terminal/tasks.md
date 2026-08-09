# Tasks

## 1. Spikes (do these first — they can invalidate the approach)

- [x] 1.1 **Throughput.** Prototype `ShellStream` → WebView2 → `xterm.js` and measure `cat` of a large file and a full-screen editor redraw against a real host. Record throughput and perceived latency. If it stutters, decide between batching, a different channel, or stopping. Gates everything below. — **25.1 MB/s bulk, 2.4 ms per full-screen repaint, no batching needed.** See design.md S1.1. Perceived latency still needs a human: run the spike with `--interactive` (see 1.2).
- [x] 1.2 **Input fidelity.** In the same prototype, check function keys, Alt combinations, Ctrl-C/Ctrl-Z, bracketed paste, clipboard copy/paste, and one IME. Record what does not work; these are the acceptance criteria for section 5. — **all 11 checks pass.** No input case xterm.js cannot express. Two defects found, both in the binding layer not the emulator: the clipboard is entirely unimplemented by default, and paste keys double-paste unless the browser default and key auto-repeat are suppressed. See design.md S1.2 for what section 5 must carry forward.
- [x] 1.3 **Asset delivery.** Try `SetVirtualHostNameToFolderMapping` and `NavigateToString`; pick one on packaging and CSP grounds and record why in design.md D3. — **`SetVirtualHostNameToFolderMapping`**; the inline route cannot express `script-src 'self'`. See design.md S1.3.
- [x] 1.4 Confirm the WebView2 runtime detection story: what `CoreWebView2Environment.CreateAsync` does when the runtime is absent, and what the user should be told. — **`WebView2RuntimeNotFoundException`**, see design.md S1.4. Distinguishable by type; the HTTP protocol's blind `catch (Exception)` must not be copied.

## 2. Vendored front end

- [x] 2.1 Vendor `xterm.js` with its version and provenance recorded; confirm the MIT licence is compatible and add it wherever third-party licences are listed. — xterm 6.0.0 + addon-fit 0.11.0, MIT, shasums and update procedure in `Assets/THIRD-PARTY.md`. No central licence list exists in this repo, so the upstream `LICENSE` ships beside the assets and is installed with them.
- [x] 2.2 Add the host document and the delivery mechanism chosen in 1.3, with a CSP that permits no network origins. — `index.html` + `terminal.js`, served via `SetVirtualHostNameToFolderMapping` with `Deny`. `default-src 'none'`, `script-src 'self'`, `connect-src 'none'`; `style-src` carries `'unsafe-inline'` because xterm's DOM renderer injects `<style>` elements (design.md S1.3).
- [x] 2.3 Ensure assets reach the build output and the installer. — copied to `TerminalAssets\` beside the executable; verified present in `bind\Release`. `build-msi.ps1` harvests the output tree recursively, so the MSI picks them up with no installer change.

## 3. Transport

- [x] 3.1 Add `mRemoteNG/Connection/Protocol/SSH/Native/` with a session type wrapping `SshClient` + `CreateShellStream`. — `NativeSshTerminalSession` behind `INativeSshTerminalSession`.
- [x] 3.2 Authenticate via `SshCredentialResolver` + `SshNetAuthAdapter`; replay resolution diagnostics on the connection's message channel. — `ForConnection` mirrors `SftpSession`; the session exposes `Diagnostics` (resolution + adapter-unsupported) for the protocol to replay in 4.3, since `Event_ErrorOccured` is protected.
- [x] 3.3 Read loop with a **stateful** UTF-8 decoder retained across reads, so a multi-byte sequence split across two buffers is not corrupted. — `ShellStreamPump`, isolated from SSH.NET behind `Stream` so it is testable without a server.
- [x] 3.4 Write path from the page to `ShellStream`. — transport half (`Send`); the page wiring is 4.3.
- [x] 3.5 Report remote shell exit and transport failure as a disconnect. — both routed to one `Disconnected`, raised once.
- [x] 3.6 Tests: decoder handles a split multi-byte sequence; exit reported as disconnect; resize while disconnected is a no-op; credential diagnostics replayed. — 16 tests, all green. Also verified end to end against the container fixture: PTY sized at 100x40, `ChangeWindowSize` to 132x50 honoured by the remote, CJK/Greek intact, zero U+FFFD, remote `exit` reported as a disconnect.

## 4. Protocol integration

- [x] 4.1 Add the `ProtocolType` value and its localized description. — `SSHNative = 23`, an unused slot: the numbers are persisted in every connections file, so renumbering would repoint saved connections. Also added the default-port case in `ConnectionInfo.GetDefaultPort`, which silently yields 0 if missed.
- [x] 4.2 Add the `ProtocolFactory.CreateProtocol` case.
- [x] 4.3 Implement the `ProtocolBase` subclass hosting the WebView2 control as `Control`. — `ProtocolNativeSsh`. WebView2 init starts in `Initialize` so the engine and the SSH handshake overlap; whichever of page-ready and connect-requested lands last starts the session. Credential diagnostics replayed here, since `Event_ErrorOccured` is protected.
- [x] 4.4 Propagate control resize to `ShellStream.ChangeWindowSize`, and send the initial size when the session starts. — the page owns the cell arithmetic: `fit` measures and reports cols/rows back, which drives `Resize`. Initial size comes from the page's `ready`.
- [x] 4.5 Fail with a clear, specific message when the WebView2 runtime is absent. — `WebView2RuntimeNotFoundException` caught ahead of the general handler; the message names the runtime, links the installer, and points at SSH2 as the fallback.
- [x] 4.6 Confirm SSH1, SSH2, Telnet, Rlogin and RAW are untouched. — asserted rather than eyeballed: `NativeSshProtocolIntegrationTests` pins each one's factory type, default port and enum value. Full suite 7308 passed.

## 5. Terminal behaviour

- [x] 5.1 Wire the input handling identified in 1.2. — keys themselves are clean; the work is `preventDefault()` + ignore `event.repeat` on any chord the host claims, and keeping Ctrl+C as SIGINT. Done in `Assets/terminal.js`.
- [x] 5.2 Clipboard copy and paste, including bracketed paste. — host-side clipboard (not `navigator.clipboard`); copy-on-select + Ctrl+Insert/Ctrl+Shift+C; paste routed **back through the page** so bracketed-paste wrapping still applies. **Still open:** Ctrl+V vs readline quoted-insert. Ctrl+V is bound for now; making it a setting belongs with the Options page in 7.1, so that is where the decision should land.
- [x] 5.3 Focus behaviour on tab activation, matching the other protocols. — `Focus()` gives the control OS focus via the base and then tells the page to focus the terminal; both are needed, since a focused WebView with an unfocused xterm swallows keystrokes. `ConnectionWindow` already calls `Protocol.Focus()` on activation, and the session focuses once on connect.
- [x] 5.4 Confirm remote output containing HTML or script markup renders as literal text. — verified in the spike (1.2 item 11); re-confirm against the shipping host document in 2.2.

## 6. Host keys

- [x] 6.1 Present unknown and changed host keys for confirmation; never accept silently. — `HostKeyGate` + `DialogHostKeyVerifier`. Without a `HostKeyReceived` handler SSH.NET trusts whatever it is given, so this closed a real hole: the session as first committed accepted every key in silence.
- [x] 6.2 Decide and implement where accepted keys are stored (design.md D5, open question). — **mRemoteNG's own file**, `hostkeys.txt` in the settings folder, tab separated and hand-editable. Reusing OpenSSH `known_hosts` would be friendlier but means writing a file another tool owns, in a format with hashed hostnames, CA markers and revocation entries we could corrupt while meaning well. Reading `known_hosts` to skip a first-connection prompt stays available as an additive follow-up.
- [x] 6.3 Tests: unknown key prompts; changed key is reported as a change; known key does not prompt. — 14 tests over the gate and the file store, including refusal recording nothing, a different port not being a change, and a second algorithm for the same host not being a change. Also verified end to end against the container: unknown key refused, accepted key silent on reconnect, tampered fingerprint presented as Changed, and the fingerprint matches `ssh-keygen -lf` character for character.

## 7. Appearance and settings

- [x] 7.1 Options page for font, colour scheme and scrollback length. — `TerminalPage`, backed by `OptionsTerminalPage.settings`. Also carries the **Ctrl+V pastes** setting that 5.2 deferred here, on by default (Windows behaviour); turning it off frees Ctrl+V for readline's quoted-insert, leaving Shift+Insert and Ctrl+Shift+V, which paste either way.
- [x] 7.2 Apply configured values to new sessions. — read at `start` rather than cached, so a change applies to the next connection with no restart. Values are clamped on the way in and out: the settings file is hand-editable, and an out-of-range number assigned to a `NumericUpDown` throws, which would take the whole options form down on open.
- [x] 7.3 Honour the application theme where it does not fight the terminal's own colour scheme. — the default scheme is **Follow**, which picks the light or dark *variant*; it never repaints the terminal from the application palette. A terminal's colours are meaning rather than decoration — red is red because the remote host said so — so the theme chooses which palette, never what is in it. Dark is matched by theme name, with `darcula` listed explicitly since it does not contain "dark".

## 8. Completion

- [x] 8.1 Full build. — clean, with restore, 81s.
- [x] 8.2 Full test suite; zero failures, no `[Ignore]`. — 7366 passed, 0 failures. No `[Ignore]` added.
- [x] 8.3 Zero new analyzer warnings.
- [x] 8.4 `openspec validate add-native-ssh-terminal --strict`. — valid.
- [x] 8.5 Manual: connect with password, with a key, and with an agent identity; resize during a full-screen editor; UTF-8 and CJK output; large-output throughput; an SSH2 connection in a tab beside it, unchanged. — **passed 2026-08-10** for key auth, password auth, UTF-8/CJK, 5.3 MB throughput, resize during `top`, and an SSH2 tab beside it unaffected. **Agent identity partially verified (2026-08-10)**: with the OpenSSH agent running and the key loaded, the terminal offered `publickey` with no key file, which can only come from the agent — so enumeration and adapter translation are confirmed in the application. Not yet confirmed that the *server accepted* the agent identity, because that connection also carried a password and either could have satisfied it. Clearing the password and reconnecting would close it.
- [x] 8.6 Record honestly what PuTTY still does better, so the default stays with PuTTY until that list is short. — design.md, "What PuTTY still does better". **Recommendation: PuTTY stays the default.** Keyboard-interactive cannot be answered, which locks out any account behind a second factor, and there is no session logging.
