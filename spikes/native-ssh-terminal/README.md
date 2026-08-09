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

## Task 1.2 — still needs a human

Throughput can be measured unattended. Keyboard fidelity cannot. Run:

```powershell
./NativeTerminalSpike.exe --interactive --key ../../../../fixture/.keys/spike_key
```

A live shell opens. Work through this list and record what fails — the failures become the
acceptance criteria for tasks 5.1–5.3:

- [ ] F1–F12 (try `showkey -a`, or `vim` then `:map <F5>`)
- [ ] Alt combinations (`Alt+B` / `Alt+F` word movement in a bash line)
- [ ] Ctrl-C interrupts a running `sleep 60`
- [ ] Ctrl-Z suspends, `fg` resumes
- [ ] Bracketed paste — paste multi-line text into `cat`, confirm it is not executed line by line
- [ ] Clipboard copy from the terminal (selection, Ctrl+Insert, right-click)
- [ ] Clipboard paste into the terminal (Ctrl+V, Shift+Insert, middle-click)
- [ ] One IME (any non-Latin input method); type into `cat` and confirm composition works
- [ ] Resize the window during `vim` or `top` and confirm the remote redraws at the new size
- [ ] Remote output containing `<img src=x onerror=alert(1)>` renders as literal text

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
