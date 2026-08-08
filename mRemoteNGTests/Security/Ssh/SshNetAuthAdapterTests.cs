using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using mRemoteNG.Connection;
using mRemoteNG.Security.Ssh;
using mRemoteNG.Security.Ssh.Adapters;
using NUnit.Framework;
using Renci.SshNet;
using Renci.SshNet.Common;

namespace mRemoteNGTests.Security.Ssh
{
    /// <summary>
    /// Covers the SSH.NET half of the "adapters translate the neutral credential" requirement in
    /// <c>specs/ssh-credential-resolution/spec.md</c>.
    /// </summary>
    [TestFixture]
    public class SshNetAuthAdapterTests
    {
        private static readonly string[] SecondFactorPrompt = ["Duo two-factor verification code: "];
        private static readonly string[] PublicKeyThenPasswordThenKeyboardInteractive =
            ["publickey", "password", "keyboard-interactive"];

        // ---- method composition ---------------------------------------------------

        [Test]
        public void KeyboardInteractiveIsAlwaysOffered()
        {
            using ResolvedSshCredential credential = new("alice");

            using SshNetAuthentication authentication = SshNetAuthAdapter.Translate(credential);

            Assert.That(authentication.Methods.Select(m => m.Name),
                        Does.Contain("keyboard-interactive"));
        }

        [Test]
        public void ACredentialWithNoSecretOffersNoPasswordMethod()
        {
            using ResolvedSshCredential credential = new("alice");

            using SshNetAuthentication authentication = SshNetAuthAdapter.Translate(credential);

            Assert.That(authentication.Methods.Select(m => m.Name), Does.Not.Contain("password"));
        }

        [Test]
        public void ASecretBecomesAPasswordMethod()
        {
            using ResolvedSshCredential credential = new("alice", secret: "secret123");

            using SshNetAuthentication authentication = SshNetAuthAdapter.Translate(credential);

            Assert.That(authentication.Methods.Select(m => m.Name), Does.Contain("password"));
        }

        [Test]
        public void PublicKeyIsOfferedBeforePasswordAndPasswordBeforeKeyboardInteractive()
        {
            using ResolvedSshCredential credential = new(
                "alice", secret: "secret123", agentIdentities: [StubIdentity()]);

            using SshNetAuthentication authentication = SshNetAuthAdapter.Translate(credential);

            Assert.That(authentication.Methods.Select(m => m.Name),
                        Is.EqualTo(PublicKeyThenPasswordThenKeyboardInteractive));
        }

        [Test]
        public void EveryMethodAuthenticatesAsTheEffectiveUsername()
        {
            using ResolvedSshCredential credential = new(
                @"CORP\alice", unqualifiedUsername: "alice", secret: "secret123");

            using SshNetAuthentication authentication = SshNetAuthAdapter.Translate(credential);

            Assert.Multiple(() =>
            {
                Assert.That(authentication.Username, Is.EqualTo(@"CORP\alice"));
                Assert.That(authentication.Methods.Select(m => m.Username),
                            Is.All.EqualTo(@"CORP\alice"));
            });
        }

        [Test]
        public void ANullCredentialIsRejected()
        {
            Assert.Throws<ArgumentNullException>(() => SshNetAuthAdapter.Translate(null!));
        }

        // ---- agent identities -----------------------------------------------------

        [Test]
        public void AgentIdentitiesBecomeASinglePublicKeyMethod()
        {
            using ResolvedSshCredential credential = new(
                "alice", agentIdentities: [StubIdentity("a"), StubIdentity("b")]);

            using SshNetAuthentication authentication = SshNetAuthAdapter.Translate(credential);

            Assert.Multiple(() =>
            {
                Assert.That(
                    authentication.Methods.Count(m => string.Equals(m.Name, "publickey", StringComparison.Ordinal)),
                    Is.EqualTo(1));
                Assert.That(authentication.Unsupported, Is.Empty);
            });
        }

        [Test]
        public void AnIdentityWithNoUsableKeyIsReportedRatherThanDropped()
        {
            // A descriptive identity with no KeyHandle — what a non-SSH.NET agent provider, or a
            // key type the agent library could not represent, would produce.
            using ResolvedSshCredential credential = new(
                "alice", agentIdentities: [new SshAgentIdentity("alice@laptop", "ssh-ed25519")]);

            using SshNetAuthentication authentication = SshNetAuthAdapter.Translate(credential);

            Assert.Multiple(() =>
            {
                Assert.That(authentication.Methods.Select(m => m.Name), Does.Not.Contain("publickey"));
                Assert.That(authentication.Unsupported, Has.Count.EqualTo(1));
                Assert.That(authentication.Unsupported[0].Message, Does.Contain("alice@laptop"));
                Assert.That(authentication.Unsupported[0].Severity,
                            Is.EqualTo(SshCredentialDiagnosticSeverity.Information));
            });
        }

        // ---- key files ------------------------------------------------------------

        [Test]
        public void AMissingKeyFileIsReportedAsAnError()
        {
            string missing = Path.Combine(Path.GetTempPath(), "mrng-no-such-key-" + Guid.NewGuid().ToString("N"));
            using ResolvedSshCredential credential = new("alice", privateKeyPath: missing);

            using SshNetAuthentication authentication = SshNetAuthAdapter.Translate(credential);

            Assert.Multiple(() =>
            {
                Assert.That(authentication.Unsupported, Has.Count.EqualTo(1));
                Assert.That(authentication.Unsupported[0].Severity,
                            Is.EqualTo(SshCredentialDiagnosticSeverity.Error));
                Assert.That(authentication.Unsupported[0].Message, Does.Contain("not found"));
            });
        }

