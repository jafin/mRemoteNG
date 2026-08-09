using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using mRemoteNG.Connection;
using mRemoteNG.Connection.Protocol.SSH.Native;
using mRemoteNG.Security.Ssh;
using NUnit.Framework;

namespace mRemoteNGTests.Connection.Protocol.SSH.Native;

/// <summary>
/// The parts of the session that are answerable without a server: what it reports before it has
/// connected, and what it refuses to do when there is no shell.
/// </summary>
[TestFixture]
[SupportedOSPlatform("windows")]
public class NativeSshTerminalSessionTests
{
    private static SshCredentialDiagnostic Diagnostic(
        string message,
        SshCredentialDiagnosticSeverity severity = SshCredentialDiagnosticSeverity.Information) =>
        new(ExternalCredentialProvider.None, severity, message);

    private static NativeSshTerminalSession Session(params SshCredentialDiagnostic[] diagnostics) =>
        new("example.invalid", 22, new ResolvedSshCredential("alice", diagnostics: diagnostics));

    [Test]
    public void ResizingBeforeConnectingSendsNothingAndRaisesNothing()
    {
        using NativeSshTerminalSession session = Session();

        Assert.Multiple(() =>
        {
            Assert.DoesNotThrow(() => session.Resize(120, 40));
            Assert.That(session.IsConnected, Is.False);
        });
    }

    [Test]
    public void ResizingAfterDisposalSendsNothingAndRaisesNothing()
    {
        NativeSshTerminalSession session = Session();
        session.Dispose();

        Assert.DoesNotThrow(() => session.Resize(120, 40));
    }

    [Test]
    public void SendingBeforeConnectingIsANoOp()
    {
        using NativeSshTerminalSession session = Session();

        Assert.DoesNotThrow(() => session.Send("ls\n"));
    }

    [Test]
    public void ANewSessionIsNotConnected()
    {
        using NativeSshTerminalSession session = Session();

        Assert.That(session.IsConnected, Is.False);
    }

    [Test]
    public void ResolutionDiagnosticsAreAvailableBeforeConnecting()
    {
        // The protocol replays these whether or not the connection got far enough to fail, so
        // they cannot be gated behind a successful connect.
        using NativeSshTerminalSession session = Session(
            Diagnostic("vault returned a key"),
            Diagnostic("the agent was unreachable", SshCredentialDiagnosticSeverity.Error));

        string[] expected = ["vault returned a key", "the agent was unreachable"];
        Assert.Multiple(() =>
        {
            Assert.That(session.Diagnostics.Select(d => d.Message), Is.SupersetOf(expected));
            Assert.That(session.Diagnostics.Select(d => d.Severity),
                Does.Contain(SshCredentialDiagnosticSeverity.Error));
        });
    }

    [Test]
    public void DiagnosticsIncludeCredentialComponentsTheAdapterCouldNotUse()
    {
        // A key path that does not exist is not a resolution diagnostic — the resolver accepted
        // it — but the adapter cannot turn it into a method, and the user still has to be told.
        string missingKey = Path.Combine(Path.GetTempPath(), $"mrng-absent-{Guid.NewGuid():N}");
        using NativeSshTerminalSession session = new("example.invalid", 22,
            new ResolvedSshCredential("alice", privateKeyPath: missingKey));

        Assert.That(session.Diagnostics, Is.Not.Empty,
            "an unusable key must be reported, not silently dropped");
    }

    [Test]
    public void DisposingTwiceIsSafe()
    {
        NativeSshTerminalSession session = Session();

        session.Dispose();
        Assert.DoesNotThrow(session.Dispose);
    }

    [Test]
    public void ForConnectionRequiresAConnection()
    {
        Assert.Throws<ArgumentNullException>(() => NativeSshTerminalSession.ForConnection(null));
    }
}
