using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security;
using System.Text.RegularExpressions;
using mRemoteNG.Config.Serializers.ConnectionSerializers.Xml;
using mRemoteNG.Connection;
using mRemoteNG.Security;
using mRemoteNG.Security.SymmetricEncryption;
using mRemoteNG.Tools;
using mRemoteNG.Tree;
using mRemoteNG.Tree.Root;
using NUnit.Framework;

namespace mRemoteNGTests.TestHelpers;

/// <summary>
/// Reading and breaking a connection store's secrets, for the fixtures that assert when they are
/// decrypted.
/// </summary>
/// <remarks>
/// Shared because both fixtures reach into the same private fields by name. Two copies of that
/// reflection would mean a field rename breaking one of them silently.
/// </remarks>
internal static class ConnectionSecretInspector
{
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// The record's own <see cref="SecureString"/>, read without going through the property that
    /// decrypts it.
    /// </summary>
    internal static SecureString? StoredSecureString(ConnectionInfo connection) =>
        (SecureString?)Field(SecretFieldName(nameof(ConnectionInfo.Password))).GetValue(connection);

    /// <summary>
    /// Whether the record is still holding this secret as ciphertext, nothing having asked for it.
    /// </summary>
    internal static bool IsStillPending(ConnectionInfo connection, string secretName) =>
        Field(PendingFieldName(secretName)).GetValue(connection) is not null;

    internal static ConnectionTreeModel Reopen(string storePath, Func<Optional<SecureString>> authentication) =>
        new XmlConnectionsDeserializer(storePath, authentication)
            .Deserialize(File.ReadAllText(storePath));

    internal static RootNodeInfo Root(ConnectionTreeModel model) =>
        model.RootNodes.OfType<RootNodeInfo>().First();

    internal static ConnectionInfo[] Connections(ConnectionTreeModel model) => [.. Root(model).Children];

    /// <summary>
    /// A key identity of no particular store, for records assembled by hand rather than loaded.
    /// </summary>
    internal static ConnectionSecretKeyIdentity AnyKey() =>
        ConnectionSecretKeyIdentity.For(new LegacyRijndaelCryptographyProvider(), fileKey: null, password: "any");

    /// <summary>
    /// Replaces one connection's stored password with something that is not a ciphertext, leaving
    /// the rest of the file exactly as it was.
    /// </summary>
    internal static void CorruptTheStoredPasswordOf(string storePath, string connectionName)
    {
        string xml = File.ReadAllText(storePath);
        Regex attribute = new($"(?<head>Name=\"{Regex.Escape(connectionName)}\"[^>]*?Password=\")[^\"]*(?<tail>\")",
                              RegexOptions.ExplicitCapture, MatchTimeout);

        Assert.That(attribute.IsMatch(xml), Is.True,
            "the store was expected to hold this password as a readable attribute");

        File.WriteAllText(storePath, attribute.Replace(xml, "${head}bm90LWEtY2lwaGVydGV4dA==${tail}", 1));
    }

    /// <summary>
    /// Breaks the one ciphertext whose plaintext the loader knows in advance, so that no key can be
    /// accepted for this store.
    /// </summary>
    internal static void CorruptTheSentinel(string storePath)
    {
        string xml = File.ReadAllText(storePath);
        Regex attribute = new("(?<head>Protected=\")[^\"]*(?<tail>\")",
                              RegexOptions.ExplicitCapture, MatchTimeout);

        Assert.That(attribute.IsMatch(xml), Is.True, "the store was expected to declare a sentinel");

        File.WriteAllText(storePath, attribute.Replace(xml, "${head}bm90LWEtc2VudGluZWw=${tail}", 1));
    }

    /// <summary>
    /// The stored <c>Password</c> attribute of every connection, in document order.
    /// </summary>
    internal static string[] StoredPasswords(string storePath) =>
        [.. Regex.Matches(File.ReadAllText(storePath), "<Node[^>]*?\\sPassword=\"(?<value>[^\"]*)\"",
                          RegexOptions.ExplicitCapture, MatchTimeout)
                 .Select(m => m.Groups["value"].Value)];

    /// <summary>An authentication requestor that fails the test if it is ever called.</summary>
    internal static Optional<SecureString> NeverAsked()
    {
        Assert.Fail("the store should have opened without a prompt");
        return Optional<SecureString>.Empty;
    }

    /// <summary>The user pressing cancel at the recovery-password prompt.</summary>
    internal static Optional<SecureString> Cancelled() => Optional<SecureString>.Empty;

    private static string SecretFieldName(string secretName) => secretName switch
    {
        nameof(ConnectionInfo.Password) => "_password",
        nameof(ConnectionInfo.RDGatewayPassword) => "_rdGatewayPassword",
        nameof(ConnectionInfo.VNCProxyPassword) => "_vncProxyPassword",
        _ => throw new ArgumentOutOfRangeException(nameof(secretName), secretName, "No such secret.")
    };

    private static string PendingFieldName(string secretName) => secretName switch
    {
        nameof(ConnectionInfo.Password) => "_pendingPassword",
        nameof(ConnectionInfo.RDGatewayPassword) => "_pendingRdGatewayPassword",
        nameof(ConnectionInfo.VNCProxyPassword) => "_pendingVncProxyPassword",
        _ => throw new ArgumentOutOfRangeException(nameof(secretName), secretName, "No such secret.")
    };

    private static FieldInfo Field(string name) =>
        typeof(AbstractConnectionRecord).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException($"AbstractConnectionRecord no longer has a field '{name}'.");
}
