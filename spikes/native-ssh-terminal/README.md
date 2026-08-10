# Native SSH terminal — spike

Throwaway harness for openspec change `add-native-ssh-terminal`, tasks **1.1**, **1.2** and **1.3**.
Nothing here ships. It exists to answer one question before 31 tasks get built on the answer:
**does `ShellStream` → WebView2 → xterm.js keep up?**

It is deliberately not in `mRemoteNG.sln`, so `build.ps1`, CI and the test run are unaffected.

## Running it

```powershell
# 1. Stand up a real sshd in a container, with benchmark payloads and a keypair.
pwsh -File fixture/up.ps1

# 2. Build.
dotnet build NativeTerminalSpike/NativeTerminalSpike.csproj -c Release

# 3. Measure (unattended, writes JSON and exits).
cd NativeTerminalSpike/bin/Release/net10.0-windows
./NativeTerminalSpike.exe --benchmark --key ../../../../fixture/.keys/spike_key --results results.json

# 4. Tear down.
pwsh -File fixture/down.ps1
```

Add `--delivery inline` to measure the `NavigateToString` route instead of the default virtual-host
mapping. Add `--password spikepass` instead of `--key` to exercise password auth.

## What it measures

Bytes are timed at **xterm's `write()` completion callback**, not at the point they are handed to the
page. Bytes accepted by `write()` are not bytes a user can see, and the difference is the whole
question.

- `renderMBps` — end-to-end, command issued to last byte on screen
- `lagMs` — per chunk, posted-to-page → rendered. The p95 is the backlog under load.
- `renderGapMs` — between successive renders. **The max is the worst visible hitch.**
- `replacementChars` — U+FFFD count. Non-zero means the UTF-8 decoder lost a split sequence.

## Results (2026-08-09, loopback, 98x31)

| Scenario | Bytes | Wall | Rendered | p95 lag | Max hitch | U+FFFD |
|---|---|---|---|---|---|---|
| `cat` 5.3 MB ASCII | 5,395,102 | 205 ms | 25.1 MB/s | 180 ms | 185 ms | 0 |
| `cat` 2.2 MB UTF-8/CJK | 2,260,099 | 136 ms | 15.9 MB/s | 86 ms | 86 ms | 0 |
| 200 full-screen redraws | 913,561 | 471 ms | 2.4 ms/repaint | 1.1 ms | 5 ms | 0 |

Startup: WebView2 environment 589 ms, page ready 909 ms, SSH connect + shell 282 ms.

Delivery comparison (same run, `--delivery inline`): 29.2 MB/s / 19.4 MB/s / 380 ms, page ready
894 ms. Performance is a wash; see design.md S1.3 for why the CSP argument decides it.

**Read the caveats in design.md S1.1 before quoting these numbers.** Loopback is a ceiling, not a
prediction.

## Task 1.2 — complete (2026-08-09): all 11 checks pass

Throughput can be measured unattended. Keyboard fidelity cannot. Run:

```powershell
./NativeTerminalSpike.exe --interactive --key ../../../../fixture/.keys/spike_key
```

A live shell opens. The checklist below was worked through on 2026-08-09 and every item passes.
It is kept because it is the regression list for sections 4 and 5 — the two defects it found
(no clipboard at all, and double paste) live in the binding layer, which is the code those
sections write. See design.md S1.2.

- [x] F1–F12 — run `showkey -a` and press each; it prints the bytes as they arrive. (`cat -v`
      also works but is line-buffered, so nothing shows until you press Enter.)
- [x] Alt combinations — in `showkey -a`, `Alt+B` / `Alt+F` / `Alt+D` should give `^[b` `^[f` `^[d`
- [x] Ctrl-C — run `sleep 60` **and press Enter first**, so a job is actually running, then Ctrl-C.
      The prompt should return at once.
