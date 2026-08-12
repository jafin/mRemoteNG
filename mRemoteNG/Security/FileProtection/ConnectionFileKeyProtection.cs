using System;
using System.Linq;
using System.Runtime.Versioning;
using System.Security;
using System.Security.Cryptography;
using System.Xml.Linq;
using mRemoteNG.Tools;

namespace mRemoteNG.Security.FileProtection;

/// <summary>
/// The two wrapped copies of a connection file's key, and the rule for choosing between them.
/// </summary>
/// <remarks>
/// <para>
/// A random per-file key encrypts the contents. That key is stored twice in the file's root: once
/// wrapped by DPAPI at <c>CurrentUser</c> scope, once wrapped by a key stretched from a recovery
/// password. Either unwraps it, which is what makes the rest of the design work — daily use costs no
/// prompt, and a file that has left the machine is still openable by the person who owns it.
/// </para>
/// <para>
/// <b>The recovery protector is not optional.</b> A file with only the machine protector is a file
/// whose entire backup history becomes worthless the moment the profile is rebuilt, with no signal
/// until the day it is needed. The portable edition is the one case that omits the <i>machine</i>
/// protector — see <c>Runtime.IsPortableEdition</c> and task 7.1 — and never the other way round.
/// </para>
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class ConnectionFileKeyProtection
{
    /// <summary>Root attribute holding the DPAPI-wrapped file key. Absent in the portable edition.</summary>
    public const string MachineProtectorAttributeName = "KeyProtectorMachine";

    /// <summary>Root attribute holding the recovery-password-wrapped file key. Always present.</summary>
    public const string RecoveryProtectorAttributeName = "KeyProtectorRecovery";

    /// <summary>How many times a recovery password may be re-entered, matching <c>PasswordAuthenticator</c>.</summary>
    private const int MaxRecoveryPasswordAttempts = 3;

    private ConnectionFileKeyProtection(string? machineProtector, string recoveryProtector)
    {
        MachineProtector = machineProtector;
        RecoveryProtector = recoveryProtector;
    }

    public string? MachineProtector { get; }

    public string RecoveryProtector { get; }

    public bool HasMachineProtector => !string.IsNullOrWhiteSpace(MachineProtector);

    /// <param name="includeMachineProtector">
    /// False for the portable edition, which runs as whatever account happens to be at the keyboard
    /// and whose whole purpose is that its files open elsewhere. A DPAPI blob there would be dead
    /// weight at best and a file that opens on exactly one machine at worst.
    /// </param>
    public static ConnectionFileKeyProtection Create(ConnectionFileKey fileKey,
                                                     SecureString recoveryPassword,
                                                     bool includeMachineProtector = true,
                                                     int iterations = RecoveryPasswordKeyProtector.DefaultIterations,
                                                     HashAlgorithmName? prf = null)
    {
        ArgumentNullException.ThrowIfNull(fileKey);
        ArgumentNullException.ThrowIfNull(recoveryPassword);

        return new ConnectionFileKeyProtection(
            includeMachineProtector ? DpapiKeyProtector.Wrap(fileKey) : null,
            RecoveryPasswordKeyProtector.Wrap(fileKey, recoveryPassword, iterations, prf));
    }

    /// <summary>
    /// Recovers the file key, trying the machine protector first and asking for the recovery password
    /// only if it cannot help.
    /// </summary>
    /// <param name="recoveryPasswordRequestor">
    /// Asked for the recovery password, once per attempt. A null requestor, or one that returns
    /// nothing, ends the attempt — this is the same contract the master-password path already uses,
    /// so a non-interactive caller cannot be made to block.
    /// </param>
    /// <param name="onMachineProtectorFailed">
    /// Told why the machine protector could not be used, before the user is asked for anything. The
    /// prompt on its own reads as "your password is wrong"; the cause — this file was protected by
    /// another account or another machine — is the part that lets someone act on it.
    /// </param>
    /// <exception cref="KeyProtectionException">
    /// Neither protector produced the key. Neither can produce a <i>wrong</i> key: DPAPI and AES-GCM
    /// both authenticate, so failure is an exception rather than plausible bytes.
    /// </exception>
    public ConnectionFileKey Unwrap(Func<Optional<SecureString>>? recoveryPasswordRequestor,
                                    Action<KeyProtectionException>? onMachineProtectorFailed = null)
    {
        if (HasMachineProtector)
        {
            try
            {
                return DpapiKeyProtector.Unwrap(MachineProtector);
            }
            catch (KeyProtectionException ex)
            {
                onMachineProtectorFailed?.Invoke(ex);
            }
        }

        if (recoveryPasswordRequestor is null)
            throw new KeyProtectionException(KeyProtector.RecoveryPassword,
                "This connection file needs its recovery password, and nothing was available to ask for it.");

        KeyProtectionException? lastFailure = null;
        for (int attempt = 0; attempt < MaxRecoveryPasswordAttempts; attempt++)
        {
            Optional<SecureString> provided = recoveryPasswordRequestor();
            if (!provided.Any())
                break;

            SecureString password = provided.First();
            if (password is null || password.Length == 0)
                break;

            try
            {
                return RecoveryPasswordKeyProtector.Unwrap(RecoveryProtector, password);
            }
            catch (KeyProtectionException ex)
            {
                lastFailure = ex;
            }
        }

        throw lastFailure ?? new KeyProtectionException(KeyProtector.RecoveryPassword,
            "This connection file was not opened: no recovery password was given.");
    }

    /// <summary>
    /// Sets or replaces the recovery password, leaving the machine protector and the file's contents
    /// untouched.
    /// </summary>
    /// <remarks>
    /// This is what makes a forgotten recovery password survivable: on the machine that wrote the
    /// file the machine protector still works, so the key can be unwrapped and re-wrapped under a new
    /// password. Only the recovery protector changes — the file key is the same key, so nothing the
    /// file holds is re-encrypted and a rolling backup taken before the change still opens.
    /// </remarks>
    public ConnectionFileKeyProtection WithRecoveryPassword(ConnectionFileKey fileKey,
                                                            SecureString recoveryPassword,
                                                            int iterations = RecoveryPasswordKeyProtector.DefaultIterations,
                                                            HashAlgorithmName? prf = null)
    {
        ArgumentNullException.ThrowIfNull(fileKey);
        ArgumentNullException.ThrowIfNull(recoveryPassword);

        return new ConnectionFileKeyProtection(MachineProtector,
            RecoveryPasswordKeyProtector.Wrap(fileKey, recoveryPassword, iterations, prf));
    }

    /// <summary>
    /// Adds a machine protector to a file that has none — a portable file opened by the installed
    /// edition, or one whose profile has been rebuilt.
    /// </summary>
    public ConnectionFileKeyProtection WithMachineProtector(ConnectionFileKey fileKey)
    {
        ArgumentNullException.ThrowIfNull(fileKey);
        return new ConnectionFileKeyProtection(DpapiKeyProtector.Wrap(fileKey), RecoveryProtector);
    }

    /// <summary>
    /// Reads the protectors off a root element's attribute values, or null when the file carries none.
    /// </summary>
    /// <remarks>
    /// A recovery protector on its own is a complete file — that is the portable edition. A machine
    /// protector on its own is not, and is refused rather than accepted as a file that happens to open
    /// today: it would be a store nobody could ever recover, which is the failure this whole change
    /// exists to prevent.
    /// </remarks>
    /// <exception cref="KeyProtectionException">
    /// The file declares a machine protector and no recovery protector.
    /// </exception>
    public static ConnectionFileKeyProtection? Read(string? machineProtector, string? recoveryProtector)
    {
        bool hasMachine = !string.IsNullOrWhiteSpace(machineProtector);
        bool hasRecovery = !string.IsNullOrWhiteSpace(recoveryProtector);

        if (!hasMachine && !hasRecovery)
            return null;

        if (!hasRecovery)
            throw new KeyProtectionException(KeyProtector.RecoveryPassword,
                "This connection file carries a machine protector but no recovery protector, so it " +
                "could never be opened anywhere else. It was not written by this application.");

        return new ConnectionFileKeyProtection(hasMachine ? machineProtector : null, recoveryProtector!);
    }

    public void WriteTo(XElement rootElement)
    {
        ArgumentNullException.ThrowIfNull(rootElement);

        if (HasMachineProtector)
            rootElement.SetAttributeValue(XName.Get(MachineProtectorAttributeName), MachineProtector);

        rootElement.SetAttributeValue(XName.Get(RecoveryProtectorAttributeName), RecoveryProtector);
    }
}
