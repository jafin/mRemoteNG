using System;
using System.Collections.Generic;
using System.Linq;
using mRemoteNG.Connection;
using mRemoteNG.Connection.Protocol;
using mRemoteNG.Security.Ssh;
using mRemoteNG.Security.Ssh.Providers;
using NUnit.Framework;

namespace mRemoteNGTests.Security.Ssh;

/// <summary>
/// Covers <c>specs/ssh-credential-resolution/spec.md</c>, and absorbs the coverage originally
/// scoped to task 1.2 (per design.md D8): provider selection is asserted here against
/// <see cref="ISshCredentialProvider"/> rather than inferred from PuTTY's command line.
/// </summary>
[TestFixture]
[NonParallelizable]
public class SshCredentialResolverTests
{
    private string _originalEmptyCredentials = null!;
    private string _originalDefaultUsername = null!;
    private string _originalUserViaApiDefault = null!;
    private ExternalCredentialProvider _originalProviderDefault;

    [SetUp]
    public void Setup()
    {
        var settings = mRemoteNG.Properties.OptionsCredentialsPage.Default;
        _originalEmptyCredentials = settings.EmptyCredentials;
        _originalDefaultUsername = settings.DefaultUsername;
        _originalUserViaApiDefault = settings.UserViaAPIDefault;
        _originalProviderDefault = settings.ExternalCredentialProviderDefault;

        settings.EmptyCredentials = "noinfo";
    }

    [TearDown]
    public void TearDown()
    {
        var settings = mRemoteNG.Properties.OptionsCredentialsPage.Default;
        settings.EmptyCredentials = _originalEmptyCredentials;
        settings.DefaultUsername = _originalDefaultUsername;
        settings.UserViaAPIDefault = _originalUserViaApiDefault;
        settings.ExternalCredentialProviderDefault = _originalProviderDefault;
    }

    private static ConnectionInfo Connection(
        string username = "alice",
        string password = "secret123",
        string domain = "",
        string hostname = "example-host",
        string privateKeyPath = "",
        ExternalCredentialProvider provider = ExternalCredentialProvider.None) =>
        new()
        {
            Protocol = ProtocolType.SSH2,
            Hostname = hostname,
            Username = username,
            Password = password,
            Domain = domain,
            PrivateKeyPath = privateKeyPath,
            ExternalCredentialProvider = provider
        };

    private static SshCredentialResolver Resolver(
        IEnumerable<ISshCredentialProvider>? providers = null,
        string? discoverableKey = null) =>
        new(providers ?? [], new StubKeyLocator(discoverableKey));

    // ---- provider supplies a password ---------------------------------------

    [Test]
    public void ProviderSuppliedPasswordReplacesTheConnectionPassword()
    {
        FakeProvider laps = FakeProvider.Returning(
            ExternalCredentialProvider.LAPS, username: "Administrator", password: "laps-pw");

        using ResolvedSshCredential credential = Resolver([laps]).Resolve(
            Connection(provider: ExternalCredentialProvider.LAPS),
            SshCredentialResolutionOptions.ForPutty);

        Assert.Multiple(() =>
        {
            Assert.That(credential.RevealSecret(), Is.EqualTo("laps-pw"));
            Assert.That(credential.EffectiveUsername, Is.EqualTo("Administrator"));
            Assert.That(credential.Provenance, Is.EqualTo(ExternalCredentialProvider.LAPS));
        });
    }

    // ---- 1.2 absorbed: one case per provider --------------------------------

