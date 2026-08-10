# Third-party assets — native SSH terminal

Vendored rather than fetched. The terminal renders output from a remote host inside a browser
engine, so nothing it loads may come off the network at runtime (see the change's design.md D3).

Fetched from the npm registry on 2026-08-09.

| Package | Version | Licence | `dist.shasum` |
|---|---|---|---|
| [`@xterm/xterm`](https://www.npmjs.com/package/@xterm/xterm) | 6.0.0 | MIT | `93637b0f2ee3a70718b5746a27c9c506af16745b` |
| [`@xterm/addon-fit`](https://www.npmjs.com/package/@xterm/addon-fit) | 0.11.0 | MIT | `ba4778b69fcc9044a060c2176bbe077657d7b37e` |

Files taken: `lib/xterm.js`, `css/xterm.css`, `lib/addon-fit.js`, and the upstream `LICENSE`
(shipped here as `xterm-LICENSE.txt`). Nothing was modified.

MIT is compatible with mRemoteNG's licence and requires the copyright notice and permission notice
to be distributed with the software — which is why `xterm-LICENSE.txt` sits in this folder and is
installed alongside the assets rather than being left in the repository only.

## Updating

Both packages are pinned copies with no package manager watching them, so a security advisory will
not surface on its own. When updating:

1. Replace the files above and update the version, licence and shasum in this table.
2. Re-run the spike benchmark (`spikes/native-ssh-terminal/`) — it asserts bracketed paste and the
   UTF-8 decoder, both of which are xterm behaviours this application depends on.
3. Re-check the CSP. The reason `style-src` needs `'unsafe-inline'` is that xterm's DOM renderer
   injects `<style>` elements; if a future version stops doing that, the grant can be dropped.
