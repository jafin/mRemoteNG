using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using mRemoteNG.App;
using mRemoteNG.Messages;
using mRemoteNG.Resources.Language;
using mRemoteNG.Connection.Protocol.SSH.Native.HostKeys;
using mRemoteNG.Security.Ssh;
using mRemoteNG.Themes;

namespace mRemoteNG.Connection.Protocol.SSH.Native;

/// <summary>
/// SSH in a tab that mRemoteNG owns end to end: SSH.NET for the transport, xterm.js in WebView2 for
/// the emulator, in place of a reparented <c>putty.exe</c> window.
/// </summary>
/// <remarks>
/// Runs alongside <see cref="ProtocolSSH2"/> rather than replacing it, so the two can be compared on
/// the same connection list until this one has earned the default.
/// </remarks>
[SupportedOSPlatform("windows")]
public class ProtocolNativeSsh : ProtocolBase
{
    /// <summary>The host name the assets are served under. Never resolved; nothing leaves the app.</summary>
    private const string VirtualHostName = "terminal.mremoteng.invalid";

    private readonly ConnectionInfo _connectionInfo;
    private readonly WebView2 _webView;

    private INativeSshTerminalSession? _session;
    private CoreWebView2Environment? _environment;
    private string? _userDataFolder;
    private bool _pageReady;
    private bool _connectRequested;
    private uint _columns = 80;
    private uint _rows = 24;

    public ProtocolNativeSsh(ConnectionInfo connectionInfo)
    {
        ArgumentNullException.ThrowIfNull(connectionInfo);

        _connectionInfo = connectionInfo;
        _webView = new WebView2 { Dock = DockStyle.Fill };
        Control = _webView;
    }

    public static class Defaults
    {
        public const int Port = 22;
    }

    public override bool Initialize()
    {
        if (!base.Initialize())
            return false;

        // Kicked off here rather than in Connect so the browser engine and the SSH handshake
        // overlap; the engine is the slower of the two by a wide margin.
        _ = InitialiseWebViewAsync();
        return true;
    }

    public override bool Connect()
    {
        _connectRequested = true;

        // The page may already be up, or may still be loading. Whichever finishes last starts the
        // session, so this is safe in either order.
        if (_pageReady)
            _ = StartSessionAsync();

        return true;
    }

    private async Task InitialiseWebViewAsync()
    {
        try
        {
            // Per-instance, like the HTTP protocol: two terminals must not share a profile.
            _userDataFolder = Path.Combine(Path.GetTempPath(), "mRemoteNG", "Terminal",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_userDataFolder);

            _environment = await CoreWebView2Environment.CreateAsync(null, _userDataFolder);
            await _webView.EnsureCoreWebView2Async(_environment);
        }
        catch (WebView2RuntimeNotFoundException)
        {
            // Named specifically, ahead of the general handler. A missing browser runtime failing
            // an SSH session must not read as an authentication or network problem — the user
            // would have no way to guess what to fix. See design.md S1.4.
            Runtime.MessageCollector.AddMessage(MessageClass.ErrorMsg,
                Language.SshNativeWebView2Missing, true);
            Event_ErrorOccured(this, Language.SshNativeWebView2Missing, null);
            Event_Disconnected(this, Language.SshNativeWebView2Missing, null);
            return;
        }
        catch (Exception ex)
        {
            Runtime.MessageCollector.AddExceptionStackTrace(Language.SshNativeTerminalHostFailed, ex);
            Event_Disconnected(this, ex.Message, null);
            return;
        }

        CoreWebView2 core = _webView.CoreWebView2;
        CoreWebView2Settings settings = core.Settings;
        settings.AreDevToolsEnabled = false;
        settings.AreDefaultContextMenusEnabled = false;
        settings.IsStatusBarEnabled = false;
        settings.AreHostObjectsAllowed = false;   // design.md D2: the message channel only
        settings.IsWebMessageEnabled = true;
        settings.IsPasswordAutosaveEnabled = false;
        settings.IsGeneralAutofillEnabled = false;

        core.WebMessageReceived += OnWebMessageReceived;

        // Deny grants the page nothing beyond the mapped folder; the assets are served from disk
        // under a real origin so the CSP can pin script to 'self'. See design.md S1.3.
        core.SetVirtualHostNameToFolderMapping(
            VirtualHostName,
            Path.Combine(AppContext.BaseDirectory, "TerminalAssets"),
            CoreWebView2HostResourceAccessKind.Deny);

        core.Navigate($"https://{VirtualHostName}/index.html");
    }

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        JsonNode? node;
        try
        {
            node = JsonNode.Parse(e.WebMessageAsJson);
        }
        catch (System.Text.Json.JsonException)
        {
            return;
        }

        if (node is null)
            return;

