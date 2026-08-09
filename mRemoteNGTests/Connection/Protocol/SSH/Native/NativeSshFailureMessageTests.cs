using System;
using System.Collections.Generic;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using mRemoteNG.Connection;
using mRemoteNG.Connection.Protocol.SSH.Native;
using mRemoteNG.Security.Ssh;
using NUnit.Framework;

namespace mRemoteNGTests.Connection.Protocol.SSH.Native;

/// <summary>
/// What the user is told when a connection is refused.
/// </summary>
/// <remarks>
/// Written after a real connection attempt. A server answers "Permission denied
/// (keyboard-interactive)" whether the key file was missing, was unreadable, or was sent and
/// refused — three causes with three different fixes, two of them not in this application at all.
/// </remarks>
[TestFixture]
[SupportedOSPlatform("windows")]
public class NativeSshFailureMessageTests
{
    private sealed class FakeSession : INativeSshTerminalSession
    {
        public IReadOnlyList<SshCredentialDiagnostic> Diagnostics { get; init; } = [];
        public IReadOnlyList<string> OfferedMethods { get; init; } = [];
        public string? OfferedKeyPath { get; init; }
        public string OfferedUsername { get; init; } = "someone";
        public IReadOnlyList<string> UnansweredPrompts { get; init; } = [];

        public bool IsConnected => false;

        // Never raised: this fake exists to answer questions about a failed connection, not to
        // carry one. Declared with explicit accessors so an unused backing field is not implied.
        public event Action<string>? OutputReceived { add { } remove { } }
        public event Action<string>? Disconnected { add { } remove { } }
        public Task ConnectAsync(uint columns, uint rows, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Send(string data) { }
        public void Resize(uint columns, uint rows) { }

        public void Dispose()
        {
        }
    }

    private static SshCredentialDiagnostic Diagnostic(string message, SshCredentialDiagnosticSeverity severity) =>
        new(ExternalCredentialProvider.None, severity, message);

    private const string Denied = "Permission denied (keyboard-interactive).";

    [Test]
    public void WithNoSessionTheMessageIsUnchanged()
    {
        Assert.That(ProtocolNativeSsh.DescribeFailure(Denied, null), Is.EqualTo(Denied));
    }

    [Test]
    public void AKeyThatWasSentAndRefusedPointsAtTheServer()
    {
        // The common case, and the one the raw message hides completely: nothing is wrong with the
        // credential, so there is nothing to fix in mRemoteNG. Saying the key was sent moves the
        // search to authorized_keys instead of back through the connection settings.
        using FakeSession session = new()
        {
            OfferedMethods = ["publickey", "keyboard-interactive"],
            OfferedKeyPath = @"C:\keys\id_ed25519",
            OfferedUsername = "alice"
        };

        string message = ProtocolNativeSsh.DescribeFailure(Denied, session);

        Assert.Multiple(() =>
        {
            Assert.That(message, Does.StartWith(Denied));
            Assert.That(message, Does.Contain(@"C:\keys\id_ed25519"));
            Assert.That(message, Does.Contain("authorized_keys"));
            Assert.That(message, Does.Contain("alice"),
                "a correct key sent as the wrong user fails exactly like a wrong key, so the "
                + "username has to be in the message");
        });
    }

    [Test]
    public void AKeyFromAnAgentIsReportedWithoutAPath()
    {
        using FakeSession session = new() { OfferedMethods = ["publickey", "keyboard-interactive"] };

        string message = ProtocolNativeSsh.DescribeFailure(Denied, session);

        Assert.Multiple(() =>
        {
            Assert.That(message, Does.Contain("authorized_keys"));
            Assert.That(message, Does.Not.Contain("{0}"));
        });
    }

    [Test]
    public void AnUnusableKeyIsNamedInsteadOfBlamingTheServer()
    {
        // Here the credential really was the problem, so pointing at authorized_keys would send
        // the user to the wrong machine.
        using FakeSession session = new()
        {
            OfferedMethods = ["keyboard-interactive"],
            Diagnostics =
            [
                Diagnostic("The configured private key file was not found: C:\\keys\\absent",
                    SshCredentialDiagnosticSeverity.Error)
            ]
        };

        string message = ProtocolNativeSsh.DescribeFailure(Denied, session);

        Assert.Multiple(() =>
        {
            Assert.That(message, Does.Contain("was not found"));
            Assert.That(message, Does.Not.Contain("authorized_keys"));
        });
    }

    [Test]
    public void AnUnansweredPromptExplainsTheRefusalOnItsOwn()
    {
        // A second factor nobody answered accounts for the refusal completely; checking
        // authorized_keys would waste the user's time.
        using FakeSession session = new()
        {
            OfferedMethods = ["publickey", "keyboard-interactive"],
            OfferedKeyPath = @"C:\keys\id_ed25519",
            UnansweredPrompts = ["Verification code: "]
        };

        string message = ProtocolNativeSsh.DescribeFailure(Denied, session);

        Assert.Multiple(() =>
        {
            Assert.That(message, Does.Contain("Verification code"));
            Assert.That(message, Does.Not.Contain("authorized_keys"));
        });
    }

    [Test]
    public void InformationalDiagnosticsAreLeftOut()
    {
        using FakeSession session = new()
        {
            Diagnostics = [Diagnostic("Using the credential from the vault.", SshCredentialDiagnosticSeverity.Information)]
        };

        Assert.That(ProtocolNativeSsh.DescribeFailure(Denied, session), Is.EqualTo(Denied));
    }

    [Test]
    public void RepeatedDiagnosticsAreNotRepeatedInTheMessage()
    {
        using FakeSession session = new()
        {
            Diagnostics =
            [
                Diagnostic("Key not found.", SshCredentialDiagnosticSeverity.Error),
                Diagnostic("Key not found.", SshCredentialDiagnosticSeverity.Error)
            ]
        };

        Assert.That(ProtocolNativeSsh.DescribeFailure(Denied, session), Is.EqualTo($"{Denied} Key not found."));
    }
}