    [TestCase(ExternalCredentialProvider.DelineaSecretServer)]
    [TestCase(ExternalCredentialProvider.ClickstudiosPasswordState)]
    [TestCase(ExternalCredentialProvider.OnePassword)]
    [TestCase(ExternalCredentialProvider.PasswordSafe)]
    [TestCase(ExternalCredentialProvider.VaultOpenbao)]
    [TestCase(ExternalCredentialProvider.LAPS)]
    public void TheConfiguredProviderIsTheOneConsulted(ExternalCredentialProvider configured)
    {
        List<ISshCredentialProvider> all =
        [
            FakeProvider.Returning(ExternalCredentialProvider.DelineaSecretServer, password: "delinea"),
            FakeProvider.Returning(ExternalCredentialProvider.ClickstudiosPasswordState, password: "passwordstate"),
            FakeProvider.Returning(ExternalCredentialProvider.OnePassword, password: "onepassword"),
            FakeProvider.Returning(ExternalCredentialProvider.PasswordSafe, password: "passwordsafe"),
            FakeProvider.Returning(ExternalCredentialProvider.VaultOpenbao, password: "vault"),
            FakeProvider.Returning(ExternalCredentialProvider.LAPS, password: "laps"),
        ];

        using ResolvedSshCredential credential = Resolver(all).Resolve(
            Connection(provider: configured), SshCredentialResolutionOptions.ForPutty);

        Assert.Multiple(() =>
        {
            Assert.That(credential.Provenance, Is.EqualTo(configured));
            foreach (FakeProvider p in all.Cast<FakeProvider>())
                Assert.That(p.CallCount, Is.EqualTo(p.Kind == configured ? 1 : 0),
                    $"{p.Kind} call count");
        });
    }

    [Test]
    public void NoProviderIsConsultedWhenTheConnectionUsesInlineCredentials()
    {
        FakeProvider laps = FakeProvider.Returning(ExternalCredentialProvider.LAPS, password: "laps");

        using ResolvedSshCredential credential = Resolver([laps]).Resolve(
            Connection(), SshCredentialResolutionOptions.ForPutty);

        Assert.Multiple(() =>
        {
            Assert.That(laps.CallCount, Is.Zero);
            Assert.That(credential.RevealSecret(), Is.EqualTo("secret123"));
            Assert.That(credential.Provenance, Is.EqualTo(ExternalCredentialProvider.None));
        });
    }

    // ---- provider fails ------------------------------------------------------

    [Test]
    public void AFailedProviderLeavesTheConfiguredCredentialsIntact()
    {
        FakeProvider failing = FakeProvider.Failing(
            ExternalCredentialProvider.DelineaSecretServer,
            new SshCredentialDiagnostic(ExternalCredentialProvider.DelineaSecretServer,
                SshCredentialDiagnosticSeverity.ProtocolError,
                "Secret Server Interface Error: boom"));

        using ResolvedSshCredential credential = Resolver([failing]).Resolve(
            Connection(provider: ExternalCredentialProvider.DelineaSecretServer),
            SshCredentialResolutionOptions.ForPutty);

        Assert.Multiple(() =>
        {
            Assert.That(credential.EffectiveUsername, Is.EqualTo("alice"),
                "A failed provider must not wipe the configured username.");
            Assert.That(credential.RevealSecret(), Is.EqualTo("secret123"),
                "A failed provider must not wipe the configured password.");
            Assert.That(credential.Provenance, Is.EqualTo(ExternalCredentialProvider.None));
        });
    }

    [Test]
    public void AFailedProviderDoesNotThrowAndSurfacesItsDiagnostic()
    {
        FakeProvider failing = FakeProvider.Failing(
            ExternalCredentialProvider.LAPS,
            new SshCredentialDiagnostic(ExternalCredentialProvider.LAPS,
                SshCredentialDiagnosticSeverity.Error, "LAPS query failed"));

        using ResolvedSshCredential credential = Resolver([failing]).Resolve(
            Connection(provider: ExternalCredentialProvider.LAPS),
            SshCredentialResolutionOptions.ForPutty);

        Assert.Multiple(() =>
        {
            Assert.That(credential.Diagnostics, Has.Count.EqualTo(1));
            Assert.That(credential.Diagnostics[0].Provider, Is.EqualTo(ExternalCredentialProvider.LAPS));
            Assert.That(credential.Diagnostics[0].Severity,
                Is.EqualTo(SshCredentialDiagnosticSeverity.Error));
        });
    }

