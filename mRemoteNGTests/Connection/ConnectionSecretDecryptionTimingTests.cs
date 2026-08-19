using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security;
using System.Threading;
using System.Threading.Tasks;
using mRemoteNG.App.Info;
using mRemoteNG.Config.Connections;
using mRemoteNG.Config.Import;
using mRemoteNG.Config.Serializers.ConnectionSerializers.Xml;
using mRemoteNG.Connection;
using mRemoteNG.Security;
using mRemoteNG.Security.FileProtection;
using mRemoteNG.Tools;
using mRemoteNG.Tree;
using mRemoteNG.Tree.Root;
using NUnit.Framework;

namespace mRemoteNGTests.Connection;

/// <summary>
/// When a connection file's secrets are decrypted.
/// </summary>
/// <remarks>
/// <para>
/// Opening a file used to decrypt every password in it before the load returned, so a store of two
/// hundred connections put two hundred passwords into the process — for the lifetime of the session
/// — to serve the two the user was going to open. It also produced them as a <c>string[]</c> first:
/// immutable, unzeroable, and alive until the garbage collector happened to take it.
/// </para>
/// <para>
/// This fixture asserts the opposite, one claim at a time: nothing is decrypted by the load, the
/// first read of a secret decrypts it, a second read does not, and a connection nobody opens never
/// has its password in the process at all.
/// </para>
/// </remarks>
[TestFixture]
public class ConnectionSecretDecryptionTimingTests
{
    private const int FastIterations = 1000;
    private static readonly string[] EverySecretInTheStore = ["first-secret", "second-secret", "third-secret"];
    private string _directory = "";
    private string _storePath = "";

    [SetUp]
    public void Setup()
    {
        _directory = Path.Combine(Path.GetTempPath(), "mrng-secret-timing-" + TestContext.CurrentContext.Test.ID);
        Directory.CreateDirectory(_directory);
        _storePath = Path.Combine(_directory, "confCons.xml");
        PortableEdition.OverrideForTests(false);
    }

