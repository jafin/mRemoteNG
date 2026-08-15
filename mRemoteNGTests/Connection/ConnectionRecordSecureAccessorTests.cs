using System;
using System.Collections.Generic;
using System.Linq;
using System.Security;
using mRemoteNG.App;
using mRemoteNG.Connection;
using mRemoteNG.Container;
using mRemoteNG.Credential;
using mRemoteNG.Credential.Repositories;
using mRemoteNG.Security;
using mRemoteNG.Tree.Root;
using NSubstitute;
using NUnit.Framework;

namespace mRemoteNGTests.Connection;

/// <summary>
/// The <c>SecureString</c> accessors, and the property that makes them safe to have: they answer
/// exactly what the plain-text properties answer.
/// </summary>
/// <remarks>
/// <para>
/// A secret resolves through four routes — the record's own value, a bound credential record, a
/// connection link, and inheritance up the tree. Two of them can be answered without a string ever
/// existing; the other two are left going through the plain-text property deliberately, because
/// reimplementing an inheritance walk that skips parents holding empty credentials would be a second
/// copy of rules free to drift from the first.
/// </para>
/// <para>
/// <b>Drift is the whole risk here.</b> An accessor that quietly returns a different password than
/// the property beside it is far worse than the copy it saves — it would authenticate as the wrong
/// thing, or fail to, with two code paths each looking correct on its own. So every route is
/// asserted for agreement rather than only for its own answer.
/// </para>
/// </remarks>
[TestFixture]
public class ConnectionRecordSecureAccessorTests
{
    private readonly List<ICredentialRepository> _registered = [];

    [TearDown]
    public void Teardown()
    {
        // Runtime.CredentialProviderCatalog is a static singleton, so anything left in it would
        // decide the answer for every test that ran afterwards.
        foreach (ICredentialRepository repository in _registered)
            Runtime.CredentialProviderCatalog.RemoveProvider(repository);

        _registered.Clear();
    }

    [Test]
    public void TheAccessorAgreesWithThePropertyForTheRecordsOwnSecret()
    {
        ConnectionInfo connection = new() { Password = "own-secret" };

        AssertAgrees(connection, "own-secret");
    }

    [Test]
    public void TheAccessorAgreesWithThePropertyForACredentialRecord()
    {
        // The route worth intercepting, and therefore the one most able to diverge: it is the only
        // one answered by code written for this accessor rather than by the property itself.
        ConnectionInfo connection = new() { Password = "ignored-when-bound" };
        BindCredential(connection, "from-the-credential-store");

        AssertAgrees(connection, "from-the-credential-store");
    }

    [Test]
    public void TheAccessorAgreesWithThePropertyForAnInheritedSecret()
    {
        RootNodeInfo root = new(RootNodeType.Connection);
        ContainerInfo parent = new() { Password = "parents-secret" };
        ConnectionInfo child = new();
        child.Inheritance.Password = true;

        root.AddChild(parent);
        parent.AddChild(child);

        AssertAgrees(child, "parents-secret");
    }

    [Test]
    public void TheAccessorAgreesWithThePropertyWhenInheritanceSkipsAnEmptyParent()
    {
        // The walk this accessor deliberately does not reimplement: an empty credential on the
        // parent is skipped and the grandparent answers. Asserted here because "deliberately not
        // reimplemented" is only true for as long as somebody checks.
        RootNodeInfo root = new(RootNodeType.Connection);
        ContainerInfo grandparent = new() { Password = "grandparents-secret" };
        ContainerInfo parent = new() { Password = "" };
        parent.Inheritance.Password = false;
        ConnectionInfo child = new();
        child.Inheritance.Password = true;

        root.AddChild(grandparent);
        grandparent.AddChild(parent);
        parent.AddChild(child);

        AssertAgrees(child, "grandparents-secret");
    }

