using System;
using System.Runtime.Versioning;
using System.Security;
using mRemoteNG.Tree.Root;

namespace mRemoteNG.Security.FileProtection;

/// <summary>
/// Giving a store its own key, and the rule about when it may not have one.
/// </summary>
/// <remarks>
/// Separate from anything that shows a dialog, so what the migration decides can be tested without a
/// window — the same split <see cref="StorageFormatUpgrade"/> keeps from its prompt.
/// </remarks>
[SupportedOSPlatform("windows")]
public static class ConnectionFileMigration
{
    /// <summary>
    /// Generates a new key for the store and wraps it under both protectors, or under the recovery
    /// password alone where a machine-bound one would not serve.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A <i>new</i> key every time, never a re-wrap of an existing one. Migration is the point at
    /// which the store stops being readable with the key published in this application's source, and
    /// carrying anything forward from that state would defeat it.
    /// </para>
    /// <para>
    /// The root is not touched until both protectors exist. A half-migrated root — a key with no
    /// protectors, or protectors wrapping a key the contents were not encrypted with — is the one
    /// state from which the store cannot be saved <i>or</i> reopened.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentException">The recovery password is empty. It is not optional: a
    /// store with only a machine protector loses its whole backup history the moment the profile is
    /// rebuilt, with no signal until the day it is needed.</exception>
    public static void Establish(RootNodeInfo rootNode,
                                 SecureString recoveryPassword,
                                 bool includeMachineProtector,
                                 int iterations = RecoveryPasswordKeyProtector.DefaultIterations)
    {
        ArgumentNullException.ThrowIfNull(rootNode);
        ArgumentNullException.ThrowIfNull(recoveryPassword);

        if (recoveryPassword.Length == 0)
            throw new ArgumentException(
                "A recovery password is required to protect a store with its own key.",
                nameof(recoveryPassword));

        ConnectionFileKey fileKey = ConnectionFileKey.Generate();
        ConnectionFileKeyProtection protection;
        try
        {
            protection = ConnectionFileKeyProtection.Create(
                fileKey, recoveryPassword, includeMachineProtector, iterations);
        }
        catch
        {
            fileKey.Dispose();
            throw;
        }

        rootNode.FileKey?.Dispose();
        rootNode.FileKey = fileKey;
        rootNode.KeyProtection = protection;
    }

    /// <summary>
    /// Whether this store is already protected by its own key, and so has nothing to migrate.
    /// </summary>
    public static bool IsAlreadyProtected(RootNodeInfo rootNode)
    {
        ArgumentNullException.ThrowIfNull(rootNode);
        return rootNode.KeyProtection is not null;
    }
}
