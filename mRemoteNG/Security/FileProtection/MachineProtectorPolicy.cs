using System;
using System.IO;

namespace mRemoteNG.Security.FileProtection;

/// <summary>
/// Decides whether a store should be given a machine-bound protector at all.
/// </summary>
/// <remarks>
/// <para>
/// A connection file carries <b>one</b> machine protector, so on a file several people share it can
/// serve exactly one of them. Everyone else fails it on every open, is told the file was protected by
/// a different account, and has no way to make that stop — there is nowhere to put a second one. A
/// protector only one member of a team can use costs the others a prompt and buys nothing, so a file
/// that does not live under the user's own profile is protected by the recovery password alone.
/// </para>
/// <para>
/// The portable edition is the other case, and for a different reason: it runs on whatever machine it
/// is carried to, so there is no account worth binding to.
/// </para>
/// <para>
/// <b>The detection cannot be exact and does not need to be.</b> A redirected Documents folder is a
/// share its owner does not know they have, and a personal file on a NAS is not a team file. Being
/// wrong in the "no machine protector" direction costs one prompt on a file that would not otherwise
/// have prompted. Being wrong the other way costs every other member of a team a prompt they can
/// never remove. The asymmetry is what settles the unknowable cases, and it is why anything this
/// cannot resolve resolves to <see langword="false"/>.
/// </para>
/// </remarks>
public static class MachineProtectorPolicy
{
    /// <param name="storePath">
    /// Where the store will be written. No longer consulted, and kept so that every caller states
    /// which store it is asking about — see the remarks on the location rule below.
    /// </param>
    /// <param name="isPortableEdition">Normally <c>Runtime.IsPortableEdition</c>.</param>
    /// <remarks>
    /// <para>
    /// <b>Two rules used to share one implementation and no longer do.</b> This once answered
    /// "portable, or outside the user profile?" as a single question, because the machine protector
    /// was a single value: on a file a team shares, one protector serves whoever wrote it and costs
    /// every other member the recovery password on every open, so writing none was the only
    /// defensible answer. With <see cref="ConnectionFileKeyProtection.MachineSlots"/> that is no
    /// longer true — each member earns their own slot — so the location no longer suppresses
    /// anything.
    /// </para>
    /// <para>
    /// The portable rule is untouched and is about the <i>edition</i>, not the location: a build
    /// carried to another machine has no account worth binding to, and a slot per machine visited
    /// would grow without bound and serve nobody.
    /// </para>
    /// <para>
    /// <see cref="IsInsideUserProfile"/> remains, because "is this file likely shared?" is still a
    /// real question — it decides what the user is told before they choose a recovery password. It
    /// simply no longer decides what is written.
    /// </para>
    /// </remarks>
    public static bool ShouldWriteMachineProtector(string? storePath, bool isPortableEdition) =>
        !isPortableEdition;

    /// <summary>
    /// Whether a path lies under the current user's profile directory.
    /// </summary>
    /// <remarks>
    /// A profile that is itself on a share — a roaming profile — still counts as inside, and that is
    /// correct: DPAPI at <c>CurrentUser</c> scope follows the user's master key, which roams with the
    /// profile. What this is looking for is a store the profile does <i>not</i> travel with, which is
    /// the redirected-documents and network-share case.
    /// </remarks>
    public static bool IsInsideUserProfile(string? storePath)
    {
        if (string.IsNullOrWhiteSpace(storePath))
            return false;

        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(profile))
            return false;

        try
        {
            string store = Path.GetFullPath(storePath);
            string root = Path.GetFullPath(profile);

            if (!root.EndsWith(Path.DirectorySeparatorChar))
                root += Path.DirectorySeparatorChar;

            return store.StartsWith(root, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException
                                      or IOException or System.Security.SecurityException)
        {
            // A path this cannot resolve is a path this cannot vouch for. Outside, per the asymmetry
            // above: one prompt is the cheaper way to be wrong.
            return false;
        }
    }
}
