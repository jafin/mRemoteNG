// Bridge between xterm.js and the mRemoteNG host, over the WebView2 web-message channel.
//
// The channel carries opaque text in both directions and nothing else. Host objects are
// deliberately not exposed to page script (see the change's design.md D2): this page renders
// output from a remote machine, and widening what it can reach into the host would make that
// output far more interesting to an attacker than it needs to be.
(function () {
    'use strict';

    var host = window.chrome && window.chrome.webview;
    if (!host) return;

    // A background and a foreground are not a theme. Anything the remote prints with an SGR colour
    // uses the 16-colour palette, and xterm's default ANSI black is close enough to a dark
    // background to be unreadable — which is most of a coloured shell prompt and all of `ls`.
    // Both palettes lift colour 0 clear of their own background for that reason.
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

    var term = null;
    var fit = null;
    var ctrlVPastes = true;

    function post(msg) { host.postMessage(msg); }

    function start(options) {
        ctrlVPastes = options.ctrlVPastes !== false;

        term = new Terminal({
            allowProposedApi: true,
            convertEol: false,
            cursorBlink: true,
            scrollback: options.scrollback || 5000,
            fontFamily: options.fontFamily || 'Cascadia Mono, Consolas, monospace',
            fontSize: options.fontSize || 14,
            theme: THEMES[options.theme] || THEMES.dark
        });

        fit = new FitAddon.FitAddon();
        term.loadAddon(fit);
        term.open(document.getElementById('terminal'));
        fit.fit();

        document.body.style.background = (THEMES[options.theme] || THEMES.dark).background;

        term.onData(function (d) { post({ t: 'input', d: d }); });
        term.onResize(function (e) { post({ t: 'resize', cols: e.cols, rows: e.rows }); });

        // Copy on select, the way PuTTY does — that is the behaviour these users have today.
        term.onSelectionChange(copySelection);

        term.attachCustomKeyEventHandler(keyHandler);

        term.element.addEventListener('mousedown', function (e) {
            if (e.button === 1) {           // middle click pastes
                e.preventDefault();
                post({ t: 'wantpaste' });
            }
        });

        window.addEventListener('resize', function () { if (fit) fit.fit(); });

        post({ t: 'ready', cols: term.cols, rows: term.rows });
    }

    function copySelection() {
        // The clipboard is the host's. navigator.clipboard is gesture- and permission-gated inside
        // WebView2, and the application already owns the Windows clipboard, so the page only ever
        // reports what is selected or asks for what is held.
        if (term && term.hasSelection())
            post({ t: 'copy', d: term.getSelection() });
    }

    // Returning false stops *xterm* handling a key. It does not stop the browser's own default
    // action, and it does not stop key auto-repeat — either will paste a second time on top of
    // ours, intermittently. A duplicated paste of a command line is a command run twice.
    function claim(e) {
        e.preventDefault();
        e.stopPropagation();
        return false;
    }

    function keyHandler(e) {
        if (e.type !== 'keydown') return true;
        if (e.repeat) return true;

        var isC = e.key === 'c' || e.key === 'C';
        var isV = e.key === 'v' || e.key === 'V';

        // Plain Ctrl+C is deliberately untouched: it must keep sending SIGINT. That is exactly why
        // copy lives on Ctrl+Insert.
        if ((e.ctrlKey && e.key === 'Insert') || (e.ctrlKey && e.shiftKey && isC)) {
            copySelection();
            return claim(e);
        }

        // Shift+Insert and Ctrl+Shift+V are the terminal conventions and always paste. Plain Ctrl+V
        // is the Windows habit and is on by default, but it is optional: Ctrl+V is readline's
        // quoted-insert, the only way to type a literal control character, so a user who needs that
        // can turn it off and keep the two conventional bindings.
        if ((e.shiftKey && e.key === 'Insert') || (e.ctrlKey && e.shiftKey && isV) ||
            (ctrlVPastes && e.ctrlKey && !e.shiftKey && !e.altKey && isV)) {
            post({ t: 'wantpaste' });
            return claim(e);
        }

        return true;
    }

    host.addEventListener('message', function (ev) {
        var m = ev.data;

        if (m.t === 'start') {
            start(m);
        } else if (!term) {
            return;                          // nothing else is meaningful before start
        } else if (m.t === 'output') {
            term.write(m.d);
        } else if (m.t === 'paste') {
            // Through xterm rather than straight to the shell, so bracketed-paste wrapping is
            // applied. Writing it directly would silently drop that protection.
            term.paste(m.d);
        } else if (m.t === 'focus') {
            term.focus();
        } else if (m.t === 'fit') {
            fit.fit();
            post({ t: 'resize', cols: term.cols, rows: term.rows });
        } else if (m.t === 'theme') {
            var theme = THEMES[m.name] || THEMES.dark;
            term.options.theme = theme;
            document.body.style.background = theme.background;
        }
    });

    post({ t: 'loaded' });
})();
