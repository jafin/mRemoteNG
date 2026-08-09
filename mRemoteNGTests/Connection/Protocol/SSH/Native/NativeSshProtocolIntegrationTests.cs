using System;
using System.Runtime.Versioning;
using mRemoteNG.Connection;
using mRemoteNG.Connection.Protocol;
using mRemoteNG.Connection.Protocol.RAW;
using mRemoteNG.Connection.Protocol.Rlogin;
using mRemoteNG.Connection.Protocol.SSH;
using mRemoteNG.Connection.Protocol.SSH.Native;
using mRemoteNG.Connection.Protocol.Telnet;
using mRemoteNG.Resources.Language;
using NUnit.Framework;

namespace mRemoteNGTests.Connection.Protocol.SSH.Native;

/// <summary>
/// Wiring for the native SSH terminal, and the promise that adding it left the protocols it sits
/// beside alone. A new <see cref="ProtocolType"/> is easy to add and easy to half-add: miss the
/// default-port switch and connections silently get port 0.
/// </summary>
[TestFixture]
[SupportedOSPlatform("windows")]
public class NativeSshProtocolIntegrationTests
{
    private static ConnectionInfo Connection(ProtocolType protocol) =>
        new() { Hostname = "example.invalid", Protocol = protocol };

    [Test]
    public void TheFactoryBuildsTheNativeTerminal()
    {
        using ProtocolBase protocol = new ProtocolFactory().CreateProtocol(Connection(ProtocolType.SSHNative));

        Assert.That(protocol, Is.InstanceOf<ProtocolNativeSsh>());
    }

    [Test]
    public void TheNativeTerminalDefaultsToPort22()
    {
        ConnectionInfo connection = Connection(ProtocolType.SSHNative);
        connection.SetDefaultPort();

        Assert.That(connection.Port, Is.EqualTo(22),
            "a missing case in GetDefaultPort yields 0, which fails at connect time with no clue why");
    }

    [Test]
    public void TheNativeTerminalIsDistinctFromSsh2()
    {
        Assert.That(ProtocolType.SSHNative, Is.Not.EqualTo(ProtocolType.SSH2),
            "the two must coexist so a connection can be run each way and compared — design.md D1");
    }

    [Test]
    public void TheNativeTerminalHasALocalizedDescription()
    {
        // Without this the protocol dropdown shows the bare enum name.
        Assert.That(Language.SshNative, Is.Not.Null.And.Not.Empty);
    }

    // ---- the protocols this change promised not to disturb (task 4.6) ------------------

    [TestCase(ProtocolType.SSH1, typeof(ProtocolSSH1), 22)]
    [TestCase(ProtocolType.SSH2, typeof(ProtocolSSH2), 22)]
    [TestCase(ProtocolType.Telnet, typeof(ProtocolTelnet), 23)]
    [TestCase(ProtocolType.Rlogin, typeof(ProtocolRlogin), 513)]
    [TestCase(ProtocolType.RAW, typeof(RawProtocol), 23)]
    public void TheProtocolsBesideItStillBuildAndKeepTheirPorts(
        ProtocolType protocolType, Type expected, int expectedPort)
    {
        using ProtocolBase protocol = new ProtocolFactory().CreateProtocol(Connection(protocolType));

        ConnectionInfo connection = Connection(protocolType);
        connection.SetDefaultPort();

        Assert.Multiple(() =>
        {
            Assert.That(protocol, Is.InstanceOf(expected));
            Assert.That(connection.Port, Is.EqualTo(expectedPort));
        });
    }

    [Test]
    public void ExistingProtocolTypeValuesAreUnchanged()
    {
        // These numbers are persisted in every user's connections file. Renumbering the enum would
        // silently repoint saved connections at a different protocol, so the new value takes an
        // unused slot rather than shifting anything.
        Assert.Multiple(() =>
        {
            Assert.That((int)ProtocolType.SSH1, Is.EqualTo(2));
            Assert.That((int)ProtocolType.SSH2, Is.EqualTo(3));
            Assert.That((int)ProtocolType.Telnet, Is.EqualTo(4));
            Assert.That((int)ProtocolType.Rlogin, Is.EqualTo(5));
            Assert.That((int)ProtocolType.RAW, Is.EqualTo(6));
            Assert.That((int)ProtocolType.IntApp, Is.EqualTo(20));
            Assert.That((int)ProtocolType.Winbox, Is.EqualTo(21));
            Assert.That((int)ProtocolType.OpenSSH, Is.EqualTo(22));
            Assert.That((int)ProtocolType.SSHNative, Is.EqualTo(23));
        });
    }
}
