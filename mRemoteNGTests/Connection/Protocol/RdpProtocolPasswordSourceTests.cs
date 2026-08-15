using mRemoteNG.Connection.Protocol.RDP;
using NUnit.Framework;

namespace mRemoteNGTests.Connection.Protocol;

/// <summary>
/// Which password an RDP connection sends, for every combination of the things that decide it.
/// </summary>
/// <remarks>
/// <para>
/// This is the coverage the change that prompted it said mattered more than the change. Narrowing
/// the window in which a password exists as plain text is worth little; breaking authentication in
/// <c>SetCredentials</c> would cost a great deal, and the paths that feed it — six external
/// credential providers, an empty-username fallback carrying four more, Restricted Admin, Remote
/// Credential Guard — cannot be exercised without a real host and a real ActiveX control.
/// </para>
/// <para>
/// So the decision those paths feed is pinned here instead. It is not a new rule: every case below
/// is what the method did when the password was a single local read at the top of it. If one of
/// these changes, authentication changed.
/// </para>
/// <para>
/// The manual verification against a real RDP host stays required. These tests say the decision is
/// unchanged; they cannot say the RDP control still accepts what it is given.
/// </para>
/// </remarks>
[TestFixture]
public class RdpProtocolPasswordSourceTests
{
    [Test]
    public void TheConnectionsOwnSecretIsUsedWhenNoProviderSuppliedOne()
    {
        Assert.That(Choose(providerSupplied: null, connectionHasSecret: true),
            Is.EqualTo(RdpProtocol.RdpPasswordSource.Connection));
    }

    [Test]
    public void AProviderThatSuppliedAPasswordWins()
    {
        // Including over the connection's own, which is the point of configuring one.
        Assert.That(Choose(providerSupplied: "from-the-vault", connectionHasSecret: true),
            Is.EqualTo(RdpProtocol.RdpPasswordSource.Provider));
    }

    [Test]
    public void AProviderThatRanAndReturnedNothingDoesNotFallBackToTheConnection()
    {
        // The distinction the refactor had to preserve, and the one easiest to lose: null means
        // nothing was consulted, empty means something was and it had nothing. A provider returning
        // empty has always fallen through to the configured default rather than to the connection's
        // own secret — quietly sending the stored password when a vault deliberately returned none
        // would be a change nobody asked for.
        Assert.That(Choose(providerSupplied: "", connectionHasSecret: true),
            Is.EqualTo(RdpProtocol.RdpPasswordSource.ConfiguredDefault));
    }

    [Test]
    public void AConnectionWithNoSecretFallsBackToTheConfiguredDefault()
    {
        Assert.That(Choose(providerSupplied: null, connectionHasSecret: false),
            Is.EqualTo(RdpProtocol.RdpPasswordSource.ConfiguredDefault));
    }

    [TestCase("windows")]
    [TestCase("")]
    [TestCase(null)]
    [TestCase("Custom")]
    public void TheConfiguredDefaultAppliesOnlyInCustomMode(string? mode)
    {
        // Ordinal comparison, so "Custom" is not "custom". Kept deliberately: this reads a stored
        // settings value that the application itself only ever writes in lower case, and loosening
        // it would start honouring a value nothing produces.
        Assert.That(Choose(providerSupplied: null, connectionHasSecret: false, mode: mode),
            Is.EqualTo(RdpProtocol.RdpPasswordSource.None));
    }

    [Test]
    public void WhetherADefaultIsConfiguredIsNotDecidedHere()
    {
        // Deliberately absent from this decision. Answering it means unprotecting the stored value,
        // and the stored value is ciphertext — so a connection that will never use the answer must
        // not run the protector to obtain it. `AssignPassword` unprotects inside the
        // ConfiguredDefault branch and sends nothing if what comes back is empty, which is also more
        // correct than what it replaced: the old code tested the *stored* form for emptiness, so a
        // default that unprotected to nothing was sent as an empty password.
        Assert.That(Choose(providerSupplied: null, connectionHasSecret: false),
            Is.EqualTo(RdpProtocol.RdpPasswordSource.ConfiguredDefault),
            "the source is chosen; whether it yields anything is settled where it is read");
    }

    [Test]
    public void NothingAnywhereSendsNothing()
    {
        // Not an empty password — nothing is assigned at all, and the RDP client prompts. That is
        // what a connection with no password has always done.
        Assert.That(Choose(providerSupplied: null, connectionHasSecret: false, mode: "none"),
            Is.EqualTo(RdpProtocol.RdpPasswordSource.None));
    }

    private static RdpProtocol.RdpPasswordSource Choose(string? providerSupplied,
                                                        bool connectionHasSecret,
                                                        string? mode = "custom") =>
        RdpProtocol.ChoosePasswordSource(providerSupplied, connectionHasSecret, mode);
}
