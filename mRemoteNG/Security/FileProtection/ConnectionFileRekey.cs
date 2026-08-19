using System;
using System.Runtime.Versioning;
using System.Security;
using mRemoteNG.Connection;
using mRemoteNG.Container;
using mRemoteNG.Tree.Root;

namespace mRemoteNG.Security.FileProtection;

/// <summary>
/// Replaces a connection file's key, its recovery password, and every machine slot it carries.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is the removal operation, and nothing smaller is honest.</b> Deleting one member's slot
/// does not remove their access: they know the recovery password, and on a file kept where several
/// people can open it they have very likely had the opportunity to copy it. The only thing that
/// actually removes access is a new key, a new password, and the contents re-encrypted — which is
/// why slot deletion is not offered and this is.
/// </para>
/// <para>
/// What it cannot do is reach a copy already taken. Someone who kept a copy still has everything the
/// file held up to the moment they took it. Every rekey of every system has this property; a user who
/// is not told it will assume otherwise, so the confirmation says it.
/// </para>
/// <para>
/// The contents are not re-encrypted here. They are re-encrypted by the save that follows, because
/// the saver encrypts with whatever key the root carries — which is what makes this a change of two
/// fields rather than a second implementation of writing the file.
/// </para>
/// </remarks>
[SupportedOSPlatform("windows")]
public static class ConnectionFileRekey
{
    /// <summary>
    /// Gives the store a new key and a new recovery password, dropping every existing machine slot.
    /// </summary>
    /// <param name="includeMachineProtector">
    /// False in the portable edition, which writes no slot here for the same reason it writes none
    /// anywhere else: rekeying is about the key and the password, and is not an occasion to start
    /// binding a file to whichever machine the build was carried to.
    /// </param>
    /// <exception cref="ArgumentException">
    /// The store is not protected by a per-file key, or the new recovery password is empty. A store
    /// with no recovery password could never be opened anywhere else, and a rekey is the last moment
    /// at which that would still be recoverable.
    /// </exception>
    public static void Apply(RootNodeInfo rootNode,
                             SecureString newRecoveryPassword,
                             bool includeMachineProtector,
                             int iterations = RecoveryPasswordKeyProtector.DefaultIterations)
    {
        ArgumentNullException.ThrowIfNull(rootNode);
        ArgumentNullException.ThrowIfNull(newRecoveryPassword);

        if (rootNode.KeyProtection is null)
            throw new ArgumentException(
                "This connection file is not protected by a key of its own, so there is nothing to " +
                "rekey. Harden its storage format first.", nameof(rootNode));

        if (newRecoveryPassword.Length == 0)
            throw new ArgumentException(
                "A rekey needs a new recovery password.", nameof(newRecoveryPassword));

        // Before the key is replaced, and this is the step that stops a rekey destroying half the
        // store. A record whose secret nothing ever read still holds it as ciphertext under the key
        // this is about to discard, and the save that follows writes those bytes through untouched -
        // so the file would end up encrypted under two keys, with the half under the old one
        // unreadable for ever. Decrypting first is also why a secret that cannot be read refuses the
        // rekey here, while the store is still exactly as it was.
        DecryptEverySecretInTheStore(rootNode);

        // Built before anything on the root is touched. A root left holding a key its protectors do
        // not wrap is the one state a store can be neither saved from nor reopened in, and a rekey
        // that fails halfway is exactly how it would get there.
        ConnectionFileKey newKey = ConnectionFileKey.Generate();
        ConnectionFileKeyProtection protection;
        try
        {
            protection = ConnectionFileKeyProtection.Create(
                newKey, newRecoveryPassword, includeMachineProtector, iterations);
        }
        catch
        {
            newKey.Dispose();
            throw;
        }

        rootNode.FileKey?.Dispose();
        rootNode.FileKey = newKey;
        rootNode.KeyProtection = protection;

        // `KeyGenerationSeen` is deliberately left as it was. The new protection carries a new
        // generation and the file still holds the old one, and that gap is what lets the save below
        // through while refusing a save from any session that has not seen this rekey — which is the
        // whole point of it. It closes itself when the save lands.

        // The slot this run remembered belongs to the key that was just discarded. Left in place it
        // would be tried first on the next read of every file and fail, which costs nothing but is
        // exactly the sort of stale state that is hard to reason about later.
        MachineSlotSession.Forget();
        RecoveryPasswordSession.Clear();
    }

    /// <summary>
    /// Resolves every secret in the store that is still held as ciphertext.
    /// </summary>
    /// <exception cref="ConnectionSecretDecryptionException">
    /// One of them could not be decrypted, so the rekey is refused and the store left untouched. A
    /// secret skipped here would be a secret lost: nothing else in the run still has the old key.
    /// </exception>
    private static void DecryptEverySecretInTheStore(ConnectionInfo node)
    {
        node.DecryptEveryStoredSecret();

        if (node is not ContainerInfo container)
            return;

        foreach (ConnectionInfo child in container.Children)
            DecryptEverySecretInTheStore(child);
    }
}
