using System;
using System.Collections.Generic;
using System.Runtime.Versioning;
using mRemoteNG.Connection;
using mRemoteNG.Security.Ssh.Providers;

namespace mRemoteNG.Security.Ssh;

/// <summary>
/// The single credential resolution path shared by every SSH backend.
/// </summary>
/// <remarks>
/// Behaviour is carried over from the cascade that lived inline in <c>PuttyBase.Connect()</c>,
/// including several quirks that are preserved deliberately rather than tidied — see the
/// comments at each site. Tidying them here would be a behaviour change hidden inside a
/// refactor; they should be fixed as their own change, against these tests.
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class SshCredentialResolver : ISshCredentialResolver
{
    private readonly IReadOnlyDictionary<ExternalCredentialProvider, ISshCredentialProvider> _providers;
    private readonly IDefaultSshKeyLocator _keyLocator;
    private readonly Agent.ISshAgentProvider? _agentProvider;

    public SshCredentialResolver(
        IEnumerable<ISshCredentialProvider> providers,
        IDefaultSshKeyLocator keyLocator,
        Agent.ISshAgentProvider? agentProvider = null)
    {
        ArgumentNullException.ThrowIfNull(providers);
        ArgumentNullException.ThrowIfNull(keyLocator);

        _agentProvider = agentProvider;

        Dictionary<ExternalCredentialProvider, ISshCredentialProvider> map = [];
        foreach (ISshCredentialProvider provider in providers)
            map[provider.Kind] = provider;

        _providers = map;
        _keyLocator = keyLocator;
    }

    /// <summary>Builds a resolver over the real external connectors.</summary>
    public static SshCredentialResolver CreateDefault() => CreateDefault(new DefaultSshKeyLocator());

    /// <summary>
    /// Builds a resolver over the real external connectors, with a caller-supplied key locator
    /// so a protocol can keep routing discovery through its own overridable hook.
    /// </summary>
    public static SshCredentialResolver CreateDefault(
        IDefaultSshKeyLocator keyLocator,
        Agent.ISshAgentProvider? agentProvider = null) =>
        new(
            [
                new DelineaSecretServerCredentialProvider(),
                new PasswordstateCredentialProvider(),
                new OnePasswordCredentialProvider(),
                new PasswordSafeCredentialProvider(),
                new VaultOpenbaoCredentialProvider(),
                new LapsCredentialProvider(),
            ],
            keyLocator,
            agentProvider);

    public ResolvedSshCredential Resolve(ConnectionInfo connectionInfo, SshCredentialResolutionOptions options)
    {
        ArgumentNullException.ThrowIfNull(connectionInfo);

        List<SshCredentialDiagnostic> diagnostics = [];

        string username = connectionInfo.Username ?? string.Empty;
        string domain = connectionInfo.Domain ?? string.Empty;
        string password = connectionInfo.Password ?? string.Empty;
        string privateKey = string.Empty;

        ExternalCredentialProvider provenance = ExternalCredentialProvider.None;

        // Only Delinea and Passwordstate materialise returned key material today; the 1Password
        // and PasswordSafe branches read a private key into the same local and then never use
        // it, because the argument builder keys off the temp file path those two never write.
        // That discard is a live bug, preserved here rather than silently fixed.
        bool keyMaterialIsUsable = false;

        SshProviderRequest request = BuildRequest(connectionInfo, username);

        // ---- 1. external credential provider ---------------------------------
        ExternalCredentialProvider configured = connectionInfo.ExternalCredentialProvider;
        if (configured != ExternalCredentialProvider.None &&
            _providers.TryGetValue(configured, out ISshCredentialProvider? provider))
        {
            SshProviderResult result = provider.Fetch(request);
            diagnostics.AddRange(result.Diagnostics);

            if (result.Succeeded)
            {
                username = result.Username;
                password = result.Password;
                privateKey = result.PrivateKey;
                provenance = configured;
                keyMaterialIsUsable = MaterialisesKeyMaterial(configured);
            }
        }

        // ---- 2. empty-username fallback --------------------------------------
        if (string.IsNullOrEmpty(username))
        {
            switch (Properties.OptionsCredentialsPage.Default.EmptyCredentials)
            {
                case "windows":
                    username = Environment.UserName;
                    break;

                case "custom" when !string.IsNullOrEmpty(Properties.OptionsCredentialsPage.Default.DefaultUsername):
                    username = Properties.OptionsCredentialsPage.Default.DefaultUsername;
                    break;

                case "custom":
                    ExternalCredentialProvider fallbackProvider =
                        Properties.OptionsCredentialsPage.Default.ExternalCredentialProviderDefault;

                    // Vault and LAPS are absent from this switch today: only the four
                    // API-key providers participate in the default-credential fallback.
                    if (ParticipatesInDefaultFallback(fallbackProvider) &&
                        _providers.TryGetValue(fallbackProvider, out ISshCredentialProvider? defaults))
                    {
                        SshProviderResult result = defaults.Fetch(request with
                        {
                            UserViaApi = Properties.OptionsCredentialsPage.Default.UserViaAPIDefault ?? string.Empty
                        });

                        diagnostics.AddRange(result.Diagnostics);

                        if (result.Succeeded)
                        {
                            username = result.Username;
                            password = result.Password;
                            privateKey = result.PrivateKey;
                            provenance = fallbackProvider;
                            keyMaterialIsUsable = MaterialisesKeyMaterial(fallbackProvider);
                        }
                    }

                    break;
            }
        }

        // ---- 3. default password when a vault supplied a key but no password ----
        // The guard is deliberately narrow and matches today's: it fires only when key
        // material was actually materialised to disk, which is Delinea/Passwordstate only.
        if (string.IsNullOrEmpty(password) && keyMaterialIsUsable && !string.IsNullOrEmpty(privateKey) &&
            string.Equals(Properties.OptionsCredentialsPage.Default.EmptyCredentials, "custom", StringComparison.Ordinal))
        {
            SymmetricEncryption.LegacyRijndaelCryptographyProvider cryptographyProvider = new();
            password = cryptographyProvider.Decrypt(
                Properties.OptionsCredentialsPage.Default.DefaultPassword, App.Runtime.EncryptionKey);
        }

        // ---- 4. key path: configured wins, then discovery ----------------------
        string? keyPath = string.IsNullOrEmpty(connectionInfo.PrivateKeyPath)
            ? null
            : connectionInfo.PrivateKeyPath;

        bool hasUsableKeyMaterial = keyMaterialIsUsable && !string.IsNullOrEmpty(privateKey);

        // Discovery runs only when nothing else can authenticate: no materialised key
        // material, no configured key path, and no secret the backend could actually use.
        // The last clause matters for OpenSSH, which cannot take a password non-interactively:
        // gating on the mere presence of a secret would strip the key it authenticates with.
        bool secretCanAuthenticate = options.BackendAcceptsSecret && !string.IsNullOrEmpty(password);

        if (!hasUsableKeyMaterial && keyPath is null && !secretCanAuthenticate)
            keyPath = _keyLocator.Locate(options.DefaultKeyDiscovery);

        // ---- 5. SSH agent identities ------------------------------------------
        // Additive, not exclusive. An agent holding some key says nothing about whether it
        // holds THIS connection's key, so agent identities are offered alongside the other
        // sources rather than suppressing them — see design.md D5.
        IReadOnlyList<SshAgentIdentity> agentIdentities = [];
        if (options.ConsultAgent && _agentProvider is not null)
            agentIdentities = _agentProvider.GetIdentities(Agent.SshAgentQuery.Default);

        // ---- 6. domain qualification ------------------------------------------
        string effectiveUsername = QualifyWithDomain(username, domain);

        return new ResolvedSshCredential(
            effectiveUsername,
            unqualifiedUsername: username,
            secret: password,
            keyMaterial: hasUsableKeyMaterial ? privateKey : null,
            privateKeyPath: keyPath,
            agentIdentities: agentIdentities,
            provenance: provenance,
            diagnostics: diagnostics);
    }

    /// <summary>
    /// Prepends the domain unless the username is already qualified. Public so the OpenSSH and
    /// SSH.NET adapters cannot drift from the PuTTY behaviour.
    /// </summary>
    internal static string QualifyWithDomain(string username, string domain)
    {
        if (string.IsNullOrEmpty(username))
            return string.Empty;

        if (string.IsNullOrEmpty(domain))
            return username;

        if (username.Contains('\\') || username.Contains('@'))
            return username;

        return domain + @"\" + username;
    }

    private static SshProviderRequest BuildRequest(ConnectionInfo info, string username) =>
        new(
            UserViaApi: info.UserViaAPI ?? string.Empty,
            Username: username,
            Hostname: info.Hostname ?? string.Empty,
            VaultMount: info.VaultOpenbaoMount ?? string.Empty,
            VaultRole: info.VaultOpenbaoRole ?? string.Empty,
            VaultSecretEngine: info.VaultOpenbaoSecretEngine);

    private static bool MaterialisesKeyMaterial(ExternalCredentialProvider provider) =>
        provider is ExternalCredentialProvider.DelineaSecretServer
            or ExternalCredentialProvider.ClickstudiosPasswordState;

    private static bool ParticipatesInDefaultFallback(ExternalCredentialProvider provider) =>
        provider is ExternalCredentialProvider.DelineaSecretServer
            or ExternalCredentialProvider.ClickstudiosPasswordState
            or ExternalCredentialProvider.OnePassword
            or ExternalCredentialProvider.PasswordSafe;
}