    [Test]
    public void AnUnsetSecretComesBackEmptyRatherThanNull()
    {
        using SecureString secret = new ConnectionInfo().SecurePassword;

        Assert.That(secret.Length, Is.Zero);
    }

    [Test]
    public void TheCallerGetsACopyItOwns()
    {
        // Handing back the stored instance would let any caller dispose the record's own secret, and
        // the next read of Password would throw from a getter nobody expects to.
        ConnectionInfo connection = new() { Password = "shared" };

        SecureString first = connection.SecurePassword;
        SecureString second = connection.SecurePassword;

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.Not.SameAs(second));
            Assert.That(first.Length, Is.EqualTo(second.Length));
        });

        first.Dispose();
        second.Dispose();

        Assert.Multiple(() =>
        {
            Assert.That(connection.Password, Is.EqualTo("shared"), "the record kept its secret");
            Assert.That(Reveal(connection.SecurePassword), Is.EqualTo("shared"));
        });
    }

    [Test]
    public void DisposingACopyDoesNotReachIntoTheCredentialStore()
    {
        // The credential branch hands out a copy of a record that other connections share. Handing
        // out the record's own instance would let one connection's caller dispose a credential every
        // other connection bound to it still needs.
        ConnectionInfo connection = new();
        ICredentialRecord record = BindCredential(connection, "shared-credential");

        connection.SecurePassword.Dispose();

        Assert.Multiple(() =>
        {
            Assert.That(record.Password.Length, Is.EqualTo("shared-credential".Length));
            Assert.That(Reveal(connection.SecurePassword), Is.EqualTo("shared-credential"));
        });
    }

    [Test]
    public void TheGatewayAndProxySecretsHaveAccessorsToo()
    {
        ConnectionInfo connection = new()
        {
            RDGatewayPassword = "gateway-secret",
            VNCProxyPassword = "proxy-secret"
        };

        Assert.Multiple(() =>
        {
            Assert.That(Reveal(connection.SecureRDGatewayPassword), Is.EqualTo(connection.RDGatewayPassword));
            Assert.That(Reveal(connection.SecureVNCProxyPassword), Is.EqualTo(connection.VNCProxyPassword));
        });
    }

    [Test]
    public void ACredentialRecordDoesNotAnswerForTheGatewayOrProxySecret()
    {
        // Credential binding covers username, password and domain. A gateway password is a different
        // secret, and answering it from the credential record would silently authenticate the
        // gateway with the connection's own credentials.
        ConnectionInfo connection = new() { RDGatewayPassword = "gateway-secret" };
        BindCredential(connection, "from-the-credential-store");

        Assert.That(Reveal(connection.SecureRDGatewayPassword), Is.EqualTo("gateway-secret"));
    }

    /// <summary>Asserts the accessor and the property give the same answer, and that it is the right one.</summary>
    private static void AssertAgrees(ConnectionInfo connection, string expected)
    {
        Assert.Multiple(() =>
        {
            Assert.That(connection.Password, Is.EqualTo(expected), "the plain-text property");
            Assert.That(Reveal(connection.SecurePassword), Is.EqualTo(expected), "and the accessor beside it");
        });
    }

    private ICredentialRecord BindCredential(ConnectionInfo connection, string password)
    {
        CredentialRecord record = new()
        {
            Title = "test",
            Username = "user",
            Password = password.ConvertToSecureString()
        };

        ICredentialRepository repository = Substitute.For<ICredentialRepository>();
        repository.Config.Returns(new CredentialRepositoryConfig(Guid.NewGuid())
        {
            Source = "test-source-" + Guid.NewGuid()
        });
        repository.CredentialRecords.Returns(new List<ICredentialRecord> { record });

        Runtime.CredentialProviderCatalog.AddProvider(repository);
        _registered.Add(repository);

        connection.CredentialId = record.Id.ToString();
        return record;
    }

    private static string Reveal(SecureString secret)
    {
        using (secret)
            return secret.ConvertToUnsecureString();
    }
}
