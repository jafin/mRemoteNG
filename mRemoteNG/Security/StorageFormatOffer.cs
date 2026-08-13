using System;
using System.IO;

namespace mRemoteNG.Security;

/// <summary>Remembers that a store's user declined the hardening offer.</summary>
public interface IStorageFormatOfferLog
{
    bool WasDeclined(string storePath);

    /// <summary>Records a decline. Returns false if it could not be persisted.</summary>
    bool RecordDecline(string storePath);
}

/// <summary>
/// Records the decline in a file beside the store it is about.
/// </summary>
/// <remarks>
/// <para>
/// Not in the connection file: a classic store must stay byte-compatible with what upstream
/// mRemoteNG writes, and an unknown element recording a dismissal is exactly the kind of construct
/// that rule exists to keep out. Not in application settings either — the decision belongs to the
/// store, so it has to survive a reinstall and must not follow the user to a different file.
/// </para>
/// <para>
/// A file beside the store satisfies both, and travels with the store when it is copied to another
/// machine. Upstream mRemoteNG never looks at it.
/// </para>
/// </remarks>
public sealed class SidecarStorageFormatOfferLog : IStorageFormatOfferLog
{
    internal const string Suffix = ".hardening-declined";

    private static string SidecarFor(string storePath) => storePath + Suffix;

    public bool WasDeclined(string storePath)
    {
        if (string.IsNullOrWhiteSpace(storePath))
            return false;

        try
        {
            return File.Exists(SidecarFor(storePath));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            // Unreadable is not declined. Offering again is the recoverable direction: the user can
            // decline a second time, whereas suppressing the offer forever leaves a security feature
            // nobody is ever told about.
            return false;
        }
    }

    public bool RecordDecline(string storePath)
    {
        if (string.IsNullOrWhiteSpace(storePath))
            return false;

        try
        {
            string sidecar = SidecarFor(storePath);
            if (!File.Exists(sidecar))
                File.WriteAllText(sidecar, "");

            // Beside the user's connections, not something they have to wonder about.
            File.SetAttributes(sidecar, FileAttributes.Hidden);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException
                                       or ArgumentException)
        {
            // A read-only directory means the offer comes back next session. Worth reporting, which
            // is why this reports rather than swallowing, but not worth failing the user's answer
            // over — they still declined, and nothing was hardened.
            return false;
        }
    }
}

/// <summary>Whether a store should be offered the upgrade at all.</summary>
public static class StorageFormatOffer
{
    /// <summary>
    /// A connection file that is not yet fully hardened and whose user has not already declined.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Not a SQL store, at any level. That upgrade is decided by whoever administers the database,
    /// not by whoever opens the application first — an accepting click from someone without that
    /// authority costs their colleagues access. It lives in the SQL options page instead.
    /// </para>
    /// <para>
    /// <b>Not a store already at the hardened level, which is not the same test.</b> See
    /// <see cref="IsFullyHardened"/>.
    /// </para>
    /// </remarks>
    public static bool ShouldOffer(StorageFormatLevel level,
        StorageFormatStoreKind storeKind,
        bool previouslyDeclined,
        bool hasPerFileKey) =>
        storeKind == StorageFormatStoreKind.ConnectionFile &&
        !previouslyDeclined &&
        !IsFullyHardened(level, storeKind, hasPerFileKey);

    /// <summary>
    /// Whether this store already has everything the hardened format gives it, and so has nothing
    /// left to be offered.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The level alone does not answer this, and reading it as though it did was a real defect.</b>
    /// A connection file raised to <see cref="StorageFormatLevel.Hardened"/> by
    /// <c>add-storage-format-opt-in</c> has a stretched KDF and no key of its own — it is still
    /// encrypted under the published default constant, and stretching a constant everybody has
    /// changes nothing about who can read the file. Deciding from the level excluded exactly the
    /// users who had taken the earlier security upgrade, and told them there was nothing left to do.
    /// </para>
    /// <para>
    /// A SQL store has no per-file key by design — several people read one database, so a key wrapped
    /// for one Windows account is meaningless there, and <c>require-sql-master-password</c> owns what
    /// replaces the default. For that kind the level genuinely is the whole answer.
    /// </para>
    /// </remarks>
    public static bool IsFullyHardened(StorageFormatLevel level,
        StorageFormatStoreKind storeKind,
        bool hasPerFileKey) =>
        level == StorageFormatLevel.Hardened &&
        (storeKind == StorageFormatStoreKind.SqlDatabase || hasPerFileKey);
}
