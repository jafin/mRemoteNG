using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Runtime.Versioning;
using System.Security;
using mRemoteNG.App;
using mRemoteNG.Config.Serializers;
using mRemoteNG.Config.Serializers.ConnectionSerializers.Sql;
using mRemoteNG.Connection;
using mRemoteNG.Messages;
using mRemoteNG.Security;
using mRemoteNG.Security.SymmetricEncryption;
using mRemoteNG.Tree;
using NUnit.Framework;

namespace mRemoteNGTests.Config.Serializers;

/// <summary>
/// Every secret column, through both providers, out to a table and back.
/// </summary>
/// <remarks>
/// <para>
/// The serializer and the deserializer take an <c>ICryptographyProvider</c> and never name one, so
/// swapping the provider ought to be invisible to them. "Ought to" is the reason for these tests:
/// the two ends have to agree column for column, and where they disagree the result is a database
/// that saves without complaint and gives back the wrong password — which is indistinguishable, from
/// the outside, from somebody having changed it.
/// </para>
/// <para>
/// The second half is about what a failed decryption <i>means</i>. Passing the undecryptable value
/// straight through is right for the legacy provider, which cannot tell an altered value from one
/// that was never encrypted — old databases hold plain values, and refusing to load them would help
/// nobody. It is wrong for the authenticated provider, which can tell, and where passing it through
/// would hand a connection its own base64 ciphertext as a password while the tamper detection said
/// nothing.
/// </para>
/// </remarks>
[SupportedOSPlatform("windows")]
[TestFixture]
public class SqlSecretColumnRoundTripTests
{
    private const string Key = "the master password";
    private const string ConnectionPassword = "hunter2";
    private const string GatewayPassword = "gateway-secret";
    private const string ProxyPassword = "proxy-secret";

    /// <summary>The three columns in <c>tblCons</c> that hold anything worth encrypting.</summary>
    private static readonly string[] SecretColumns = ["Password", "RDGatewayPassword", "VNCProxyPassword"];

    private static IEnumerable<TestCaseData> Providers()
    {
        yield return new TestCaseData(new LegacyRijndaelCryptographyProvider()).SetName("{m}(legacy)");
        yield return new TestCaseData(new AeadCryptographyProvider()).SetName("{m}(authenticated)");
    }

    [SetUp]
    public void Setup() => Runtime.MessageCollector.ClearMessages();

    [TestCaseSource(nameof(Providers))]
    public void EverySecretColumnComesBackAsItWentIn(ICryptographyProvider provider)
    {
        DataTable table = Serialize(provider, Connection());

        ConnectionInfo restored = Deserialize(provider, table);

        Assert.Multiple(() =>
        {
            Assert.That(restored.Password, Is.EqualTo(ConnectionPassword));
            Assert.That(restored.RDGatewayPassword, Is.EqualTo(GatewayPassword));
            Assert.That(restored.VNCProxyPassword, Is.EqualTo(ProxyPassword));
        });
    }

    [TestCaseSource(nameof(Providers))]
    public void NoSecretColumnIsWrittenInTheClear(ICryptographyProvider provider)
    {
        // Without this, the round-trip above would pass just as happily against a provider that
        // encrypted nothing at all — which is exactly the defect worth catching in a change that
        // swaps one provider for another.
        DataTable table = Serialize(provider, Connection());
        DataRow row = table.Rows[0];

        Assert.Multiple(() =>
        {
            Assert.That((string)row["Password"], Is.Not.EqualTo(ConnectionPassword));
            Assert.That((string)row["RDGatewayPassword"], Is.Not.EqualTo(GatewayPassword));
            Assert.That((string)row["VNCProxyPassword"], Is.Not.EqualTo(ProxyPassword));
        });
    }

    [Test]
    public void TheTwoProvidersDoNotReadEachOther()
    {
        // The premise the whole version gate rests on. If either could read the other's output,
        // choosing the provider from the database's recorded version would be decoration.
        DataTable authenticated = Serialize(new AeadCryptographyProvider(), Connection());

        ConnectionInfo restored = Deserialize(new LegacyRijndaelCryptographyProvider(), authenticated);

        Assert.That(restored.Password, Is.Not.EqualTo(ConnectionPassword));
    }

