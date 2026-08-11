using System;
using System.Linq;
using mRemoteNG.Connection.Protocol;
using NUnit.Framework;

namespace mRemoteNGTests.Connection.Protocol;

/// <summary>
/// Which protocols the SFTP file manager offers itself for.
/// </summary>
/// <remarks>
/// Written because the gate was inline at two menu call sites when <see cref="ProtocolType.SSHNative"/>
/// was introduced by a separate change, so the native SSH terminal shipped with the file manager
/// greyed out. A list of protocols in one place is only as good as something checking it.
/// </remarks>
[TestFixture]
public class ProtocolFeatureSftpTests
{
    [TestCase(ProtocolType.SSH2)]
    [TestCase(ProtocolType.OpenSSH)]
    [TestCase(ProtocolType.SSHNative)]
    public void EverySshProtocolSshNetCanServeIsOffered(ProtocolType protocol)
    {
        // The file manager opens its own SSH.NET connection, so how the session is hosted is
        // irrelevant — a native terminal session is as serviceable as a reparented PuTTY one.
        Assert.That(ProtocolFeature.SupportsSftp(protocol), Is.True);
    }

    [Test]
    public void Ssh1IsNotOffered()
    {
        // SSH.NET's SFTP does not implement it, so offering it would fail at connect time.
        Assert.That(ProtocolFeature.SupportsSftp(ProtocolType.SSH1), Is.False);
    }

    [Test]
    public void NoProtocolThatIsNotSshIsOffered()
    {
        // Guards the other direction: a future protocol must be added deliberately rather than
        // by widening this predicate past what SFTP can actually reach.
        var nonSsh = Enum.GetValues<ProtocolType>()
            .Where(p => p is not (ProtocolType.SSH1 or ProtocolType.SSH2 or ProtocolType.OpenSSH
                or ProtocolType.SSHNative));

        Assert.That(nonSsh.Where(ProtocolFeature.SupportsSftp), Is.Empty);
    }
}
