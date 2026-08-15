using System;
using System.Linq;
using System.Reflection;
using System.Security;
using mRemoteNG.Connection;
using NUnit.Framework;

namespace mRemoteNGTests.Connection;

/// <summary>
/// How a connection record holds its three secrets, asserted because it is invisible from outside.
/// </summary>
/// <remarks>
/// <para>
/// The properties are typed <see cref="string"/> for the property grid's benefit, and the storage
/// behind them is <see cref="SecureString"/>. A reader — or an automated scan — sees only the
/// property type and concludes the secret is held in a plain field, which is how this fork keeps
/// being reported as carrying CVE-2023-30367 when it does not.
/// </para>
/// <para>
/// That is the reason for these tests. Nothing here changes behaviour; it pins behaviour that is
/// true today, untested, and one well-meant simplification of the setter away from being given up
/// silently. A refactor that replaced <c>SetSecureStringField</c> with <c>SetField</c> would pass
/// every other test in the suite.
/// </para>
/// </remarks>
[TestFixture]
public class ConnectionRecordSecretStorageTests
{
    private static readonly string[] SecretProperties =
        [nameof(ConnectionInfo.Password), nameof(ConnectionInfo.RDGatewayPassword),
         nameof(ConnectionInfo.VNCProxyPassword)];

    private static readonly string[] SecretFields =
        ["_password", "_rdGatewayPassword", "_vncProxyPassword"];

    [TestCaseSource(nameof(SecretFields))]
    public void EachSecretIsHeldInASecureString(string fieldName)
    {
        FieldInfo field = SecretField(fieldName);

        Assert.That(field.FieldType, Is.EqualTo(typeof(SecureString)),
            "the property type is string for the property grid; the storage must not be");
    }

    [Test]
    public void NoSecretIsHeldInAPlainStringField()
    {
        // The same claim from the other side, and the one that catches a *fourth* secret added later
        // without this treatment. Named fields would not.
        string[] plainStringSecrets =
        [
            .. typeof(AbstractConnectionRecord)
                .GetFields(BindingFlags.NonPublic | BindingFlags.Instance)
                .Where(f => f.FieldType == typeof(string))
                .Select(f => f.Name)
                .Where(name => name.Contains("assword", StringComparison.Ordinal))
        ];

        Assert.That(plainStringSecrets, Is.Empty);
    }

    [TestCaseSource(nameof(SecretProperties))]
    public void ReplacingASecretDisposesTheOneItReplaces(string propertyName)
    {
        // Without this the old value stays in memory, encrypted but never released, for every edit
        // the user makes. SecureString is not a security boundary on its own; leaking every previous
        // value of one is giving up what little it does buy.
        ConnectionInfo connection = new();
        Set(connection, propertyName, "first");
        SecureString replaced = Stored(connection, propertyName);

        Set(connection, propertyName, "second");

        Assert.Multiple(() =>
        {
            Assert.Throws<ObjectDisposedException>(() => _ = replaced.Length,
                "the previous value was left alive");
            Assert.That(Stored(connection, propertyName), Is.Not.SameAs(replaced));
            Assert.That(Get(connection, propertyName), Is.EqualTo("second"));
        });
    }

    [TestCaseSource(nameof(SecretProperties))]
    public void SettingTheValueItAlreadyHoldsChangesNothing(string propertyName)
    {
        // The property grid and the inheritance machinery both write properties back unchanged. If
        // that replaced the stored value it would churn a secret — and dispose an instance a caller
        // may be mid-read of — on every repaint.
        ConnectionInfo connection = new();
        Set(connection, propertyName, "unchanged");
        SecureString before = Stored(connection, propertyName);

        int notifications = 0;
        connection.PropertyChanged += (_, e) =>
        {
            if (string.Equals(e.PropertyName, propertyName, StringComparison.Ordinal))
                notifications++;
        };

        Set(connection, propertyName, "unchanged");

        Assert.Multiple(() =>
        {
            Assert.That(Stored(connection, propertyName), Is.SameAs(before), "the instance was replaced");
            Assert.That(notifications, Is.Zero, "and a change nobody made was announced");
        });
    }

    [TestCaseSource(nameof(SecretProperties))]
    public void ChangingASecretIsAnnouncedOnce(string propertyName)
    {
        ConnectionInfo connection = new();
        Set(connection, propertyName, "before");

        int notifications = 0;
        connection.PropertyChanged += (_, e) =>
        {
            if (string.Equals(e.PropertyName, propertyName, StringComparison.Ordinal))
                notifications++;
        };

        Set(connection, propertyName, "after");

        Assert.That(notifications, Is.EqualTo(1));
    }

    [TestCaseSource(nameof(SecretProperties))]
    public void ASecretRoundTripsThroughTheStringProperty(string propertyName)
    {
        // Including the characters a UTF-16 round trip through unmanaged memory is most likely to
        // lose: the property grid writes whatever the user typed.
        const string awkward = "pässwörd – \"quoted\" & <angled> \U0001F511";
        ConnectionInfo connection = new();

        Set(connection, propertyName, awkward);

        Assert.That(Get(connection, propertyName), Is.EqualTo(awkward));
    }

    [TestCaseSource(nameof(SecretProperties))]
    public void AnUnsetSecretReadsAsEmptyRatherThanNull(string propertyName)
    {
        // Every caller treats these as non-null, and the backing field starts null.
        Assert.That(Get(new ConnectionInfo(), propertyName), Is.Empty);
    }

    [TestCaseSource(nameof(SecretProperties))]
    public void ClearingASecretDisposesItAndLeavesNothingBehind(string propertyName)
    {
        ConnectionInfo connection = new();
        Set(connection, propertyName, "secret");
        SecureString cleared = Stored(connection, propertyName);

        Set(connection, propertyName, "");

        Assert.Multiple(() =>
        {
            Assert.Throws<ObjectDisposedException>(() => _ = cleared.Length);
            Assert.That(Get(connection, propertyName), Is.Empty);
        });
    }

    private static FieldInfo SecretField(string fieldName)
    {
        FieldInfo? field = typeof(AbstractConnectionRecord)
            .GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);

        Assert.That(field, Is.Not.Null,
            $"'{fieldName}' no longer exists — if a secret was renamed, this fixture must follow it " +
            "rather than be deleted");

        return field!;
    }

    private static SecureString Stored(ConnectionInfo connection, string propertyName)
    {
        FieldInfo field = SecretField(propertyName switch
        {
            nameof(ConnectionInfo.Password) => "_password",
            nameof(ConnectionInfo.RDGatewayPassword) => "_rdGatewayPassword",
            _ => "_vncProxyPassword"
        });

        return (SecureString)field.GetValue(connection)!;
    }

    private static void Set(ConnectionInfo connection, string propertyName, string value) =>
        typeof(ConnectionInfo).GetProperty(propertyName)!.SetValue(connection, value);

    private static string Get(ConnectionInfo connection, string propertyName) =>
        (string)typeof(ConnectionInfo).GetProperty(propertyName)!.GetValue(connection)!;
}
