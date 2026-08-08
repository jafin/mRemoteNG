using System;
using System.Threading;
using mRemoteNG.Connection;
using mRemoteNG.Connection.Protocol;
using mRemoteNG.Connection.Protocol.SSH;
using mRemoteNG.UI.Tabs;
using NUnit.Framework;

namespace mRemoteNGTests.Connection.Protocol
{
    /// <summary>
    /// Characterization tests for the credential portion of the command line that
    /// <see cref="PuttyBase.Connect"/> hands to PuTTY.
    ///
    /// These pin CURRENT behaviour ahead of the credential-resolver extraction
    /// (openspec change add-ssh-agent-credential-resolver, tasks 3.x-4.x). After that
    /// refactor these tests must still pass UNMODIFIED — an edit needed here is a
    /// behaviour regression, not a test that needs updating.
    /// </summary>
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    [NonParallelizable]
    public class PuttyBaseCredentialArgumentTests
    {
        private const string PipeArgument = @"\\.\PIPE\mRemoteNGSecretPipe-test";

        private TestableSsh2Protocol _protocol = null!;
        private ConnectionTab _connectionTab = null!;
        private InterfaceControl _interfaceControl = null!;
        private ConnectionInfo _connectionInfo = null!;

        private string? _originalPuttyPath;
        private string _originalEmptyCredentials = null!;
        private string _originalDefaultUsername = null!;

        [SetUp]
        public void Setup()
        {
            _originalPuttyPath = PuttyBase.PuttyPath;
            _originalEmptyCredentials = mRemoteNG.Properties.OptionsCredentialsPage.Default.EmptyCredentials;
            _originalDefaultUsername = mRemoteNG.Properties.OptionsCredentialsPage.Default.DefaultUsername;

            // cmd.exe stands in for putty.exe: it satisfies PathValidator and starts cleanly.
            // The arguments are captured before Process.Start, so what cmd.exe does with them
            // is irrelevant to these assertions.
            PuttyBase.PuttyPath = "cmd.exe";

            _protocol = new TestableSsh2Protocol();
            _connectionTab = new ConnectionTab();
            _connectionInfo = new ConnectionInfo
            {
                Protocol = ProtocolType.SSH2,
                Name = "Test Connection",
                Hostname = "example-host",
                Port = 22,
                Username = "alice",
                Password = "secret123",
                Domain = string.Empty,
                PrivateKeyPath = string.Empty
            };

            _interfaceControl = new InterfaceControl(_connectionTab, _protocol, _connectionInfo);
            _protocol.InterfaceControl = _interfaceControl;
        }

        [TearDown]
        public void TearDown()
        {
            _protocol?.Close();
            _interfaceControl?.Dispose();
            _connectionTab?.Dispose();

            PuttyBase.PuttyPath = _originalPuttyPath;
            mRemoteNG.Properties.OptionsCredentialsPage.Default.EmptyCredentials = _originalEmptyCredentials;
            mRemoteNG.Properties.OptionsCredentialsPage.Default.DefaultUsername = _originalDefaultUsername;
        }

        private string Connect()
        {
            _protocol.Connect();
            Assert.That(_protocol.BuiltArguments, Is.Not.Null,
                        "Connect() did not reach argument construction.");
            return _protocol.BuiltArguments!;
        }

        // ---- 1.1 plain password -------------------------------------------------

        [Test]
        public void PasswordIsPassedViaNamedPipeOnModernPutty()
        {
            _protocol.PuttyVersion = new Version(0, 81);

            string args = Connect();

            Assert.Multiple(() =>
            {
                Assert.That(args, Does.Contain("-pwfile " + PipeArgument));
                Assert.That(args, Does.Not.Contain("-pw secret123"),
                            "The password must never appear in plaintext on a 0.81+ command line.");
                Assert.That(_protocol.PipedPassword, Is.EqualTo("secret123"));
            });
        }

        // ---- 1.3 the -pwfile / -pw version split --------------------------------

        [Test]
        public void PasswordIsPassedInlineOnLegacyPutty()
        {
            _protocol.PuttyVersion = new Version(0, 80);

            string args = Connect();

            Assert.Multiple(() =>
            {
                Assert.That(args, Does.Contain("-pw secret123"));
                Assert.That(args, Does.Not.Contain("-pwfile"));
                Assert.That(_protocol.PipedPassword, Is.Null, "No pipe should be created below 0.81.");
            });
        }

        [Test]
        public void ModernPuttyBoundaryUsesPipeAtExactly081()
        {
            _protocol.PuttyVersion = new Version(0, 81, 0);

            Assert.That(Connect(), Does.Contain("-pwfile"));
        }

        // ---- 1.1 no password ----------------------------------------------------

        [Test]
        public void NoPasswordEmitsNeitherPwNorPwfile()
        {
            _connectionInfo.Password = string.Empty;

            string args = Connect();

            Assert.Multiple(() =>
            {
                Assert.That(args, Does.Not.Contain("-pw "));
                Assert.That(args, Does.Not.Contain("-pwfile"));
                Assert.That(_protocol.PipedPassword, Is.Null);
            });
        }

        // ---- 1.1 username and domain qualification ------------------------------

        [Test]
        public void UsernameIsPassedWithLoginFlag()
        {
            Assert.That(Connect(), Does.Contain("-l alice"));
        }

        [Test]
        public void DomainQualifiesAnUnqualifiedUsername()
        {
            _connectionInfo.Domain = "CORP";

            Assert.That(Connect(), Does.Contain(@"-l CORP\alice"));
        }

        [Test]
        public void DomainDoesNotRequalifyAUsernameContainingABackslash()
        {
            _connectionInfo.Domain = "CORP";
            _connectionInfo.Username = @"OTHER\alice";

            Assert.That(Connect(), Does.Contain(@"-l OTHER\alice"));
        }

