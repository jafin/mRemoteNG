using System.Threading;

namespace mRemoteNG.Security.FileProtection;

/// <summary>
/// The machine slot that has already opened a store this run, kept so the search is not repeated.
/// </summary>
/// <remarks>
/// <para>
/// Slots are unlabelled, so the only way to find the one belonging to this account is to try them
/// until one unwraps and authenticates. That is cheap once and wasteful repeatedly: a store is read
/// more than once per run — after an external change, and on the automatic backup-recovery path —
/// and on a file shared by a team the member listed last pays for everyone ahead of them every time.
/// </para>
/// <para>
/// This is a hint and never an answer. The remembered slot is tried first and validated exactly as
/// any other slot is, so a stale one — the file was rekeyed, or replaced from a backup — costs one
/// failed DPAPI call and the search carries on. Nothing here is secret: a wrapped key is what the
/// file already publishes, which is why this can be a plain string where
/// <see cref="RecoveryPasswordSession"/> guards a <c>SecureString</c>.
/// </para>
/// </remarks>
public static class MachineSlotSession
{
    private static readonly Lock Gate = new();
    private static string? _remembered;

    /// <summary>The slot that last opened a store, or null when none has this run.</summary>
    public static string? Peek()
    {
        lock (Gate)
            return _remembered;
    }

    /// <summary>Records the slot that opened a store.</summary>
    public static void Remember(string slot)
    {
        lock (Gate)
            _remembered = slot;
    }

    /// <summary>
    /// Forgets it, when the store's key is replaced and every slot with it.
    /// </summary>
    /// <remarks>
    /// Deliberately not called by the autolock, unlike <see cref="RecoveryPasswordSession.Clear"/>.
    /// That lock exists so walking away requires re-authentication, and a recovery password left in
    /// memory would defeat it. A slot defeats nothing: it is public, and unwrapping it still requires
    /// the Windows account that is already signed in — the same account that would open the store
    /// without any prompt once the screen is unlocked.
    /// </remarks>
    public static void Forget()
    {
        lock (Gate)
            _remembered = null;
    }
}