    [Test]
    public void AnAlteredValueIsRefusedRatherThanUsed()
    {
        // **The point of authenticating the ciphertext.** Somebody with write access to the database
        // changes a stored password; GCM's tag catches it. What must not happen is the old fallback
        // treating that as "this was probably never encrypted" and handing the connection its own
        // base64 as a password — tamper detection doing its work and reporting nothing.
        AeadCryptographyProvider provider = new();
        DataTable table = Serialize(provider, Connection());
        string original = (string)table.Rows[0]["Password"];
        table.Rows[0]["Password"] = Tamper(original);

        ConnectionInfo restored = Deserialize(provider, table);

        Assert.Multiple(() =>
        {
            Assert.That(restored.Password, Is.Empty, "no password rather than a wrong one");
            Assert.That(restored.Password, Is.Not.EqualTo(original), "and certainly not the ciphertext");
            Assert.That(ErrorsReported(), Is.GreaterThan(0), "and the user is told");

            // The rest of the connection still loads. One altered row must not deny access to every
            // other connection in the database.
            Assert.That(restored.RDGatewayPassword, Is.EqualTo(GatewayPassword));
        });
    }

    [Test]
    public void AValueThatWasNeverEncryptedIsRefusedUnderAuthenticatedEncryption()
    {
        // Same reasoning from the other side. Under the authenticated provider there is no such
        // thing as a plain value that merely failed to decrypt: every value in such a database was
        // written encrypted, so anything that will not decrypt is wrong.
        AeadCryptographyProvider provider = new();
        DataTable table = Serialize(provider, Connection());
        table.Rows[0]["Password"] = "not encrypted at all!";

        ConnectionInfo restored = Deserialize(provider, table);

        Assert.Multiple(() =>
        {
            Assert.That(restored.Password, Is.Empty);
            Assert.That(ErrorsReported(), Is.GreaterThan(0));
        });
    }

    [Test]
    public void AValueThatWasNeverEncryptedIsStillReadUnderTheLegacyProvider()
    {
        // And the compatibility this preserves. Databases old enough to hold plain values exist; the
        // legacy provider cannot tell one from an altered value, so it keeps reading them. Refusing
        // them would break stores that work today to protect against something it cannot detect.
        LegacyRijndaelCryptographyProvider provider = new();
        DataTable table = Serialize(provider, Connection());
        table.Rows[0]["Password"] = "not encrypted at all!";

        ConnectionInfo restored = Deserialize(provider, table);

        Assert.Multiple(() =>
        {
            Assert.That(restored.Password, Is.EqualTo("not encrypted at all!"));
            Assert.That(ErrorsReported(), Is.Zero, "and it is not an error, so it is not reported as one");
        });
    }

    [Test]
    public void EveryKnownSecretColumnIsCoveredByTheseTests()
    {
        // A guard on the list above rather than on the code. A fourth secret column added to the
        // schema without being added here would leave it silently untested through both providers —
        // and the tests would still be green, which is the failure mode worth a test of its own.
        DataTable schema = DataTableSerializer.GetExpectedSchema();

        IEnumerable<string> secretLooking = schema.Columns.Cast<DataColumn>()
            .Select(column => column.ColumnName)
            .Where(name => name.EndsWith("Password", StringComparison.Ordinal) &&
                           !name.StartsWith("Inherit", StringComparison.Ordinal));

        Assert.That(secretLooking, Is.EquivalentTo(SecretColumns));
    }

    private static ConnectionInfo Connection() =>
        new()
        {
            Name = "server",
            Password = ConnectionPassword,
            RDGatewayPassword = GatewayPassword,
            VNCProxyPassword = ProxyPassword
        };

    private static DataTable Serialize(ICryptographyProvider provider, ConnectionInfo connection) =>
        new DataTableSerializer(new SaveFilter { SavePassword = true }, provider,
                                Key.ConvertToSecureString())
            .Serialize(connection);

    private static ConnectionInfo Deserialize(ICryptographyProvider provider, DataTable table) =>
        new DataTableDeserializer(provider, Key.ConvertToSecureString())
            .Deserialize(table)
            .GetRecursiveChildList()[0];

    /// <summary>
    /// Flips a byte inside the ciphertext, keeping it valid base64 — the alteration the tag exists
    /// to catch, rather than a string mangled so badly it never reaches the cipher.
    /// </summary>
    private static string Tamper(string cipherText)
    {
        byte[] bytes = Convert.FromBase64String(cipherText);
        bytes[^1] ^= 0xFF;
        return Convert.ToBase64String(bytes);
    }

    private static int ErrorsReported() =>
        Runtime.MessageCollector.Messages.Count(message => message.Class == MessageClass.ErrorMsg);
}