        [Test]
        public void DomainDoesNotRequalifyAUsernameContainingAnAtSign()
        {
            _connectionInfo.Domain = "CORP";
            _connectionInfo.Username = "alice@other.example";

            Assert.That(Connect(), Does.Contain("-l alice@other.example"));
        }

        // ---- 1.1 configured private key -----------------------------------------

        [Test]
        public void ConfiguredPrivateKeyPathIsPassedWithIdentityFlag()
        {
            _connectionInfo.PrivateKeyPath = @"C:\keys\id_ed25519.ppk";

            string args = Connect();

            Assert.Multiple(() =>
            {
                Assert.That(args, Does.Contain(@"-i C:\keys\id_ed25519.ppk"));
                Assert.That(_protocol.DefaultKeyLookups, Is.Zero,
                            "A configured key must short-circuit default-key discovery.");
            });
        }

        [Test]
        public void ConfiguredPrivateKeyPathTakesPrecedenceOverDiscovery()
        {
            _connectionInfo.Password = string.Empty;
            _connectionInfo.PrivateKeyPath = @"C:\keys\configured.ppk";
            _protocol.DiscoverableKey = @"C:\users\alice\.ssh\id_rsa.ppk";

            string args = Connect();

            Assert.Multiple(() =>
            {
                Assert.That(args, Does.Contain(@"-i C:\keys\configured.ppk"));
                Assert.That(args, Does.Not.Contain("id_rsa.ppk"));
            });
        }

        // ---- 1.1 discovered default key -----------------------------------------

        [Test]
        public void DefaultKeyIsDiscoveredOnlyWhenThereIsNoPasswordAndNoConfiguredKey()
        {
            _connectionInfo.Password = string.Empty;
            _protocol.DiscoverableKey = @"C:\users\alice\.ssh\id_ed25519.ppk";

            Assert.That(Connect(), Does.Contain(@"-i C:\users\alice\.ssh\id_ed25519.ppk"));
        }

        [Test]
        public void DefaultKeyIsNotDiscoveredWhenAPasswordIsSet()
        {
            _protocol.DiscoverableKey = @"C:\users\alice\.ssh\id_ed25519.ppk";

            string args = Connect();

            Assert.Multiple(() =>
            {
                Assert.That(args, Does.Not.Contain("-i "));
                Assert.That(_protocol.DefaultKeyLookups, Is.Zero);
            });
        }

        [Test]
        public void NoIdentityFlagWhenDiscoveryFindsNothing()
        {
            _connectionInfo.Password = string.Empty;
            _protocol.DiscoverableKey = null;

            string args = Connect();

            Assert.Multiple(() =>
            {
                Assert.That(args, Does.Not.Contain("-i "));
                Assert.That(_protocol.DefaultKeyLookups, Is.EqualTo(1));
            });
        }

        // ---- 1.1 empty-username fallback ----------------------------------------

        [Test]
        public void EmptyUsernameFallsBackToTheWindowsUser()
        {
            _connectionInfo.Username = string.Empty;
            mRemoteNG.Properties.OptionsCredentialsPage.Default.EmptyCredentials = "windows";

            Assert.That(Connect(), Does.Contain("-l " + Environment.UserName));
        }

        [Test]
        public void EmptyUsernameFallsBackToTheConfiguredDefaultUsername()
        {
            _connectionInfo.Username = string.Empty;
            mRemoteNG.Properties.OptionsCredentialsPage.Default.EmptyCredentials = "custom";
            mRemoteNG.Properties.OptionsCredentialsPage.Default.DefaultUsername = "svc-default";

            Assert.That(Connect(), Does.Contain("-l svc-default"));
        }

        [Test]
        public void EmptyUsernameEmitsNoLoginFlagUnderNoinfo()
        {
            _connectionInfo.Username = string.Empty;
            mRemoteNG.Properties.OptionsCredentialsPage.Default.EmptyCredentials = "noinfo";

            Assert.That(Connect(), Does.Not.Contain("-l "));
        }

        // ---- credential suppression ---------------------------------------------

        [Test]
        public void ForceNoCredentialsSuppressesUsernameAndPassword()
        {
            _protocol.Force = ConnectionInfo.Force.NoCredentials;

            string args = Connect();

            Assert.Multiple(() =>
            {
                Assert.That(args, Does.Not.Contain("-l "));
                Assert.That(args, Does.Not.Contain("-pw"));
                Assert.That(_protocol.PipedPassword, Is.Null);
            });
        }

        /// <summary>
        /// Pins the four environment-dependent inputs so the produced command line is
        /// deterministic: PuTTY flavour, PuTTY version, password-pipe naming, and
        /// default-key discovery. Overriding the pipe hook also keeps the real
        /// foreground pipe-server thread (30s timeout) out of the test run.
        /// </summary>
        private sealed class TestableSsh2Protocol : ProtocolSSH2
        {
            public Version PuttyVersion { get; set; } = new(0, 81);
            public string? DiscoverableKey { get; set; }
            public string? PipedPassword { get; private set; }
            public int DefaultKeyLookups { get; private set; }
            public string? BuiltArguments => LastBuiltArguments;

            protected override bool DetectIsPuttyNg() => false;

            protected override Version GetPuttyVersion() => PuttyVersion;

            protected override string CreatePasswordPipeArgument(string password)
            {
                PipedPassword = password;
                return PipeArgument;
            }

            protected override string? FindDefaultPrivateKey()
            {
                DefaultKeyLookups++;
                return DiscoverableKey;
            }

            protected override void Resize(object sender, EventArgs e)
            {
                // No native window to move in tests.
            }
        }
    }
}