    [Test]
    public void ASuccessfulProviderMayLegitimatelyReturnEmptyCredentials()
    {
        FakeProvider empty = FakeProvider.Returning(
            ExternalCredentialProvider.LAPS, username: "", password: "");

        using ResolvedSshCredential credential = Resolver([empty]).Resolve(
            Connection(provider: ExternalCredentialProvider.LAPS),
            SshCredentialResolutionOptions.ForPutty);

        Assert.That(credential.HasSecret, Is.False,
            "Success overwrites unconditionally, even with empty values.");
    }

    // ---- provider supplies a private key -------------------------------------

    [TestCase(ExternalCredentialProvider.DelineaSecretServer)]
    [TestCase(ExternalCredentialProvider.ClickstudiosPasswordState)]
    public void KeyMaterialIsCarriedForProvidersThatMaterialiseIt(ExternalCredentialProvider kind)
    {
        FakeProvider p = FakeProvider.Returning(kind, password: "", privateKey: "PRIVATE-KEY-BODY");

        using ResolvedSshCredential credential = Resolver([p]).Resolve(
            Connection(provider: kind), SshCredentialResolutionOptions.ForPutty);

        Assert.Multiple(() =>
        {
            Assert.That(credential.HasKeyMaterial, Is.True);
            Assert.That(credential.RevealKeyMaterial(), Is.EqualTo("PRIVATE-KEY-BODY"));
        });
    }

    [TestCase(ExternalCredentialProvider.OnePassword)]
    [TestCase(ExternalCredentialProvider.PasswordSafe)]
    public void KeyMaterialIsDiscardedForProvidersThatNeverMaterialisedIt(ExternalCredentialProvider kind)
    {
        // Preserves a live bug: these two branches read a private key into the same local and
        // then never use it, because the argument builder keys off the temp file path that
        // only Delinea and Passwordstate write. Pinned so the refactor cannot change it by
        // accident; fixing it is a separate change.
        FakeProvider p = FakeProvider.Returning(kind, password: "", privateKey: "PRIVATE-KEY-BODY");

        using ResolvedSshCredential credential = Resolver([p]).Resolve(
            Connection(provider: kind), SshCredentialResolutionOptions.ForPutty);

        Assert.That(credential.HasKeyMaterial, Is.False);
    }

    // ---- username fallback ----------------------------------------------------

    [Test]
    public void EmptyUsernameFallsBackToTheWindowsUser()
    {
        mRemoteNG.Properties.OptionsCredentialsPage.Default.EmptyCredentials = "windows";

        using ResolvedSshCredential credential = Resolver().Resolve(
            Connection(username: ""), SshCredentialResolutionOptions.ForPutty);

        Assert.That(credential.EffectiveUsername, Is.EqualTo(Environment.UserName));
    }

    [Test]
    public void EmptyUsernameFallsBackToTheConfiguredDefaultUsername()
    {
        var settings = mRemoteNG.Properties.OptionsCredentialsPage.Default;
        settings.EmptyCredentials = "custom";
        settings.DefaultUsername = "svc-default";

        using ResolvedSshCredential credential = Resolver().Resolve(
            Connection(username: ""), SshCredentialResolutionOptions.ForPutty);

        Assert.That(credential.EffectiveUsername, Is.EqualTo("svc-default"));
    }

    [Test]
    public void EmptyUsernameUnderNoinfoStaysEmpty()
    {
        using ResolvedSshCredential credential = Resolver().Resolve(
            Connection(username: ""), SshCredentialResolutionOptions.ForPutty);

        Assert.That(credential.EffectiveUsername, Is.Empty);
    }

    [Test]
    public void CustomFallbackConsultsTheDefaultProviderWhenNoDefaultUsernameIsSet()
    {
        var settings = mRemoteNG.Properties.OptionsCredentialsPage.Default;
        settings.EmptyCredentials = "custom";
        settings.DefaultUsername = "";
        settings.UserViaAPIDefault = "vault-key";
        settings.ExternalCredentialProviderDefault = ExternalCredentialProvider.OnePassword;

        FakeProvider op = FakeProvider.Returning(
            ExternalCredentialProvider.OnePassword, username: "op-user", password: "op-pw");

        using ResolvedSshCredential credential = Resolver([op]).Resolve(
            Connection(username: ""), SshCredentialResolutionOptions.ForPutty);

        Assert.Multiple(() =>
        {
            Assert.That(credential.EffectiveUsername, Is.EqualTo("op-user"));
            Assert.That(credential.RevealSecret(), Is.EqualTo("op-pw"));
            Assert.That(op.LastRequest.UserViaApi, Is.EqualTo("vault-key"),
                "The default fallback must use UserViaAPIDefault, not the connection's key.");
        });
    }

