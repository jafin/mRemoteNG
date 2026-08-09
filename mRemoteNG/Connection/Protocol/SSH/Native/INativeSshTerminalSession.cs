using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using mRemoteNG.Security.Ssh;

namespace mRemoteNG.Connection.Protocol.SSH.Native;

/// <summary>
/// The transport behind a native SSH terminal: a PTY-backed shell, its output, and its size.
/// </summary>
/// <remarks>
/// An interface so the protocol and its UI can be exercised without a server. The concrete
/// implementation is <see cref="NativeSshTerminalSession"/>.
/// </remarks>
public interface INativeSshTerminalSession : IDisposable
{
    /// <summary>Decoded shell output, ready to hand to the emulator.</summary>
    event Action<string> OutputReceived;

    /// <summary>
    /// Raised once when the session ends for any reason — the remote shell exiting and the
    /// transport failing are both disconnects, and the terminal treats them the same way.
    /// </summary>
    event Action<string> Disconnected;

    /// <summary>
    /// Messages produced while resolving credentials, plus anything the SSH.NET adapter could not
    /// use. The caller replays these on the channel each one records; the session cannot, because
    /// <c>ProtocolBase.Event_ErrorOccured</c> is protected.
    /// </summary>
    IReadOnlyList<SshCredentialDiagnostic> Diagnostics { get; }

    bool IsConnected { get; }

    /// <summary>Connects, authenticates, and opens a shell of the given size.</summary>
    Task ConnectAsync(uint columns, uint rows, CancellationToken cancellationToken = default);

    /// <summary>Sends user input. A no-op when no shell is open.</summary>
    void Send(string data);

    /// <summary>
    /// Tells the remote pseudo-terminal its new size. A no-op when no shell is open — the control
    /// is laid out before the session exists and after it ends, and neither is an error.
    /// </summary>
    void Resize(uint columns, uint rows);
}
