using System;
using mRemoteNG.Tools.Cmdline;

namespace mRemoteNG.Security.Ssh.Adapters;

/// <summary>
/// Translates a <see cref="ResolvedSshCredential"/> into PuTTY command-line arguments.
/// </summary>
/// <remarks>
/// <para>
/// Split into two calls rather than one because PuTTY's argument order is not what a single
/// "append the credentials" method would produce: <c>-l</c> and the password sit inside the
/// <c>Force.NoCredentials</c> guard, while <c>-i</c> sits outside it and after the Vault
/// SSH-OTP block. Merging them would change which arguments <c>NoCredentials</c> suppresses.
/// </para>
/// <para>
/// The PuTTY version and the password-pipe factory are injected rather than looked up, so the
/// protocol keeps its overridable hooks and tests stay deterministic.
/// </para>
/// </remarks>
/// <param name="puttyVersion">Selects <c>-pwfile</c> (0.81+) over <c>-pw</c>.</param>
/// <param name="passwordPipeFactory">
/// Starts the named pipe carrying the password and returns the path for <c>-pwfile</c>.
/// </param>
public sealed class PuttyArgsAdapter(Version puttyVersion, Func<string, string> passwordPipeFactory)
{
    /// <summary>The first PuTTY release supporting <c>-pwfile</c>.</summary>
    private static readonly Version PasswordFileSupported = new(0, 81);

    private readonly Version _puttyVersion = puttyVersion ?? throw new ArgumentNullException(nameof(puttyVersion));
    private readonly Func<string, string> _passwordPipeFactory =
        passwordPipeFactory ?? throw new ArgumentNullException(nameof(passwordPipeFactory));

    /// <summary>
    /// Appends <c>-l</c> and the password argument. Call only when credentials are permitted.
    /// </summary>
    public void AppendLoginAndPassword(CommandLineArguments arguments, ResolvedSshCredential credential)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(credential);

        if (!string.IsNullOrEmpty(credential.EffectiveUsername))
            arguments.Add("-l", credential.EffectiveUsername);

        if (!credential.HasSecret)
            return;

        if (_puttyVersion >= PasswordFileSupported)
        {
            arguments.Add("-pwfile", _passwordPipeFactory(credential.RevealSecret()));
        }
        else
        {
            arguments.Add("-pw", credential.RevealSecret());
        }

        // NOTE: -batch is a plink.exe option, not a putty.exe one. Passing it to putty.exe
        // produces "option -batch not available in this tool" (#49). Do not reintroduce it.
    }

    /// <summary>
    /// Appends <c>-i</c>. Sits outside the credential guard, matching PuTTY's existing order.
    /// </summary>
    /// <param name="temporaryPrivateKeyPath">
    /// Path the caller wrote provider-supplied key material to, or empty. Takes precedence over
    /// <see cref="ResolvedSshCredential.PrivateKeyPath"/>.
    /// </param>
    public static void AppendIdentity(
        CommandLineArguments arguments,
        ResolvedSshCredential credential,
        string temporaryPrivateKeyPath)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(credential);

        if (!string.IsNullOrEmpty(temporaryPrivateKeyPath))
            arguments.Add("-i", temporaryPrivateKeyPath);
        else if (!string.IsNullOrEmpty(credential.PrivateKeyPath))
            arguments.Add("-i", credential.PrivateKeyPath);
    }
}