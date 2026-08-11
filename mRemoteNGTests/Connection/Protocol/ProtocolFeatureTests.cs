using System;
using System.Collections.Generic;
using System.Linq;
using mRemoteNG.Connection.Protocol;
using NUnit.Framework;

namespace mRemoteNGTests.Connection.Protocol;

/// <summary>
/// Which protocols the two SSH file features — the SFTP file manager and the transfer window —
/// offer themselves for.
/// </summary>
/// <remarks>
/// Written because each gate was inline at two menu call sites when <see cref="ProtocolType.SSHNative"/>
/// was introduced by a separate change, so the native SSH terminal shipped with both greyed out for
/// it. A list of protocols in one place is only as good as something checking it.
/// </remarks>
[TestFixture]
public class ProtocolFeatureTests
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
        Assert.That(NonSshProtocols().Where(ProtocolFeature.SupportsSftp), Is.Empty);
    }

    // ---- the transfer window -----------------------------------------------------

    [TestCase(ProtocolType.SSH2)]
    [TestCase(ProtocolType.OpenSSH)]
    [TestCase(ProtocolType.SSHNative)]
    public void EverySshProtocolSshNetCanServeIsOfferedTheTransferWindow(ProtocolType protocol)
    {
        // Same reasoning as the file manager: SecureTransfer opens its own SSH.NET connection for
        // SCP and for SFTP alike, so the session's own transport does not come into it.
        Assert.That(ProtocolFeature.SupportsFileTransfer(protocol), Is.True);
    }

    [Test]
    public void Ssh1IsNotOfferedTheTransferWindow()
    {
        // The gate used to admit SSH1 — SSH.NET speaks SSH2 only, so the menu entry led to a
        // connection that could not be made. Withholding it is the fix, not a regression.
        Assert.That(ProtocolFeature.SupportsFileTransfer(ProtocolType.SSH1), Is.False);
    }

    [Test]
    public void NoProtocolThatIsNotSshIsOfferedTheTransferWindow()
    {
        Assert.That(NonSshProtocols().Where(ProtocolFeature.SupportsFileTransfer), Is.Empty);
    }

    [Test]
    public void BothFeaturesAgree()
    {
        // They are stated separately because they answer different questions, but they share one
        // list. If they ever diverge it must be because someone meant them to, and this is where
        // that shows up.
        Assert.That(Enum.GetValues<ProtocolType>()
                .Where(p => ProtocolFeature.SupportsSftp(p) != ProtocolFeature.SupportsFileTransfer(p)),
            Is.Empty);
    }

    private static IEnumerable<ProtocolType> NonSshProtocols() =>
        Enum.GetValues<ProtocolType>()
            .Where(p => p is not (ProtocolType.SSH1 or ProtocolType.SSH2 or ProtocolType.OpenSSH
                or ProtocolType.SSHNative));
}