    [TearDown]
    public void Teardown()
    {
        RecoveryPasswordSession.Clear();
        MachineSlotSession.Forget();
        PortableEdition.OverrideForTests(null);
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, true);
    }

    [Test]
    public void LoadingAFileDecryptsNothingInIt()
    {
        // Read the SecureString field rather than the property: the getter is where the decryption
        // now happens, so asking it would say nothing about when the work was done. Nothing touches
        // a password between the load and this read, so a field holding a value could only have been
        // filled by the load.
        SaveStoreWithPasswords();

        ConnectionTreeModel opened = Reopen();

        Assert.That(Connections(opened).Select(StoredSecureString), Is.All.Null,
            "no password was in memory before anything asked for one");
    }

    [Test]
    public void ASecretIsDecryptedOnFirstReadAndHeldAsASecureString()
    {
        SaveStoreWithPasswords();
        ConnectionInfo connection = Connections(Reopen()).First();

        Assert.That(connection.Password, Is.EqualTo("first-secret"));

        SecureString? stored = StoredSecureString(connection);
        Assert.Multiple(() =>
        {
            Assert.That(stored, Is.Not.Null, "the read left the value on the record");
            Assert.That(stored!.Length, Is.EqualTo("first-secret".Length));
            Assert.That(PendingSecret(connection, "_pendingPassword"), Is.Null,
                "and nothing is left waiting to be decrypted a second time");
        });
    }

    [Test]
    public void AConnectionNobodyOpensNeverHasItsPasswordDecrypted()
    {
        // The point of the whole change, stated as a user meets it: a file of two hundred
        // connections should cost the two passwords the user actually asks for, not two hundred.
        SaveStoreWithPasswords();
        ConnectionInfo[] connections = Connections(Reopen());

        _ = connections[0].Password;

        Assert.Multiple(() =>
        {
            Assert.That(StoredSecureString(connections[1]), Is.Null);
            Assert.That(StoredSecureString(connections[2]), Is.Null);
        });
    }

    [Test]
    public void EverySecretIsStillReadableOneAtATime()
    {
        SaveStoreWithPasswords();

        Assert.That(Connections(Reopen()).Select(c => c.Password), Is.EqualTo(EverySecretInTheStore));
    }

    [Test]
    public void BothDoorsToOneSecretGiveTheSameAnswer()
    {
        // Two ways to read a password and only one of them lazy would answer differently depending
        // on which a caller happened to use.
        SaveStoreWithPasswords();
        ConnectionInfo[] connections = Connections(Reopen());

        using SecureString throughTheSecureStringAccessor = connections[1].SecurePassword;

        Assert.Multiple(() =>
        {
            Assert.That(throughTheSecureStringAccessor.ConvertToUnsecureString(), Is.EqualTo("second-secret"));
            Assert.That(connections[1].Password, Is.EqualTo("second-secret"));
        });
    }

    [Test]
    public void ASecretIsDecryptedOnceHoweverManyTimesItIsRead()
    {
        int decryptions = 0;
        ConnectionInfo connection = new() { Name = "counted" };
        connection.SetPendingSecret(nameof(ConnectionInfo.Password),
            new PendingConnectionSecret("ciphertext", AnyKey(), _ => { decryptions++; return "plain"; }));

        _ = connection.Password;
        using (SecureString _ = connection.SecurePassword) { }
        _ = connection.Password;

        Assert.That(decryptions, Is.EqualTo(1));
    }

    [Test]
    public void TwoThreadsAskingAtOnceDecryptOnceAndBothGetTheAnswer()
    {
        // The tree is read from the user interface thread and from the host-status monitor. Two
        // threads decrypting the same record would each build a SecureString, and one of the two
        // would be dropped on the floor undisposed.
        int decryptions = 0;
        using ManualResetEventSlim start = new();
        ConnectionInfo connection = new() { Name = "raced" };
        connection.SetPendingSecret(nameof(ConnectionInfo.Password),
            new PendingConnectionSecret("ciphertext", AnyKey(), _ =>
            {
                Interlocked.Increment(ref decryptions);
                Thread.Sleep(20);
                return "plain";
            }));

        string[] answers = new string[8];
        Task[] readers = [.. Enumerable.Range(0, answers.Length).Select(i => Task.Run(() =>
        {
            start.Wait();
            answers[i] = connection.Password;
        }))];

        start.Set();
        Task.WaitAll(readers);

        Assert.Multiple(() =>
        {
            Assert.That(decryptions, Is.EqualTo(1));
            Assert.That(answers, Is.All.EqualTo("plain"));
        });
    }

    [Test]
    public void AssigningASecretThrowsAwayTheCiphertextItWasLoadedWith()
    {
        // Otherwise a connection edited before its password was ever read would decrypt over the top
        // of what the user typed the next time anything asked.
        ConnectionInfo connection = new() { Name = "edited" };
        connection.SetPendingSecret(nameof(ConnectionInfo.Password),
            new PendingConnectionSecret("ciphertext", AnyKey(), _ => "from-the-file"));

        connection.Password = "typed-by-the-user";

        Assert.That(connection.Password, Is.EqualTo("typed-by-the-user"));
    }

    [Test]
    public void ASecretThatCannotBeDecryptedIsReportedAndNeverResolvesToEmpty()
    {
        // An empty secret is not a neutral answer: several callers read empty as "no password
        // configured" and fall through to the configured default, so a failure answered that way
        // would send the wrong credentials to a host rather than reporting anything.
        SaveStoreWithPasswords();
        CorruptTheStoredPasswordOf("two");
        ConnectionInfo[] connections = Connections(Reopen());

        ConnectionSecretDecryptionException? failure =
            Assert.Throws<ConnectionSecretDecryptionException>(() => _ = connections[1].Password);

        Assert.Multiple(() =>
        {
            Assert.That(failure!.ConnectionName, Is.EqualTo("two"), "reported against the connection it belongs to");
            Assert.That(failure.SecretName, Is.EqualTo(nameof(ConnectionInfo.Password)));
        });
    }

    [Test]
    public void OneUnreadableSecretLeavesEveryOtherConnectionUsable()
    {
        SaveStoreWithPasswords();
        CorruptTheStoredPasswordOf("two");
        ConnectionInfo[] connections = Connections(Reopen());

        Assert.Multiple(() =>
        {
            Assert.That(connections[0].Password, Is.EqualTo("first-secret"));
            Assert.That(connections[2].Password, Is.EqualTo("third-secret"));
            Assert.Throws<ConnectionSecretDecryptionException>(() => _ = connections[1].Password);
        });
    }

    [Test]
    public void AFailedDecryptIsReportedOncePerSecretHoweverOftenItIsRead()
    {
        // The property grid re-reads a displayed connection constantly. A message per repaint over
        // one broken field would bury everything else in the notification panel.
        int reports = 0;
        ConnectionInfo connection = new() { Name = "broken" };
        connection.SetPendingSecret(nameof(ConnectionInfo.Password),
            new PendingConnectionSecret("ciphertext", AnyKey(),
                                        _ => throw new InvalidOperationException("no")));

        using (CountingMessages(() => reports++))
        {
            for (int i = 0; i < 5; i++)
                Assert.Throws<ConnectionSecretDecryptionException>(() => _ = connection.Password);
        }

        Assert.That(reports, Is.EqualTo(1));
    }

    [Test]
    public void AKeyThatOpensNothingIsCaughtWhileTheFileIsOpened()
    {
        // Deferring secrets must not turn one honest "this file did not open" into one failure per
        // connection. The root sentinel is still decrypted by the load - it is the one ciphertext
        // whose plaintext is known in advance - and that is what catches a key that is simply wrong,
        // before any connection is asked about.
        SaveStoreWithPasswords();
        CorruptTheSentinel();

        Exception? thrown = Assert.Catch(() => new XmlConnectionsDeserializer(_storePath, Cancelled)
                                                   .Deserialize(File.ReadAllText(_storePath)));

        Assert.Multiple(() =>
        {
            Assert.That(thrown, Is.Not.Null, "the store must not open on a key that decrypts nothing");
            Assert.That(thrown, Is.Not.InstanceOf<ConnectionSecretDecryptionException>(),
                        "reported while opening the file, not once per connection in it");
        });
    }

    [Test]
    public void AskingWhetherAConnectionHasAPasswordDoesNotDecryptOneThatIsNotThere()
    {
        // CredentialImportHelper asks this of every node in a file being imported. A record with
        // nothing stored and nowhere to borrow from has to answer without any work at all.
        ConnectionInfo connection = new() { Name = "bare" };

        Assert.That(CredentialImportHelper.HasCredentials(connection), Is.False);
    }

    private static ConnectionSecretKeyIdentity AnyKey() =>
        ConnectionSecretKeyIdentity.For(new mRemoteNG.Security.SymmetricEncryption.LegacyRijndaelCryptographyProvider(),
                                        fileKey: null, password: "any");

    /// <summary>
    /// Counts messages reaching the collector for as long as it is held.
    /// </summary>
    private static Unsubscriber CountingMessages(Action onMessage)
    {
        void Handler(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs args)
        {
            if (args.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Add)
                onMessage();
        }

        mRemoteNG.App.Runtime.MessageCollector.CollectionChanged += Handler;
        return new Unsubscriber(() => mRemoteNG.App.Runtime.MessageCollector.CollectionChanged -= Handler);
    }

    private sealed class Unsubscriber(Action dispose) : IDisposable
    {
        public void Dispose() => dispose();
    }

    private void SaveStoreWithPasswords()
    {
        using ConnectionFileKey fileKey = ConnectionFileKey.Generate();
        ConnectionTreeModel model = new();
        RootNodeInfo root = new(RootNodeType.Connection)
        {
            StorageFormat = StorageFormatLevel.Hardened,
            FileKey = ConnectionFileKey.FromBytes(fileKey.Bytes),
            KeyProtection = ConnectionFileKeyProtection.Create(
                fileKey, "recovery".ConvertToSecureString(), includeMachineProtector: true, FastIterations)
        };

        root.AddChild(new ConnectionInfo { Name = "one", Password = "first-secret" });
        root.AddChild(new ConnectionInfo { Name = "two", Password = "second-secret" });
        root.AddChild(new ConnectionInfo { Name = "three", Password = "third-secret" });
        model.AddRootNode(root);

        new XmlConnectionsSaver(_storePath, new SaveFilter()).Save(model);
    }

    /// <summary>
    /// Replaces one connection's stored password with something that is not a ciphertext, leaving
    /// the rest of the file exactly as it was.
    /// </summary>
    private void CorruptTheStoredPasswordOf(string connectionName)
    {
        string xml = File.ReadAllText(_storePath);
        System.Text.RegularExpressions.Regex attribute = new(
            $"(?<head>Name=\"{connectionName}\"[^>]*?Password=\")[^\"]*(?<tail>\")",
            System.Text.RegularExpressions.RegexOptions.ExplicitCapture, TimeSpan.FromSeconds(5));

        Assert.That(attribute.IsMatch(xml), Is.True,
            "the store was expected to hold this password as a readable attribute");

        File.WriteAllText(_storePath, attribute.Replace(xml, "${head}bm90LWEtY2lwaGVydGV4dA==${tail}", 1));
    }

    /// <summary>
    /// Breaks the one ciphertext whose plaintext the loader knows in advance, so that no key can be
    /// accepted for this store.
    /// </summary>
    private void CorruptTheSentinel()
    {
        string xml = File.ReadAllText(_storePath);
        System.Text.RegularExpressions.Regex attribute = new(
            "(?<head>Protected=\")[^\"]*(?<tail>\")",
            System.Text.RegularExpressions.RegexOptions.ExplicitCapture, TimeSpan.FromSeconds(5));

        Assert.That(attribute.IsMatch(xml), Is.True, "the store was expected to declare a sentinel");

        File.WriteAllText(_storePath, attribute.Replace(xml, "${head}bm90LWEtc2VudGluZWw=${tail}", 1));
    }

    private ConnectionTreeModel Reopen() =>
        new XmlConnectionsDeserializer(_storePath, NeverAsked)
            .Deserialize(File.ReadAllText(_storePath));

    private static SecureString? StoredSecureString(ConnectionInfo connection) =>
        (SecureString?)typeof(AbstractConnectionRecord)
            .GetField("_password", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(connection);

    private static object? PendingSecret(ConnectionInfo connection, string fieldName) =>
        typeof(AbstractConnectionRecord)
            .GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(connection);

    private static ConnectionInfo[] Connections(ConnectionTreeModel model) =>
        [.. model.RootNodes.OfType<RootNodeInfo>().First().Children];

    private static Optional<SecureString> NeverAsked()
    {
        Assert.Fail("the machine protector should have opened the store");
        return Optional<SecureString>.Empty;
    }

    /// <summary>The user pressing cancel at the recovery-password prompt.</summary>
    private static Optional<SecureString> Cancelled() => Optional<SecureString>.Empty;
}