        [Test]
        public void AnUnparseableKeyFileIsReportedRatherThanThrown()
        {
            string path = WriteTempFile("not a private key at all");

            try
            {
                using ResolvedSshCredential credential = new("alice", privateKeyPath: path);

                using SshNetAuthentication authentication = SshNetAuthAdapter.Translate(credential);

                Assert.Multiple(() =>
                {
                    Assert.That(authentication.Unsupported, Has.Count.EqualTo(1));
                    Assert.That(authentication.Unsupported[0].Message, Does.Contain("could not be loaded"));
                    Assert.That(authentication.Methods.Select(m => m.Name), Does.Not.Contain("publickey"));
                });
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Test]
        public void UnparseableKeyMaterialIsReportedRatherThanThrown()
        {
            using ResolvedSshCredential credential = new(
                "alice",
                keyMaterial: "NOT-A-KEY",
                provenance: ExternalCredentialProvider.ClickstudiosPasswordState);

            using SshNetAuthentication authentication = SshNetAuthAdapter.Translate(credential);

            Assert.Multiple(() =>
            {
                Assert.That(authentication.Unsupported, Has.Count.EqualTo(1));
                Assert.That(authentication.Unsupported[0].Message,
                            Does.Contain(nameof(ExternalCredentialProvider.ClickstudiosPasswordState)));
                Assert.That(authentication.Unsupported[0].Message, Does.Not.Contain("NOT-A-KEY"));
            });
        }

        [Test]
        public void KeyMaterialIsAttemptedRatherThanRejectedOutright()
        {
            // The point of the D3 asymmetry: ssh.exe reports provider-supplied key material as
            // unusable because it only takes a path, whereas SSH.NET can load one from memory. The
            // material here is junk, so it fails to parse — but it fails at the parser, which is
            // proof the adapter tried, rather than refusing on principle.
            using ResolvedSshCredential credential = new("alice", keyMaterial: "NOT-A-KEY");

            using SshNetAuthentication authentication = SshNetAuthAdapter.Translate(credential);

            Assert.That(authentication.Unsupported[0].Message, Does.Contain("could not be parsed"));
        }

        // ---- keyboard-interactive prompt answering ---------------------------------

        [Test]
        public void APasswordPromptIsAnsweredWithTheSecret()
        {
            using ResolvedSshCredential credential = new("alice", secret: "secret123");
            AuthenticationPrompt prompt = new(1, isEchoed: false, "Password: ");
            List<string> unanswered = [];

            SshNetAuthAdapter.AnswerPrompts(PromptEvent(prompt), credential, unanswered);

            Assert.Multiple(() =>
            {
                Assert.That(prompt.Response, Is.EqualTo("secret123"));
                Assert.That(unanswered, Is.Empty);
            });
        }

        [Test]
        public void APassphrasePromptIsAnsweredWithTheSecret()
        {
            using ResolvedSshCredential credential = new("alice", secret: "secret123");
            AuthenticationPrompt prompt = new(1, isEchoed: false, "Enter passphrase for key: ");
            List<string> unanswered = [];

            SshNetAuthAdapter.AnswerPrompts(PromptEvent(prompt), credential, unanswered);

            Assert.That(prompt.Response, Is.EqualTo("secret123"));
        }

        [Test]
        public void ASecondFactorPromptIsRecordedRatherThanAnsweredWithThePassword()
        {
            using ResolvedSshCredential credential = new("alice", secret: "secret123");
            AuthenticationPrompt prompt = new(1, isEchoed: true, "Duo two-factor verification code: ");
            List<string> unanswered = [];

            SshNetAuthAdapter.AnswerPrompts(PromptEvent(prompt), credential, unanswered);

            Assert.Multiple(() =>
            {
                Assert.That(prompt.Response, Is.Null.Or.Empty);
                Assert.That(unanswered, Is.EqualTo(SecondFactorPrompt));
            });
        }

        [Test]
        public void APasswordPromptWithNoSecretToOfferIsRecordedAsUnanswered()
        {
            using ResolvedSshCredential credential = new("alice");
            AuthenticationPrompt prompt = new(1, isEchoed: false, "Password: ");
            List<string> unanswered = [];

            SshNetAuthAdapter.AnswerPrompts(PromptEvent(prompt), credential, unanswered);

            Assert.That(unanswered, Has.Count.EqualTo(1));
        }

        [Test]
        public void NoPromptsHaveBeenRecordedBeforeAConnectionIsAttempted()
        {
            using ResolvedSshCredential credential = new("alice", secret: "secret123");

            using SshNetAuthentication authentication = SshNetAuthAdapter.Translate(credential);

            Assert.That(authentication.UnansweredPrompts, Is.Empty);
        }

        // ---- helpers ---------------------------------------------------------------

        private static AuthenticationPromptEventArgs PromptEvent(params AuthenticationPrompt[] prompts) =>
            new("alice", instruction: string.Empty, language: "en-US", prompts);

        /// <summary>
        /// An identity carrying a key handle SSH.NET can use. A real one comes from the agent
        /// library; any <c>IPrivateKeySource</c> proves the adapter's unboxing path, and no test may
        /// require a running agent.
        /// </summary>
        private static SshAgentIdentity StubIdentity(string comment = "alice@laptop") =>
            new(comment, "ssh-ed25519") { KeyHandle = new StubPrivateKeySource() };

        private sealed class StubPrivateKeySource : IPrivateKeySource
        {
            public IReadOnlyCollection<Renci.SshNet.Security.HostAlgorithm> HostKeyAlgorithms => [];
        }

        private static string WriteTempFile(string contents)
        {
            string path = Path.Combine(Path.GetTempPath(), "mrng-key-" + Guid.NewGuid().ToString("N"));
            File.WriteAllText(path, contents);
            return path;
        }
    }
}