- [x] Ctrl-Z — again **press Enter** to start `sleep 60`, then Ctrl-Z for `[1]+ Stopped`, then
      `fg` to resume. Ctrl-Z at an idle prompt correctly does nothing, so testing it there proves
      nothing. Test these two outside `showkey -a`: in raw mode it captures Ctrl-C/Ctrl-Z as bytes
      instead of letting them become signals.
- [x] Bracketed paste — **covered by `--benchmark`**, which drives xterm's own paste path with mode
      2004 enabled and asserts the `^[[200~` / `^[[201~` wrapping. Do *not* test this by pasting
      into a bare `cat -v`: bash clears mode 2004 before running any command, so an unwrapped paste
      there is correct and proves nothing. To check it by hand, paste a multi-line block at the
      **bash prompt** and confirm the lines sit in the input buffer instead of each executing as
      its newline arrives.
- [x] Clipboard copy — select with the mouse (copies on select, PuTTY-style), or Ctrl+Insert /
      Ctrl+Shift+C. Paste into Notepad to confirm it left the app.
- [x] Clipboard paste — Shift+Insert, Ctrl+Shift+V, Ctrl+V, middle-click. Note which work.
- [x] One IME (any non-Latin input method); type into `cat` and confirm composition works
- [x] Resize the window during `vim` or `top` and confirm the remote redraws at the new size
- [x] Remote output containing `<img src=x onerror=alert(1)>` renders as literal text

## Clipboard: a finding, not just an implementation

Nothing about the clipboard is free. xterm does not copy on selection, and inside WebView2 only
**Shift+Insert** pastes without help — Ctrl+V and middle-click do nothing at all (measured). So the
binding layer owns all of it, which is exactly the risk design.md predicted.

It is done **on the host**, not through `navigator.clipboard`. The web clipboard API is gesture- and
permission-gated inside WebView2, and mRemoteNG is a WinForms app that already owns the Windows
clipboard; routing through the page would add a permission prompt and buy nothing. The page only
reports the selected text, or asks for the current clipboard contents.

Paste is sent back **through the page** rather than written straight to the shell, so xterm applies
bracketed-paste wrapping. Writing it directly to `ShellStream` would silently lose that protection.

**Binding a paste key is not enough — you must also suppress the browser.** Returning `false` from
`attachCustomKeyEventHandler` stops *xterm* handling the key, but not the browser's default action
and not key auto-repeat. Either will paste a second time on top of yours. Observed as an
intermittent double paste on Ctrl+V, which for a pasted command line means running it twice. Fixed
by calling `preventDefault()`/`stopPropagation()` and ignoring `event.repeat`. Shift+Insert did not
show it, which is what made it look like a Ctrl+V-specific quirk rather than a general one.

One decision for task 5.2 rather than for a spike: **Ctrl+V is readline's quoted-insert (`^V`)**.
Binding it to paste removes the only way to type a literal control character. PuTTY declines that
trade and uses Shift+Insert; a Windows-native app probably should not. The spike binds it so the
behaviour can be seen, and the trade recorded — it likely wants to be configurable.

## Third-party assets

Vendored under `NativeTerminalSpike/assets/`, fetched from the npm registry on 2026-08-09:

| Package | Version | Licence | dist.shasum |
|---|---|---|---|
| `@xterm/xterm` | 6.0.0 | MIT | `93637b0f2ee3a70718b5746a27c9c506af16745b` |
| `@xterm/addon-fit` | 0.11.0 | MIT | `ba4778b69fcc9044a060c2176bbe077657d7b37e` |

`xterm-LICENSE.txt` is the upstream MIT licence text. Task 2.1 repeats this properly for the
shipping copy, including wherever the app lists third-party licences.

## Known spike-only shortcuts

These are wrong on purpose and must not be carried into the real implementation:

- **Host keys are auto-trusted** (`ShellSession` constructor). The spec requires confirmation.
- No host-key storage, no reconnect, no theming, no settings.
- `--password` is passed on the command line.