    [TestCase(ExternalCredentialProvider.VaultOpenbao)]
    [TestCase(ExternalCredentialProvider.LAPS)]
    public void VaultAndLapsDoNotParticipateInTheDefaultCredentialFallback(ExternalCredentialProvider kind)
    {
        var settings = mRemoteNG.Properties.OptionsCredentialsPage.Default;
        settings.EmptyCredentials = "custom";
        settings.DefaultUsername = "";
        settings.ExternalCredentialProviderDefault = kind;

        FakeProvider p = FakeProvider.Returning(kind, username: "should-not-be-used");

        using ResolvedSshCredential credential = Resolver([p]).Resolve(
            Connection(username: ""), SshCredentialResolutionOptions.ForPutty);

        Assert.Multiple(() =>
        {
            Assert.That(p.CallCount, Is.Zero);
            Assert.That(credential.EffectiveUsername, Is.Empty);
        });
    }

    // ---- domain qualification --------------------------------------------------

    [TestCase("alice", "CORP", @"CORP\alice")]
    [TestCase("alice", "", "alice")]
    [TestCase(@"OTHER\alice", "CORP", @"OTHER\alice")]
    [TestCase("alice@other.example", "CORP", "alice@other.example")]
    [TestCase("", "CORP", "")]
    public void DomainQualificationMatchesThePuttyBehaviour(string username, string domain, string expected)
    {
        using ResolvedSshCredential credential = Resolver().Resolve(
            Connection(username: username, domain: domain), SshCredentialResolutionOptions.ForPutty);

        Assert.That(credential.EffectiveUsername, Is.EqualTo(expected));
    }

    [Test]
    public void DomainQualificationAppliesToAProviderSuppliedUsername()
    {
        FakeProvider laps = FakeProvider.Returning(
            ExternalCredentialProvider.LAPS, username: "Administrator", password: "pw");

        using ResolvedSshCredential credential = Resolver([laps]).Resolve(
            Connection(domain: "CORP", provider: ExternalCredentialProvider.LAPS),
            SshCredentialResolutionOptions.ForPutty);

        Assert.That(credential.EffectiveUsername, Is.EqualTo(@"CORP\Administrator"));
    }

    // ---- default key discovery --------------------------------------------------

    [Test]
    public void DiscoveryRunsOnlyWhenThereIsNoPasswordAndNoConfiguredKey()
    {
        using ResolvedSshCredential credential = Resolver(discoverableKey: @"C:\keys\id_ed25519.ppk")
            .Resolve(Connection(password: ""), SshCredentialResolutionOptions.ForPutty);

        Assert.That(credential.PrivateKeyPath, Is.EqualTo(@"C:\keys\id_ed25519.ppk"));
    }

    [Test]
    public void DiscoveryIsSkippedWhenAPasswordIsPresent()
    {
        using ResolvedSshCredential credential = Resolver(discoverableKey: @"C:\keys\id_ed25519.ppk")
            .Resolve(Connection(), SshCredentialResolutionOptions.ForPutty);

        Assert.That(credential.PrivateKeyPath, Is.Null);
    }

    [Test]
    public void AConfiguredKeyPathBeatsDiscovery()
    {
        using ResolvedSshCredential credential = Resolver(discoverableKey: @"C:\keys\discovered.ppk")
            .Resolve(Connection(password: "", privateKeyPath: @"C:\keys\configured.ppk"),
                SshCredentialResolutionOptions.ForPutty);

        Assert.That(credential.PrivateKeyPath, Is.EqualTo(@"C:\keys\configured.ppk"));
    }

