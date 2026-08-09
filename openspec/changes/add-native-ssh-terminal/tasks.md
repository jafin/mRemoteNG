# Tasks

## 1. Spikes (do these first — they can invalidate the approach)

- [ ] 1.1 **Throughput.** Prototype `ShellStream` → WebView2 → `xterm.js` and measure `cat` of a large file and a full-screen editor redraw against a real host. Record throughput and perceived latency. If it stutters, decide between batching, a different channel, or stopping. Gates everything below.
- [ ] 1.2 **Input fidelity.** In the same prototype, check function keys, Alt combinations, Ctrl-C/Ctrl-Z, bracketed paste, clipboard copy/paste, and one IME. Record what does not work; these are the acceptance criteria for section 5.
- [ ] 1.3 **Asset delivery.** Try `SetVirtualHostNameToFolderMapping` and `NavigateToString`; pick one on packaging and CSP grounds and record why in design.md D3.
- [ ] 1.4 Confirm the WebView2 runtime detection story: what `CoreWebView2Environment.CreateAsync` does when the runtime is absent, and what the user should be told.

## 2. Vendored front end

- [ ] 2.1 Vendor `xterm.js` with its version and provenance recorded; confirm the MIT licence is compatible and add it wherever third-party licences are listed.
- [ ] 2.2 Add the host document and the delivery mechanism chosen in 1.3, with a CSP that permits no network origins.
- [ ] 2.3 Ensure assets reach the build output and the installer.

## 3. Transport

- [ ] 3.1 Add `mRemoteNG/Connection/Protocol/SSH/Native/` with a session type wrapping `SshClient` + `CreateShellStream`.
- [ ] 3.2 Authenticate via `SshCredentialResolver` + `SshNetAuthAdapter`; replay resolution diagnostics on the connection's message channel.
- [ ] 3.3 Read loop with a **stateful** UTF-8 decoder retained across reads, so a multi-byte sequence split across two buffers is not corrupted.
- [ ] 3.4 Write path from the page to `ShellStream`.
- [ ] 3.5 Report remote shell exit and transport failure as a disconnect.
- [ ] 3.6 Tests: decoder handles a split multi-byte sequence; exit reported as disconnect; resize while disconnected is a no-op; credential diagnostics replayed.

## 4. Protocol integration

- [ ] 4.1 Add the `ProtocolType` value and its localized description.
- [ ] 4.2 Add the `ProtocolFactory.CreateProtocol` case.
- [ ] 4.3 Implement the `ProtocolBase` subclass hosting the WebView2 control as `Control`.
- [ ] 4.4 Propagate control resize to `ShellStream.ChangeWindowSize`, and send the initial size when the session starts.
- [ ] 4.5 Fail with a clear, specific message when the WebView2 runtime is absent.
- [ ] 4.6 Confirm SSH1, SSH2, Telnet, Rlogin and RAW are untouched.

## 5. Terminal behaviour

- [ ] 5.1 Wire the input handling identified in 1.2.
- [ ] 5.2 Clipboard copy and paste, including bracketed paste.
- [ ] 5.3 Focus behaviour on tab activation, matching the other protocols.
- [ ] 5.4 Confirm remote output containing HTML or script markup renders as literal text.

## 6. Host keys

- [ ] 6.1 Present unknown and changed host keys for confirmation; never accept silently.
- [ ] 6.2 Decide and implement where accepted keys are stored (design.md D5, open question).
- [ ] 6.3 Tests: unknown key prompts; changed key is reported as a change; known key does not prompt.

## 7. Appearance and settings

- [ ] 7.1 Options page for font, colour scheme and scrollback length.
- [ ] 7.2 Apply configured values to new sessions.
- [ ] 7.3 Honour the application theme where it does not fight the terminal's own colour scheme.

## 8. Completion

- [ ] 8.1 Full build.
- [ ] 8.2 Full test suite; zero failures, no `[Ignore]`.
- [ ] 8.3 Zero new analyzer warnings.
- [ ] 8.4 `openspec validate add-native-ssh-terminal --strict`.
- [ ] 8.5 Manual: connect with password, with a key, and with an agent identity; resize during a full-screen editor; UTF-8 and CJK output; large-output throughput; an SSH2 connection in a tab beside it, unchanged.
- [ ] 8.6 Record honestly what PuTTY still does better, so the default stays with PuTTY until that list is short.