        switch (node["t"]?.GetValue<string>())
        {
            case "loaded":
                Post(BuildStartMessage());
                break;

            case "ready":
                _columns = ReadDimension(node["cols"], _columns);
                _rows = ReadDimension(node["rows"], _rows);
                _pageReady = true;
                if (_connectRequested)
                    _ = StartSessionAsync();
                break;

            case "input":
                _session?.Send(node["d"]?.GetValue<string>() ?? string.Empty);
                break;

            case "resize":
                _columns = ReadDimension(node["cols"], _columns);
                _rows = ReadDimension(node["rows"], _rows);
                // A no-op before the shell exists and after it ends, which is most of the time the
                // control spends being laid out.
                _session?.Resize(_columns, _rows);
                break;

            case "copy":
                CopyToClipboard(node["d"]?.GetValue<string>());
                break;

            case "wantpaste":
                PasteFromClipboard();
                break;
        }
    }

    /// <summary>
    /// The configured appearance and input options, applied as the terminal starts. Read here
    /// rather than cached so a change takes effect on the next connection without a restart.
    /// </summary>
    private static JsonObject BuildStartMessage()
    {
        Properties.OptionsTerminalPage settings = Properties.OptionsTerminalPage.Default;

        return new JsonObject
        {
            ["t"] = "start",
            ["theme"] = ResolveColorScheme(settings.TerminalColorScheme),
            ["fontFamily"] = string.IsNullOrWhiteSpace(settings.TerminalFontFamily)
                ? "Cascadia Mono, Consolas, monospace"
                : settings.TerminalFontFamily,
            ["fontSize"] = Math.Clamp(settings.TerminalFontSize, 6, 32),
            ["scrollback"] = Math.Clamp(settings.TerminalScrollback, 0, 200_000),
            ["ctrlVPastes"] = settings.TerminalCtrlVPastes
        };
    }

    /// <summary>
    /// Resolves the configured scheme, including "Follow".
    /// </summary>
    /// <remarks>
    /// Following the application theme picks the light or dark <i>variant</i>; it does not repaint
    /// the terminal from the application palette. A terminal's colours are meaning, not decoration
    /// — red is red because the remote host said so — so the theme chooses which palette, never
    /// what is in it. Task 7.3.
    /// </remarks>
    private static string ResolveColorScheme(string? configured)
    {
        if (string.Equals(configured, "Dark", StringComparison.OrdinalIgnoreCase))
            return "dark";

        if (string.Equals(configured, "Light", StringComparison.OrdinalIgnoreCase))
            return "light";

        string themeName = ThemeManager.getInstance().ActiveTheme?.Name ?? string.Empty;

        // Matched by name because the theme files carry no light/dark flag. "darcula" is listed
        // explicitly because it is dark and does not contain "dark".
        bool dark = themeName.Contains("dark", StringComparison.OrdinalIgnoreCase)
                    || themeName.Contains("darcula", StringComparison.OrdinalIgnoreCase);

        return dark ? "dark" : "light";
    }

    private static uint ReadDimension(JsonNode? value, uint fallback)
    {
        int parsed = value?.GetValue<int>() ?? 0;
        return parsed > 0 ? (uint)parsed : fallback;
    }

    private async Task StartSessionAsync()
    {
        if (_session is not null)
            return;

        try
        {
            // The dialog marshals to the terminal control, so the prompt appears over the tab the
            // user is actually looking at rather than behind it.
            HostKeyGate hostKeys = new(new FileHostKeyStore(), new DialogHostKeyVerifier(_webView));

            NativeSshTerminalSession session =
                NativeSshTerminalSession.ForConnection(_connectionInfo, hostKeys: hostKeys);
            _session = session;

            ReplayCredentialDiagnostics(session.Diagnostics);

            session.OutputReceived += OnOutputReceived;
            session.Disconnected += OnSessionDisconnected;

            Event_Connecting(this);
            await session.ConnectAsync(_columns, _rows).ConfigureAwait(true);

            Event_Connected(this);
            Post(new JsonObject { ["t"] = "focus" });
        }
        catch (Exception ex)
        {
            Runtime.MessageCollector.AddExceptionStackTrace(Language.SshNativeConnectFailed, ex);
            Event_Disconnected(this, DescribeFailure(ex.Message, _session?.Diagnostics), null);
            Close();
        }
    }

    /// <summary>
    /// Combines the transport's failure with anything credential resolution already knew was wrong.
    /// </summary>
    /// <remarks>
    /// "Permission denied (keyboard-interactive)" is what a server says when no key was offered, and
    /// it says nothing about why. The reason is usually already known — a configured key file that
    /// was not found, or one that is passphrase-encrypted and could not be loaded — but it is
    /// recorded on the message channel, which is not where someone looks when a tab fails to open.
    /// Putting it in the disconnect message is the difference between an error a user can act on
    /// and one they can only re-try.
    /// </remarks>
    internal static string DescribeFailure(string message, IReadOnlyList<SshCredentialDiagnostic>? diagnostics)
    {
        if (diagnostics is null || diagnostics.Count == 0)
            return message;

        List<string> reasons = [];
        foreach (SshCredentialDiagnostic diagnostic in diagnostics)
        {
            // Informational diagnostics narrate what worked; only the ones describing something
            // unusable help here, and repeating the rest would bury them.
            if (diagnostic.Severity is SshCredentialDiagnosticSeverity.Error
                or SshCredentialDiagnosticSeverity.ProtocolError
                && !reasons.Contains(diagnostic.Message, StringComparer.Ordinal))
            {
                reasons.Add(diagnostic.Message);
            }
        }

        return reasons.Count == 0 ? message : $"{message} {string.Join(" ", reasons)}";
    }

    /// <summary>
    /// Replays resolution diagnostics on the channel each was recorded against. The session cannot
    /// do this itself — <see cref="ProtocolBase.Event_ErrorOccured"/> is protected and raises an
    /// event callers subscribe to.
    /// </summary>
    private void ReplayCredentialDiagnostics(System.Collections.Generic.IReadOnlyList<SshCredentialDiagnostic> diagnostics)
    {
        foreach (SshCredentialDiagnostic diagnostic in diagnostics)
        {
            switch (diagnostic.Severity)
            {
                case SshCredentialDiagnosticSeverity.ProtocolError:
                    Event_ErrorOccured(this, diagnostic.Message, 0);
                    break;
                case SshCredentialDiagnosticSeverity.Error:
                    Runtime.MessageCollector.AddMessage(MessageClass.ErrorMsg, diagnostic.Message);
                    break;
                default:
                    Runtime.MessageCollector.AddMessage(MessageClass.InformationMsg, diagnostic.Message);
                    break;
            }
        }
    }

    private void OnOutputReceived(string text)
    {
        if (!_webView.IsHandleCreated || _webView.IsDisposed)
            return;

        // The pump runs on its own thread; everything below here is UI-thread only.
        _webView.BeginInvoke(() => Post(new JsonObject { ["t"] = "output", ["d"] = text }));
    }

    private void OnSessionDisconnected(string reason)
    {
        if (!_webView.IsHandleCreated || _webView.IsDisposed)
            return;

        _webView.BeginInvoke(() =>
        {
            IsSessionDisconnected = true;
            Event_Disconnected(this, reason, null);
            Close();
        });
    }

    private void Post(JsonObject message)
    {
        try
        {
            _webView.CoreWebView2?.PostWebMessageAsJson(message.ToJsonString());
        }
        catch (ObjectDisposedException)
        {
            // The control was torn down between the check and the post. Nothing to say about it.
        }
        catch (InvalidOperationException)
        {
        }
    }

    /// <summary>
    /// The clipboard is handled here rather than through <c>navigator.clipboard</c>: the web API is
    /// gesture- and permission-gated inside WebView2, and this application already owns the Windows
    /// clipboard. The page only reports what is selected, or asks for what is held.
    /// </summary>
    private static void CopyToClipboard(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return;

        try
        {
            Clipboard.SetText(text);
        }
        catch (System.Runtime.InteropServices.ExternalException ex)
        {
            // Another process can hold the clipboard open; Windows offers no way to wait politely.
            Runtime.MessageCollector.AddExceptionStackTrace(Language.SshNativeClipboardFailed, ex);
        }
    }

    private void PasteFromClipboard()
    {
        string text;
        try
        {
            if (!Clipboard.ContainsText())
                return;

            text = Clipboard.GetText();
        }
        catch (System.Runtime.InteropServices.ExternalException ex)
        {
            Runtime.MessageCollector.AddExceptionStackTrace(Language.SshNativeClipboardFailed, ex);
            return;
        }

        if (string.IsNullOrEmpty(text))
            return;

        // Back through the page so xterm applies bracketed-paste wrapping. Writing straight to the
        // shell would drop it silently, and a pasted command would execute on arrival.
        Post(new JsonObject { ["t"] = "paste", ["d"] = text });
    }

    public override void Focus()
    {
        base.Focus();
        Post(new JsonObject { ["t"] = "focus" });
    }

    protected override void Resize(object sender, EventArgs e)
    {
        // The page owns the character-cell arithmetic: it measures the font and reports the new
        // dimensions back through "resize", which is what reaches ChangeWindowSize.
        Post(new JsonObject { ["t"] = "fit" });
        base.Resize(sender, e);
    }

    public override void Disconnect()
    {
        TearDownSession();
        base.Disconnect();
    }

    private void TearDownSession()
    {
        INativeSshTerminalSession? session = _session;
        _session = null;

        if (session is null)
            return;

        session.OutputReceived -= OnOutputReceived;
        session.Disconnected -= OnSessionDisconnected;
        session.Dispose();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            TearDownSession();

            if (_webView.CoreWebView2 is not null)
                _webView.CoreWebView2.WebMessageReceived -= OnWebMessageReceived;

            // The profile folder is this instance's alone and is worth nothing once the tab closes.
            if (_userDataFolder is not null)
            {
                try
                {
                    Directory.Delete(_userDataFolder, recursive: true);
                }
                catch (IOException)
                {
                    // WebView2 may still hold files briefly; it lives under the temp directory.
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }

        base.Dispose(disposing);
    }
}