    [Test]
    public void MaterialisedKeyMaterialSuppressesDiscovery()
    {
        FakeProvider delinea = FakeProvider.Returning(
            ExternalCredentialProvider.DelineaSecretServer, password: "", privateKey: "KEY");

        using ResolvedSshCredential credential = Resolver([delinea], discoverableKey: @"C:\keys\discovered.ppk")
            .Resolve(Connection(password: "", provider: ExternalCredentialProvider.DelineaSecretServer),
                SshCredentialResolutionOptions.ForPutty);

        Assert.Multiple(() =>
        {
            Assert.That(credential.HasKeyMaterial, Is.True);
            Assert.That(credential.PrivateKeyPath, Is.Null);
        });
    }

    [Test]
    public void DiscoveryStillRunsWithAPasswordWhenTheBackendCannotUseOne()
    {
        // ssh.exe has no way to accept a password non-interactively, so a stored password is
        // not an alternative to a key. Gating discovery on the mere presence of a secret would
        // strip the key an OpenSSH connection authenticates with today.
        using ResolvedSshCredential credential = Resolver(discoverableKey: @"C:\keys\id_ed25519")
            .Resolve(Connection(password: "secret123"), SshCredentialResolutionOptions.ForOpenSsh);

        Assert.That(credential.PrivateKeyPath, Is.EqualTo(@"C:\keys\id_ed25519"));
    }

    [Test]
    public void DiscoveryIsStillSkippedWithAPasswordWhenTheBackendCanUseOne()
    {
        using ResolvedSshCredential credential = Resolver(discoverableKey: @"C:\keys\id_ed25519.ppk")
            .Resolve(Connection(password: "secret123"), SshCredentialResolutionOptions.ForPutty);

        Assert.That(credential.PrivateKeyPath, Is.Null);
    }

    [Test]
    public void DiscoveryModeNoneFindsNothing()
    {
        using ResolvedSshCredential credential = Resolver(discoverableKey: @"C:\keys\id_ed25519.ppk")
            .Resolve(Connection(password: ""), SshCredentialResolutionOptions.None);

        Assert.That(credential.PrivateKeyPath, Is.Null);
    }

    [Test]
    public void TheBackendChoosesWhichKeyFormatIsDiscovered()
    {
        RecordingKeyLocator locator = new();
        SshCredentialResolver resolver = new([], locator);

        resolver.Resolve(Connection(password: ""), SshCredentialResolutionOptions.ForPutty).Dispose();
        resolver.Resolve(Connection(password: ""), SshCredentialResolutionOptions.ForOpenSsh).Dispose();

        Assert.That(locator.Modes, Is.EqualTo(new[]
        {
            DefaultKeyDiscoveryMode.PuttyPpk,
            DefaultKeyDiscoveryMode.OpenSsh
        }));
    }

    // ---- default-password guard --------------------------------------------------

    [Test]
    public void TheDefaultPasswordIsNotAppliedWhenTheProviderDidNotMaterialiseAKey()
    {
        var settings = mRemoteNG.Properties.OptionsCredentialsPage.Default;
        settings.EmptyCredentials = "custom";
        settings.DefaultUsername = "svc";

        FakeProvider op = FakeProvider.Returning(
            ExternalCredentialProvider.OnePassword, username: "u", password: "", privateKey: "KEY");

        using ResolvedSshCredential credential = Resolver([op]).Resolve(
            Connection(provider: ExternalCredentialProvider.OnePassword),
            SshCredentialResolutionOptions.ForPutty);

        Assert.That(credential.HasSecret, Is.False,
            "The stored default password only applies when key material was materialised.");
    }

    [Test]
    public void TheDefaultPasswordIsNotAppliedUnderNoinfo()
    {
        FakeProvider delinea = FakeProvider.Returning(
            ExternalCredentialProvider.DelineaSecretServer, username: "u", password: "", privateKey: "KEY");

        using ResolvedSshCredential credential = Resolver([delinea]).Resolve(
            Connection(provider: ExternalCredentialProvider.DelineaSecretServer),
            SshCredentialResolutionOptions.ForPutty);

        Assert.That(credential.HasSecret, Is.False);
    }

    // ---- re-invocable contract (D7) -------------------------------------------------

