// Spike bridge: WebView2 web-message channel <-> xterm.js.
// Deliberately no host objects (design.md D2) — this channel carries opaque text only.
(function () {
    'use strict';

    var host = window.chrome && window.chrome.webview;
    if (!host) return;

    var term = new Terminal({
        allowProposedApi: true,
        convertEol: false,
        cursorBlink: true,
        scrollback: 5000,
        fontFamily: 'Cascadia Mono, Consolas, monospace',
        fontSize: 14,
        theme: { background: '#101216', foreground: '#d8dee9' }
    });

    var fit = new FitAddon.FitAddon();
    term.loadAddon(fit);
    term.open(document.getElementById('term'));
    fit.fit();

    function post(msg) { host.postMessage(msg); }

    // User input -> host -> ShellStream.
    term.onData(function (d) { post({ t: 'i', d: d }); });

    // Report size changes so the host can drive ChangeWindowSize.
    term.onResize(function (e) { post({ t: 'r', cols: e.cols, rows: e.rows }); });

    window.addEventListener('resize', function () { fit.fit(); });

    host.addEventListener('message', function (ev) {
        var m = ev.data;
        if (m.t === 'd') {
            // The ack fires once xterm has parsed AND rendered this chunk. That is the number
            // that matters: bytes accepted by write() are not bytes the user can see.
            term.write(m.d, function () {
                post({ t: 'a', s: m.s, ts: performance.now() });
            });
        } else if (m.t === 'fit') {
            fit.fit();
            post({ t: 'r', cols: term.cols, rows: term.rows });
        } else if (m.t === 'focus') {
            term.focus();
        } else if (m.t === 'probe') {
            // 1.3 / security check: does remote output reach the DOM as markup?
            post({ t: 'probe', html: document.body.innerHTML.indexOf('<img') >= 0 ? 'markup-leaked' : 'clean' });
        }
    });

    post({ t: 'ready', cols: term.cols, rows: term.rows, ts: performance.now() });
})();
