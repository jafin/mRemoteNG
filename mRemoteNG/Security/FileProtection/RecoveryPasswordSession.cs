using System.Security;
using System.Threading;

namespace mRemoteNG.Security.FileProtection;

/// <summary>
/// The recovery password that has already opened this store, kept for the run.
/// </summary>
/// <remarks>
/// <para>
/// Where a machine protector is absent or cannot be used — a shared file, the portable edition, a
/// restored backup — the recovery password is what opens the store, and the store is read more than
/// once per run: after an external change, and on the automatic backup-recovery path. Prompting each
/// time would turn a password meant to be typed rarely into one typed constantly, which is how a user
/// ends up choosing a short one.
/// </para>
/// <para>
/// Held no longer than the process, and dropped when the store locks. <c>XmlConnectionsDecryptor</c>
/// already caches its derived decryption key for the same reason and for the same lifetime; this is
/// the one secret the user types that would otherwise be asked for repeatedly.
/// </para>
/// </remarks>
public static class RecoveryPasswordSession
{
    private static readonly Lock Gate = new();
    private static SecureString? _remembered;

    /// <summary>Whether a password from this run is available to try.</summary>
    public static bool HasRemembered
    {
        get
        {
            lock (Gate)
                return _remembered is not null;
        }
    }

    /// <summary>
    /// Keeps a copy of a password that has just opened the store.
    /// </summary>
    /// <remarks>
    /// A copy, so the caller stays free to dispose its own — the requestor's contract everywhere else
    /// in this codebase is that the caller owns what it returned.
    /// </remarks>
    public static void Remember(SecureString password)
    {
        if (password is null || password.Length == 0)
            return;

        SecureString copy = password.Copy();
        copy.MakeReadOnly();

        lock (Gate)
        {
            _remembered?.Dispose();
            _remembered = copy;
        }
    }

    /// <summary>A copy of the remembered password, or null. The caller owns what it gets back.</summary>
    public static SecureString? Peek()
    {
        lock (Gate)
            return _remembered?.Copy();
    }

    /// <summary>
    /// Forgets the password. Called when the store locks, which is what stops this defeating
    /// <c>AutoLockOnMinimize</c> — a lock whose whole purpose is that walking away requires
    /// re-authentication.
    /// </summary>
    public static void Clear()
    {
        lock (Gate)
        {
            _remembered?.Dispose();
            _remembered = null;
        }
    }
}