    [Test]
    public void EveryResolveCallReconsultsTheProvider()
    {
        // Vault SSH-OTP mints single-use credentials, so caching would authenticate the first
        // connection and fail every one after it.
        FakeProvider vault = FakeProvider.Sequence(
            ExternalCredentialProvider.VaultOpenbao, "otp-1", "otp-2", "otp-3");

        SshCredentialResolver resolver = Resolver([vault]);
        ConnectionInfo info = Connection(provider: ExternalCredentialProvider.VaultOpenbao);

        using ResolvedSshCredential first = resolver.Resolve(info, SshCredentialResolutionOptions.ForPutty);
        using ResolvedSshCredential second = resolver.Resolve(info, SshCredentialResolutionOptions.ForPutty);
        using ResolvedSshCredential third = resolver.Resolve(info, SshCredentialResolutionOptions.ForPutty);

        Assert.Multiple(() =>
        {
            Assert.That(vault.CallCount, Is.EqualTo(3));
            Assert.That(first.RevealSecret(), Is.EqualTo("otp-1"));
            Assert.That(second.RevealSecret(), Is.EqualTo("otp-2"));
            Assert.That(third.RevealSecret(), Is.EqualTo("otp-3"));
        });
    }

    // ---- agent identities (task 7.1) ------------------------------------------------

    private static readonly SshAgentIdentity AgentKey = new("alice@laptop", "ssh-ed25519");

    [Test]
    public void AgentIdentitiesAreCarriedWhenTheBackendConsultsTheAgent()
    {
        SshCredentialResolver resolver = new([], new StubKeyLocator(null), new StubAgentProvider([AgentKey]));

        using ResolvedSshCredential credential = resolver.Resolve(
            Connection(), SshCredentialResolutionOptions.ForSshNet(agentEnabled: true));

        Assert.Multiple(() =>
        {
            Assert.That(credential.HasAgentIdentities, Is.True);
            Assert.That(credential.AgentIdentities, Is.EqualTo(new[] { AgentKey }));
        });
    }

    [Test]
    public void TheAgentIsNotContactedWhenTheSettingIsOff()
    {
        StubAgentProvider agent = new([AgentKey]);
        SshCredentialResolver resolver = new([], new StubKeyLocator(null), agent);

        using ResolvedSshCredential credential = resolver.Resolve(
            Connection(), SshCredentialResolutionOptions.ForSshNet(agentEnabled: false));

        Assert.Multiple(() =>
        {
            Assert.That(agent.CallCount, Is.Zero);
            Assert.That(credential.HasAgentIdentities, Is.False);
        });
    }

    [TestCase(false)]
    public void BackendsWithANativeAgentNeverContactTheProvider(bool _)
    {
        StubAgentProvider agent = new([AgentKey]);
        SshCredentialResolver resolver = new([], new StubKeyLocator(null), agent);

        resolver.Resolve(Connection(), SshCredentialResolutionOptions.ForPutty).Dispose();
        resolver.Resolve(Connection(), SshCredentialResolutionOptions.ForOpenSsh).Dispose();

        Assert.That(agent.CallCount, Is.Zero,
            "PuTTY uses Pageant natively and ssh.exe uses the Windows agent natively.");
    }

    [Test]
    public void AResolverWithNoAgentProviderStillResolves()
    {
        SshCredentialResolver resolver = new([], new StubKeyLocator(null));

        using ResolvedSshCredential credential = resolver.Resolve(
            Connection(), SshCredentialResolutionOptions.ForSshNet(agentEnabled: true));

        Assert.That(credential.HasAgentIdentities, Is.False);
    }

