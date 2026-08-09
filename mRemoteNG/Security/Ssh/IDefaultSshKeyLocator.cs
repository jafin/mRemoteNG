using System;
using System.IO;

namespace mRemoteNG.Security.Ssh;

/// <summary>
/// Finds a default private key in the user profile when a connection configures neither a key
/// path nor a password.
/// </summary>
public interface IDefaultSshKeyLocator
{
    /// <summary>Returns the first matching default key, or <see langword="null"/>.</summary>
    string? Locate(DefaultKeyDiscoveryMode mode);
}

/// <summary>
/// Adapts an arbitrary lookup function to <see cref="IDefaultSshKeyLocator"/>.
/// </summary>
/// <remarks>
/// Lets a protocol keep owning its own discovery hook while still handing the resolver a
/// locator — <c>PuttyBase</c> routes through its overridable <c>FindDefaultPrivateKey()</c>
/// this way, so the seam its tests depend on stays live after the cutover.
/// </remarks>
public sealed class DelegateSshKeyLocator(Func<DefaultKeyDiscoveryMode, string?> locate) : IDefaultSshKeyLocator
{
    private readonly Func<DefaultKeyDiscoveryMode, string?> _locate =
        locate ?? throw new ArgumentNullException(nameof(locate));

    public string? Locate(DefaultKeyDiscoveryMode mode) =>
        mode == DefaultKeyDiscoveryMode.None ? null : _locate(mode);
}

/// <summary>
/// Locates default keys under <c>%USERPROFILE%\.ssh</c>.
/// </summary>
/// <remarks>
/// The two candidate lists differ on purpose and both are carried over verbatim from the code
/// they replace.
/// <para>
/// <b>PuTTY</b> gets <c>.ppk</c> only. PuTTY/plink can load an OpenSSH-format key via <c>-i</c>
/// only on 0.75+; older clients print "Unable to use key file" and skip it, which wastes an
/// auth round trip and can trip a server's <c>MaxAuthTries</c> before agent auth is tried.
/// </para>
/// <para>
/// <b>OpenSSH</b> gets OpenSSH-format names, and includes <c>id_dsa</c> — which the PuTTY list
/// does not. That asymmetry is inherited from the existing code, not a decision made here.
/// </para>
/// </remarks>
public sealed class DefaultSshKeyLocator : IDefaultSshKeyLocator
{
    private static readonly string[] PuttyKeyNames =
    [
        "id_ed25519.ppk", "id_rsa.ppk", "id_ecdsa.ppk"
    ];

    private static readonly string[] OpenSshKeyNames =
    [
        "id_ed25519", "id_ecdsa", "id_rsa", "id_dsa"
    ];

    public string? Locate(DefaultKeyDiscoveryMode mode)
    {
        string[] candidates = mode switch
        {
            DefaultKeyDiscoveryMode.PuttyPpk => PuttyKeyNames,
            DefaultKeyDiscoveryMode.OpenSsh => OpenSshKeyNames,
            _ => []
        };

        if (candidates.Length == 0)
            return null;

        string sshDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ssh");

        if (!Directory.Exists(sshDir))
            return null;

        foreach (string keyName in candidates)
        {
            string candidate = Path.Combine(sshDir, keyName);
            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }
}