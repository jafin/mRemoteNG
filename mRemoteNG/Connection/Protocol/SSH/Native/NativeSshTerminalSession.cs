using System;
using System.Collections.Generic;
using System.Runtime.Versioning;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using mRemoteNG.Connection.Protocol.SSH.Native.HostKeys;
using mRemoteNG.Security.Ssh;
using mRemoteNG.Security.Ssh.Adapters;
using mRemoteNG.Security.Ssh.Agent;
using Renci.SshNet;
using Renci.SshNet.Common;
using SshNetConnectionInfo = Renci.SshNet.ConnectionInfo;

namespace mRemoteNG.Connection.Protocol.SSH.Native;

/// <summary>
/// An in-process SSH shell: SSH.NET for the transport, a PTY on the remote end, decoded output.
/// </summary>
/// <remarks>
/// <para>
/// Authentication goes through <see cref="SshCredentialResolver"/> and
/// <see cref="SshNetAuthAdapter"/>, the same path <c>SftpSession</c> and <c>SecureTransfer</c>
/// use. A terminal with its own credential path would reintroduce exactly the per-backend
/// divergence the shared model removed.
/// </para>
/// <para>
/// Single use. Reconnecting means a new session, which keeps this free of the release-and-retry
/// sequencing <c>SftpSession</c> needs.
/// </para>
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class NativeSshTerminalSession : INativeSshTerminalSession
{
    private readonly string _host;
    private readonly int _port;
    private readonly ResolvedSshCredential _credential;
    private readonly SshNetAuthentication _authentication;
    private readonly HostKeyGate _hostKeys;
    private readonly CancellationTokenSource _teardown = new();

    private SshClient? _client;
    private ShellStream? _shell;
    private int _disconnectRaised;
    private bool _disposed;

    public event Action<string>? OutputReceived;
    public event Action<string>? Disconnected;

    /// <param name="credential">
    /// Ownership passes to this instance. The authentication methods read it lazily, so it must
    /// stay alive for the life of the connection.
    /// </param>
    /// <param name="hostKeys">
    /// Decides whether the presented host key is acceptable. Omitting it refuses every key: a
    /// session with no way to ask must not answer on the user's behalf, and silent acceptance is
    /// the one outcome the spec forbids.
    /// </param>
    public NativeSshTerminalSession(
        string host, int port, ResolvedSshCredential credential, HostKeyGate? hostKeys = null)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(credential);

        _host = host;
        _port = port;
        _credential = credential;
        _hostKeys = hostKeys ?? new HostKeyGate(new FileHostKeyStore(), new DenyUnverifiedHostKeys());

        // Translated up front rather than at connect time so Diagnostics is answerable before a
        // connection exists — the protocol reports them whether or not the session got that far.
        // Translation never throws for unusable key material; it records it in Unsupported.
        _authentication = SshNetAuthAdapter.Translate(credential);
        Diagnostics = [.. credential.Diagnostics, .. _authentication.Unsupported];
    }

    /// <summary>
    /// Builds a session for an mRemoteNG connection, resolving credentials the way every other
    /// SSH.NET-backed caller does.
    /// </summary>
    /// <param name="agentSettings">
    /// Reuses <c>SftpSession</c>'s indirection rather than declaring a second one. The abstraction
    /// wants promoting out of that class now it has a second caller, but moving it is a change to
    /// SFTP and belongs in its own commit.
    /// </param>
    public static NativeSshTerminalSession ForConnection(
        ConnectionInfo connectionInfo,
        Sftp.SftpSession.ISshAgentSettingsSource? agentSettings = null,
        HostKeyGate? hostKeys = null)
    {
        ArgumentNullException.ThrowIfNull(connectionInfo);

        bool agentEnabled = agentSettings?.IsEnabled ?? SshAgentSettings.Default.IsEnabled;

        // The agent provider has to be supplied, not merely permitted: the resolver consults the
        // agent only when ConsultAgent and a provider are both present.
        ResolvedSshCredential credential = SshCredentialResolver
            .CreateDefault(new DefaultSshKeyLocator(),
                agentEnabled ? new SshNetAgentProvider() : null)
            .Resolve(connectionInfo, SshCredentialResolutionOptions.ForSshNet(agentEnabled));

        return new NativeSshTerminalSession(
            connectionInfo.Hostname.Trim(), connectionInfo.Port, credential, hostKeys);
    }

    public IReadOnlyList<SshCredentialDiagnostic> Diagnostics { get; }

    /// <summary>The authentication methods offered to the server, in the order they were tried.</summary>
    public IReadOnlyList<string> OfferedMethods =>
        [.. System.Linq.Enumerable.Select(_authentication.Methods, m => m.Name)];

    /// <summary>
    /// The key file that actually became an authentication source, or null if none did. Not a
    /// secret; a path. Deliberately not the credential's resolved path — a key that failed to load
    /// was never sent, and naming it would point the reader at a file the server never saw.
    /// </summary>
    public string? OfferedKeyPath => _authentication.KeyFileOffered;

    /// <summary>The endpoint actually dialled. The port is not shown anywhere else.</summary>
    public string Endpoint => string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{_host}:{_port}");

    /// <summary>
    /// The username authentication was attempted as — which is not always the one typed into the
    /// connection, since it can be inherited or supplied by a credential provider.
    /// </summary>
    public string OfferedUsername => _authentication.Username;

    /// <summary>
    /// Keyboard-interactive prompts the server asked that nothing could answer — typically a second
    /// factor. Only meaningful after a connection attempt.
    /// </summary>
    public IReadOnlyList<string> UnansweredPrompts => _authentication.UnansweredPrompts;

    public bool IsConnected => _client?.IsConnected == true && _shell is not null;

    public async Task ConnectAsync(uint columns, uint rows, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_client is not null)
            throw new InvalidOperationException("This session has already been connected.");

        SshNetConnectionInfo connectionInfo =
            new(_host, _port, _authentication.Username, _authentication.Methods);

        SshClient client = new(connectionInfo);
        client.ErrorOccurred += OnTransportError;

        // Without a handler SSH.NET trusts whatever it is given. That is the silent acceptance the
        // spec rules out, so this is not optional decoration.
        client.HostKeyReceived += OnHostKeyReceived;

        try
        {
            await client.ConnectAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            client.ErrorOccurred -= OnTransportError;
            client.HostKeyReceived -= OnHostKeyReceived;
            client.Dispose();
            throw;
        }

        _client = client;

        // Dispose can run while the await above is outstanding — closing the tab mid-connect does
        // exactly that. It saw a null _client and tore everything else down, so this client is
        // unowned: close it here rather than leave a live SSH connection for the life of the
        // process, and do not touch the already-disposed _teardown below.
        if (_disposed)
        {
            client.ErrorOccurred -= OnTransportError;
            client.HostKeyReceived -= OnHostKeyReceived;
            _client = null;
            SafeDispose(client);
            return;
        }

        // Zero pixel dimensions tell the server to size from the character cell counts, which is
        // what the emulator reports and the only thing it can report accurately.
        _shell = client.CreateShellStream("xterm-256color", columns, rows, 0, 0, 64 * 1024);

        ShellStreamPump pump = new(_shell);
        pump.TextReceived += text => OutputReceived?.Invoke(text);
        pump.Closed += RaiseDisconnected;

        // Read before the task starts: Dispose can win the race to dispose the source, and reading
        // Token from inside the delegate would then throw where nothing observes it.
        CancellationToken teardownToken = _teardown.Token;

        // Fire and forget by design: the pump owns its own lifetime and reports its end through
        // Closed. Awaiting it here would mean never returning from Connect.
        _ = Task.Run(() => pump.PumpAsync(teardownToken), CancellationToken.None);
    }

    public void Send(string data)
    {
        if (string.IsNullOrEmpty(data))
            return;

        ShellStream? shell = _shell;
        if (shell is null)
            return;

        byte[] bytes = Encoding.UTF8.GetBytes(data);

        try
        {
            shell.Write(bytes, 0, bytes.Length);
            shell.Flush();
        }
        catch (Exception ex) when (ex is ObjectDisposedException or SshException or System.IO.IOException)
        {
            // Same race as Resize: the shell ended between the null check and the write. The
            // disconnect is reported on its own; a lost keystroke on a dead session is not.
        }
    }

    public void Resize(uint columns, uint rows)
    {
        // Spec: resizing while disconnected sends nothing and raises nothing. The control is laid
        // out before Connect and after the shell ends, so this is the normal case, not an edge one.
        ShellStream? shell = _shell;
        if (shell is null)
            return;

        try
        {
            shell.ChangeWindowSize(columns, rows, 0, 0);
        }
        catch (Exception ex) when (ex is ObjectDisposedException or SshException)
        {
            // The shell went away between the null check and the call. A resize that loses a race
            // with a disconnect is not worth surfacing; the disconnect itself already is.
        }
    }

    private void OnHostKeyReceived(object? sender, HostKeyEventArgs e)
    {
        // FingerPrintSHA256 is base64 without padding, matching what OpenSSH prints, so a user can
        // compare it against ssh-keyscan output character for character.
        e.CanTrust = _hostKeys.Evaluate(_host, _port, e.HostKeyName, $"SHA256:{e.FingerPrintSHA256}");
    }

    private void OnTransportError(object sender, ExceptionEventArgs e) =>
        RaiseDisconnected(e.Exception?.Message ?? "The SSH transport failed.");

    private void RaiseDisconnected(string reason)
    {
        // The shell exiting and the transport failing can both land here, and a failing transport
        // usually produces both. They arrive on different threads — SSH.NET's and the pump's — so
        // the claim has to be atomic for the terminal to be told exactly once.
        if (Interlocked.Exchange(ref _disconnectRaised, 1) != 0)
            return;

        Disconnected?.Invoke(reason);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        try { _teardown.Cancel(); } catch (ObjectDisposedException) { /* already torn down */ }

        if (_client is not null)
        {
            _client.ErrorOccurred -= OnTransportError;
            _client.HostKeyReceived -= OnHostKeyReceived;
        }

        SafeDispose(_shell);
        SafeDispose(_client);

        _authentication.Dispose();
        _credential.Dispose();
        _teardown.Dispose();
    }

    /// <summary>
    /// Disposal of a live SSH object can fail on the way down — the socket may already be gone,
    /// and SSH.NET surfaces that as an exception. There is nothing to do about it while tearing
    /// the session down, and letting it escape would mask whatever caused the teardown.
    /// </summary>
    private static void SafeDispose(IDisposable? disposable)
    {
        try
        {
            disposable?.Dispose();
        }
        catch (SshException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
        catch (System.IO.IOException)
        {
        }
    }
}