    [Test]
    public void AgentIdentitiesDoNotSuppressProviderKeyMaterial()
    {
        // An agent holding SOME key says nothing about whether it holds THIS connection's key.
        // Suppressing the vault-supplied key because an unrelated agent identity exists would
        // break authentication outright. Agent identities are additive - design.md D5.
        FakeProvider delinea = FakeProvider.Returning(
            ExternalCredentialProvider.DelineaSecretServer, password: "", privateKey: "VAULT-KEY");

        SshCredentialResolver resolver = new(
            [delinea], new StubKeyLocator(null), new StubAgentProvider([AgentKey]));

        using ResolvedSshCredential credential = resolver.Resolve(
            Connection(password: "", provider: ExternalCredentialProvider.DelineaSecretServer),
            SshCredentialResolutionOptions.ForSshNet(agentEnabled: true));

        Assert.Multiple(() =>
        {
            Assert.That(credential.HasAgentIdentities, Is.True);
            Assert.That(credential.HasKeyMaterial, Is.True, "The vault key must still be offered.");
            Assert.That(credential.RevealKeyMaterial(), Is.EqualTo("VAULT-KEY"));
        });
    }

    [Test]
    public void AgentIdentitiesDoNotSuppressAConfiguredKeyPath()
    {
        SshCredentialResolver resolver = new(
            [], new StubKeyLocator(null), new StubAgentProvider([AgentKey]));

        using ResolvedSshCredential credential = resolver.Resolve(
            Connection(password: "", privateKeyPath: @"C:\keys\configured.ppk"),
            SshCredentialResolutionOptions.ForSshNet(agentEnabled: true));

        Assert.Multiple(() =>
        {
            Assert.That(credential.HasAgentIdentities, Is.True);
            Assert.That(credential.PrivateKeyPath, Is.EqualTo(@"C:\keys\configured.ppk"));
        });
    }

    // ---- argument validation ------------------------------------------------------

    [Test]
    public void ANullConnectionIsRejected()
    {
        Assert.Throws<ArgumentNullException>(
            () => Resolver().Resolve(null!, SshCredentialResolutionOptions.ForPutty));
    }

    // ---- fakes --------------------------------------------------------------------

    private sealed class FakeProvider : ISshCredentialProvider
    {
        private readonly Queue<string>? _passwordSequence;
        private SshProviderResult _result = SshProviderResult.Empty;

        private FakeProvider(ExternalCredentialProvider kind, Queue<string>? passwordSequence = null)
        {
            Kind = kind;
            _passwordSequence = passwordSequence;
        }

        public ExternalCredentialProvider Kind { get; }
        public int CallCount { get; private set; }
        public SshProviderRequest LastRequest { get; private set; }

        public static FakeProvider Returning(
            ExternalCredentialProvider kind,
            string username = "provider-user",
            string password = "provider-pw",
            string privateKey = "") =>
            new(kind) { _result = new SshProviderResult(username, password, privateKey, []) };

        public static FakeProvider Failing(ExternalCredentialProvider kind, SshCredentialDiagnostic diagnostic) =>
            new(kind) { _result = SshProviderResult.Failed(diagnostic) };

        public static FakeProvider Sequence(ExternalCredentialProvider kind, params string[] passwords) =>
            new(kind, new Queue<string>(passwords));

        public SshProviderResult Fetch(SshProviderRequest request)
        {
            CallCount++;
            LastRequest = request;

            if (_passwordSequence is not null)
                return new SshProviderResult("provider-user", _passwordSequence.Dequeue(), string.Empty, []);

            return _result;
        }
    }

    private sealed class StubKeyLocator(string? key) : IDefaultSshKeyLocator
    {
        public string? Locate(DefaultKeyDiscoveryMode mode) =>
            mode == DefaultKeyDiscoveryMode.None ? null : key;
    }

    private sealed class StubAgentProvider(IReadOnlyList<SshAgentIdentity> identities)
        : mRemoteNG.Security.Ssh.Agent.ISshAgentProvider
    {
        public int CallCount { get; private set; }

        public IReadOnlyList<SshAgentIdentity> GetIdentities(
            mRemoteNG.Security.Ssh.Agent.SshAgentQuery query)
        {
            CallCount++;
            return identities;
        }
    }

    private sealed class RecordingKeyLocator : IDefaultSshKeyLocator
    {
        public List<DefaultKeyDiscoveryMode> Modes { get; } = [];

        public string? Locate(DefaultKeyDiscoveryMode mode)
        {
            Modes.Add(mode);
            return null;
        }
    }
}