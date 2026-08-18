using System;
using System.IO;
using System.Runtime.Versioning;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using mRemoteNG.App;
using mRemoteNG.App.Info;
using mRemoteNG.Messages;
using mRemoteNG.Security;
using mRemoteNG.Tools;
using mRemoteNG.Tree;
using mRemoteNG.Tree.Root;

namespace mRemoteNG.Config.Connections;

/// <summary>
/// The local copy of a SQL store, kept so its connections are still readable when the database is
/// not.
/// </summary>
/// <remarks>
/// <para>
/// <b>It holds every password in the store, so how it is keyed is the whole design.</b> Until this
/// existed the cache was written through the ordinary connection-file saver using the *database's*
/// key — which for a database with no master password is
/// <see cref="ConnectionFileDefaults.LegacyEncryptionKey"/>, a constant published in mRemoteNG's own
/// source. A shared team database therefore produced, on every successful load, a local file that
/// anyone could read, and upgrading that database to authenticated encryption did nothing for it.
/// </para>
/// <para>
/// The cache now carries a random key of its own, wrapped by DPAPI for the account that wrote it.
/// Nothing to type, and it cannot be read by another account or on another machine.
/// </para>
/// <para>
/// <b>Deliberately not the connection file's key-slot machinery.</b> That format refuses a machine
/// protector with no recovery protector — see <c>ConnectionFileKeyProtection.Read</c> — because a
/// connection file that opens on exactly one machine is a file somebody eventually loses. A cache is
/// the opposite: it is derived data, disposable by definition, and the correct response to a key
/// that no longer unwraps is to delete it and take a fresh copy. Prompting for a recovery password
/// to read a cache would be absurd, and weakening that invariant for this would be worse.
/// </para>
/// </remarks>
[SupportedOSPlatform("windows")]
public static class SqlConnectionsCache
{
    /// <summary>Bound to the account, not the machine: the same profile roaming still opens it.</summary>
    private const DataProtectionScope Scope = DataProtectionScope.CurrentUser;

    /// <summary>
    /// Where the copy lives. A seam only so a test does not write a connection store into the
    /// developer's real settings folder; nothing in the product sets it.
    /// </summary>
    internal static string Location { get; set; } = SettingsFileInfo.SettingsPath;

    public static string FilePath => Path.Combine(Location, SettingsFileInfo.SqlConnectionsCache);

    /// <summary>Beside the cache, and useless without it — it wraps a key and holds no connection data.</summary>
    private static string KeyFilePath => FilePath + ".key";

    /// <summary>
    /// Whether there is a copy this account can actually read. A cache without its key file is not
    /// "a cache we might manage to open"; it is unreadable, and saying so here keeps the fallback
    /// path from discovering that halfway through.
    /// </summary>
    public static bool IsUsable => File.Exists(FilePath) && File.Exists(KeyFilePath);

    /// <summary>When the copy was taken, for telling the user how stale what they are looking at is.</summary>
    public static DateTime WrittenAtUtc =>
        File.Exists(FilePath) ? File.GetLastWriteTimeUtc(FilePath) : DateTime.MinValue;

    /// <summary>
    /// Deletes a cache written before this existed, rather than leaving it where it is.
    /// </summary>
    /// <remarks>
    /// Such a file is encrypted under the published legacy key and holds every password in the
    /// store. A fix that stops writing new ones and leaves the existing one on disk has fixed
    /// nothing for the people who already have it — which is everybody who has used the SQL backend.
    /// </remarks>
    public static void DiscardIfUnprotected()
    {
        if (!File.Exists(FilePath) || File.Exists(KeyFilePath))
            return;

        Runtime.MessageCollector.AddMessage(MessageClass.InformationMsg,
            "Removing a cached copy of the SQL connections that was written before this version. It " +
            "held every password in the database under a key published in mRemoteNG's source code. A " +
            "new, protected copy is taken the next time the database is read.", true);

        Discard();
    }

    public static void Discard()
    {
        TryDelete(FilePath);
        TryDelete(KeyFilePath);
    }

    /// <summary>
    /// Takes a copy of the store, protected by a fresh key of its own.
    /// </summary>
    /// <remarks>
    /// The key is swapped onto the root node for the duration of the write, because the saver takes
    /// the store's key from the root node it is given and there is no other way to hand it one.
    /// **This is safe only because of where it is called from**: the model has been loaded but not
    /// yet published, so no other thread can see it. Moving this call after the model is assigned
    /// would make it a race that intermittently writes the connection file under the cache's key.
    /// </remarks>
    public static void Write(ConnectionTreeModel connectionTreeModel, SaveFilter saveFilter)
    {
        ArgumentNullException.ThrowIfNull(connectionTreeModel);

        try
        {
            RootNodeInfo? root = null;
            foreach (RootNodeInfo candidate in RootNodesOf(connectionTreeModel))
            {
                root = candidate;
                break;
            }

            if (root is null)
                return;

            string key = NewKey();

            // The key first. A crash between the two writes leaves a key with no cache, which
            // reads as "no cache"; the other order leaves a cache nothing can open.
            Directory.CreateDirectory(Path.GetDirectoryName(KeyFilePath)!);
            File.WriteAllText(KeyFilePath, Convert.ToBase64String(
                ProtectedData.Protect(Encoding.UTF8.GetBytes(key), optionalEntropy: null, Scope)));

            bool hadPassword = root.Password;
            string previousKey = root.PasswordString;

            try
            {
                root.PasswordString = key;
                new XmlConnectionsSaver(FilePath, saveFilter).Save(connectionTreeModel);
            }
            finally
            {
                root.PasswordString = previousKey;
                root.Password = hadPassword;
            }

            Runtime.MessageCollector.AddMessage(MessageClass.DebugMsg,
                $"SQL connections cache saved to '{FilePath}'");
        }
        catch (Exception ex)
        {
            // A cache is a convenience; failing to write one must not fail the load that produced
            // it. But the half-written state is not a convenience, so it goes.
            Discard();
            Runtime.MessageCollector.AddExceptionStackTrace("Failed to save SQL connections cache", ex);
        }
    }

    /// <summary>
    /// Reads the copy back. Throws if it cannot be read, so the caller reports one failure rather
    /// than presenting an empty store as though it were the database's contents.
    /// </summary>
    public static ConnectionTreeModel Read()
    {
        SecureString key = UnwrapKey();

        try
        {
            return new XmlConnectionsLoader(FilePath, null, _ => new Optional<SecureString>(key)).Load();
        }
        finally
        {
            key.Dispose();
        }
    }

    private static SecureString UnwrapKey()
    {
        byte[] wrapped = Convert.FromBase64String(File.ReadAllText(KeyFilePath));
        byte[] plain = ProtectedData.Unprotect(wrapped, optionalEntropy: null, Scope);

        try
        {
            return Encoding.UTF8.GetString(plain).ConvertToSecureString();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plain);
        }
    }

    /// <summary>
    /// 32 random bytes, base64. Not derived from anything — there is no password here to derive
    /// from, and a key derived from something knowable would be a key anybody could recompute.
    /// </summary>
    private static string NewKey() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    private static System.Collections.Generic.IEnumerable<RootNodeInfo> RootNodesOf(ConnectionTreeModel model)
    {
        foreach (Container.ContainerInfo node in model.RootNodes)
            if (node is RootNodeInfo root)
                yield return root;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex)
        {
            Runtime.MessageCollector.AddExceptionStackTrace($"Could not delete '{path}'", ex);
        }
    }
}
