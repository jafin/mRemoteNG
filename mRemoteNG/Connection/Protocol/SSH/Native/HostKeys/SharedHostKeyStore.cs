using System.Runtime.Versioning;

namespace mRemoteNG.Connection.Protocol.SSH.Native.HostKeys;

/// <summary>
/// The one record of accepted host keys the whole application reads and writes.
/// </summary>
/// <remarks>
/// <para>
/// Every consumer builds its own <see cref="HostKeyGate"/>, because the verifier has to reach the UI
/// thread of the window that is connecting and no single control serves all of them. What cannot
/// differ is the trust record: a key accepted for a terminal session has to be known to the file
/// manager and to the transfer window, or the user is asked once per feature about one decision.
/// </para>
/// <para>
/// One instance rather than one per gate. Separate <see cref="FileHostKeyStore"/> instances over the
/// same file would stay consistent — it locks and re-reads on every call — but each instance's lock
/// would then guard only itself, and the serialization <see cref="HostKeyDecisionLock"/> provides
/// would be the only thing keeping two writers apart.
/// </para>
/// </remarks>
[SupportedOSPlatform("windows")]
public static class SharedHostKeyStore
{
    /// <summary>Created on first use, so nothing touches the settings path at type load.</summary>
    public static IHostKeyStore Instance { get; } = new FileHostKeyStore();
}
