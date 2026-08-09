// Spike bridge: WebView2 web-message channel <-> xterm.js.
// Deliberately no host objects (design.md D2) — this channel carries opaque text only.
(function () {
    'use strict';

    var host = window.chrome && window.chrome.webview;
    if (!host) return;

    // A background and a foreground are not a theme. Anything the remote prints with an SGR colour
    // uses the 16-colour palette, and xterm's default ANSI black is near enough to a dark
    // background to be unreadable — which is most of a coloured shell prompt and all of `ls`.
    // Both palettes below lift colour 0 clear of their background for that reason.
    var THEMES = {
        dark: {
            background: '#1e1e1e', foreground: '#e6e6e6',
            cursor: '#ffffff', cursorAccent: '#1e1e1e', selectionBackground: '#264f78',
            black: '#6a6a6a', red: '#f14c4c', green: '#23d18b', yellow: '#f5f543',
            blue: '#4fa6ee', magenta: '#d670d6', cyan: '#29b8db', white: '#e5e5e5',
            brightBlack: '#8c8c8c', brightRed: '#ff6b6b', brightGreen: '#4ae5a4',
            brightYellow: '#ffff8c', brightBlue: '#7ac0ff', brightMagenta: '#ee9bee',
            brightCyan: '#5fd7f0', brightWhite: '#ffffff'
        },
        light: {
            background: '#ffffff', foreground: '#1f1f1f',
            cursor: '#000000', cursorAccent: '#ffffff', selectionBackground: '#add6ff',
            black: '#3a3a3a', red: '#cd3131', green: '#107c10', yellow: '#795e26',
            blue: '#0451a5', magenta: '#bc05bc', cyan: '#0598bc', white: '#555555',
            brightBlack: '#666666', brightRed: '#cd3131', brightGreen: '#14ce14',
            brightYellow: '#b5ba00', brightBlue: '#0451a5', brightMagenta: '#bc05bc',
            brightCyan: '#0598bc', brightWhite: '#000000'
        }
    };

    var term = new Terminal({
        allowProposedApi: true,
        convertEol: false,
        cursorBlink: true,
        scrollback: 5000,
        fontFamily: 'Cascadia Mono, Consolas, monospace',
        fontSize: 14,
        theme: THEMES.dark
    });

    var fit = new FitAddon.FitAddon();
    term.loadAddon(fit);
    term.open(document.getElementById('term'));
    fit.fit();

    function post(msg) { host.postMessage(msg); }

    function applyTheme(name) {
        var theme = THEMES[name] || THEMES.dark;
        term.options.theme = theme;
        document.body.style.background = theme.background;
        reportColours(name, theme);
    }

    // If this still reads wrong, the host log says what actually got painted rather than what was
    // asked for — which is the difference between fixing it and guessing again.
    function reportColours(name, theme) {
        var screen = document.querySelector('.xterm-screen') || document.getElementById('term');
        var computed = window.getComputedStyle(screen);
        var xterm = document.querySelector('.xterm');
        var row = document.querySelector('.xterm-rows > div');
        post({
            t: 'diag',
            theme: name,
            wantFg: theme.foreground,
            wantBg: theme.background,
            gotColor: computed.color,
            gotBackground: computed.backgroundColor,
            xtermColor: xterm ? window.getComputedStyle(xterm).color : 'no .xterm',
            xtermBackground: xterm ? window.getComputedStyle(xterm).backgroundColor : 'no .xterm',
            canvases: document.querySelectorAll('canvas').length,
            styleTags: document.querySelectorAll('style').length,
            rowSample: row ? row.innerHTML.slice(0, 160) : 'no rows',
            bodyBackground: window.getComputedStyle(document.body).backgroundColor,
            forcedColors: window.matchMedia('(forced-colors: active)').matches,
            cols: term.cols,
            rows: term.rows
        });
    }

    // Decisive on whether the CSP is silently dropping anything the renderer needs.
    document.addEventListener('securitypolicyviolation', function (e) {
        post({ t: 'csp', directive: e.violatedDirective, blocked: e.blockedURI, sample: (e.sample || '').slice(0, 80) });
    });

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
        } else if (m.t === 'theme') {
            applyTheme(m.name);
        } else if (m.t === 'fit') {
            fit.fit();
            post({ t: 'r', cols: term.cols, rows: term.rows });
        } else if (m.t === 'focus') {
            term.focus();
        }
    });

    applyTheme('dark');
    post({ t: 'ready', cols: term.cols, rows: term.rows, ts: performance.now() });
})();
