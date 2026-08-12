using System;
using System.IO;
using System.Security;
using System.Text;
using mRemoteNG.Config.Connections;
using mRemoteNG.Config.Serializers.ConnectionSerializers.Xml;
using mRemoteNG.Security;
using mRemoteNG.Tools;
using mRemoteNG.Tree;
using mRemoteNGTests.Properties;
using NUnit.Framework;

namespace mRemoteNGTests.Config.Serializers.ConnectionSerializers.Xml;

/// <summary>
/// A store declaring a format level this build does not recognise is refused rather than read.
/// </summary>
/// <remarks>
/// The failure this guards against is silent: reading an unrecognised level as classic discards a
/// statement a newer build made deliberately, and the next ordinary save writes the file back
/// without it. Nothing writes such a level today, which is exactly why the rule has to ship now —
/// it must be in the build that precedes the one introducing a level, or the first store written at
/// that level meets an older build that flattens it.
/// </remarks>
[TestFixture]
public class XmlConnectionsDeserializerStorageFormatTests
{
    private const string UnknownLevel = "Quantum";

    [Test]
    public void AnUnrecognisedLevelIsRefusedRatherThanReadAsClassic()
    {
        string confCons = WithStorageFormat(Resources.confCons_v2_6, UnknownLevel);
        XmlConnectionsDeserializer deserializer = new("", NeverCalled);

        NotSupportedException? thrown = Assert.Throws<NotSupportedException>(
            () => deserializer.Deserialize(confCons));

        Assert.That(thrown!.Message, Does.Contain(UnknownLevel),
            "the refusal names the level it could not resolve");
    }

    [Test]
    public void NoPasswordIsRequestedForAStoreThatWasNeverGoingToOpen()
    {
        // The point of refusing before CreateDecryptor. A prompt on a file that cannot open teaches
        // the user their password is wrong — the same misdiagnosis a hardened file produces in
        // upstream mRemoteNG, and the reason that failure mode is worth avoiding here.
        int requests = 0;

        XmlConnectionsDeserializer deserializer = new("", () =>
        {
            requests++;
            return "irrelevant".ConvertToSecureString();
        });

        Assert.Throws<NotSupportedException>(
            () => deserializer.Deserialize(WithStorageFormat(Resources.confCons_v2_6, UnknownLevel)));

        Assert.That(requests, Is.Zero, "the authentication requestor was never invoked");
    }

    [TestCase("<?xml version=\"1.0\" encoding=\"UTF-8\"?>", TestName = "UppercaseEncoding")]
    [TestCase("<?xml version=\"1.0\" encoding=\"utf-8\" standalone=\"yes\"?>", TestName = "StandaloneAttribute")]
    [TestCase("", TestName = "NoDeclarationAtAll")]
    public void ADeclarationLegacyDecryptDoesNotRecogniseIsStillRefusedAsNewerNotCorrupt(string declaration)
    {
        // LegacyFullFileDecrypt runs before the root node is read, and returns its input untouched
        // only when that input contains the exact declaration <?xml version="1.0" encoding="utf-8"?>
        // — case-insensitively, so an uppercase encoding is fine. A standalone attribute or no
        // declaration at all is not: the file goes through a decrypt attempt that mangles it, and
        // the load then fails as "Failed to parse XML connection file". That reports a newer-format
        // store as a corrupt one, which is the second of the two misdiagnoses task 2.2 rules out.
        string confCons = WithStorageFormat(Resources.confCons_v2_6, UnknownLevel);
        int firstElement = confCons.IndexOf("<Connections", StringComparison.Ordinal);
        string rebuilt = declaration + confCons[firstElement..];

        int requests = 0;
        XmlConnectionsDeserializer deserializer = new("", () =>
        {
            requests++;
            return "irrelevant".ConvertToSecureString();
        });

        NotSupportedException? thrown = Assert.Throws<NotSupportedException>(
            () => deserializer.Deserialize(rebuilt));

        Assert.Multiple(() =>
        {
            Assert.That(thrown!.Message, Does.Contain(UnknownLevel),
                "refused as a newer-format store, not as unparseable XML");
            Assert.That(requests, Is.Zero, "the authentication requestor was never invoked");
        });
    }

    [Test]
    public void ADoctypeStoreIsRejectedOutrightRatherThanReadAsClassic()
    {
        // The preflight reader prohibits DTD, exactly as SecureXmlHelper does, so a DOCTYPE throws
        // and the preflight declines to judge the file. That is deliberate: such a file cannot load
        // at all — the real parser rejects it too (see SecureXmlHelperTests' XXE cases) — so
        // reporting it as "written by a newer version" would send the user to upgrade something
        // that still would not open. Loosening the preflight to DtdProcessing.Ignore would make it
        // more permissive than the parser it runs ahead of.
        //
        // What must hold regardless of which message wins: the store is never read, and never
        // silently treated as classic.
        string confCons = WithStorageFormat(Resources.confCons_v2_6, UnknownLevel);
        int declEnd = confCons.IndexOf("?>", StringComparison.Ordinal) + 2;
        string rebuilt = confCons[..declEnd] +
                         "\n<!DOCTYPE Connections [<!ENTITY x \"y\">]>" +
                         confCons[declEnd..];

        int requests = 0;
        XmlConnectionsDeserializer deserializer = new("", () =>
        {
            requests++;
            return "irrelevant".ConvertToSecureString();
        });

        Assert.Multiple(() =>
        {
            Assert.That(() => deserializer.Deserialize(rebuilt), Throws.Exception,
                "a DTD-bearing store is refused rather than opened");
            Assert.That(requests, Is.Zero, "and no password is requested for it");
        });
    }

    [TestCase("")]
    [TestCase("Hardened")]
    public void TheTwoLevelsThatExistStillOpen(string level)
    {
        // The property the change must not disturb. An absent declaration is every file upstream
        // mRemoteNG has ever written; if this check reached those, it would lock users out of their
        // own connections rather than protect them.
        string confCons = level.Length == 0
            ? Resources.confCons_v2_6
            : WithStorageFormat(Resources.confCons_v2_6, level);

        XmlConnectionsDeserializer deserializer = new("", NeverCalled);

        ConnectionTreeModel model = deserializer.Deserialize(confCons);

        Assert.That(model.RootNodes, Is.Not.Empty);
    }

    [Test]
    public void ARefusedStoreIsNotWrittenOver()
    {
        // Belt and braces for the save side. FileDataProviderWithRollingBackup copies the file
        // before writing, so a save that should not have happened destroys the original *and*
        // spends a backup slot on the result — neither is recoverable.
        string directory = Path.Combine(Path.GetTempPath(), "mRemoteNGTests-fmt-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string file = Path.Combine(directory, "confCons.xml");

        try
        {
            File.WriteAllText(file, WithStorageFormat(Resources.confCons_v2_6, UnknownLevel), Encoding.UTF8);
            byte[] before = File.ReadAllBytes(file);

            XmlConnectionsSaver saver = new(file, new SaveFilter());

            Assert.Throws<NotSupportedException>(() => saver.Save(new ConnectionTreeModel()));

            Assert.Multiple(() =>
            {
                Assert.That(File.ReadAllBytes(file), Is.EqualTo(before), "the file on disk is byte-identical");
                Assert.That(Directory.GetFiles(directory), Has.Length.EqualTo(1), "no backup slot was spent");
            });
        }
        finally
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A leftover temp directory is not worth failing a green test over.
            }
        }
    }

    [Test]
    public void AStoreAtARecognisedLevelIsStillWritable()
    {
        // The guard must not turn into "saving is refused", which would be a far worse defect than
        // the one it prevents.
        string directory = Path.Combine(Path.GetTempPath(), "mRemoteNGTests-fmt-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string file = Path.Combine(directory, "confCons.xml");

        try
        {
            File.WriteAllText(file, Resources.confCons_v2_6, Encoding.UTF8);

            XmlConnectionsDeserializer deserializer = new("", NeverCalled);
            ConnectionTreeModel model = deserializer.Deserialize(Resources.confCons_v2_6);

            XmlConnectionsSaver saver = new(file, new SaveFilter());

            Assert.DoesNotThrow(() => saver.Save(model));
        }
        finally
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A leftover temp directory is not worth failing a green test over.
            }
        }
    }

    /// <summary>Stamps a level onto the root element of a real connection file.</summary>
    private static string WithStorageFormat(string confCons, string level)
    {
        // Upstream writes a bare <Connections>; this fork writes it namespaced as
        // <mrng:Connections>. Both shapes have to be stampable or the fixture silently tests nothing.
        int rootStart = confCons.IndexOf("<mrng:Connections", StringComparison.Ordinal);
        if (rootStart < 0)
            rootStart = confCons.IndexOf("<Connections", StringComparison.Ordinal);

        // Zero is a legitimate position — a document with no XML declaration starts with its root.
        Assert.That(rootStart, Is.GreaterThanOrEqualTo(0), "the fixture's root element was not found");

        int rootEnd = confCons.IndexOf('>', rootStart);

        Assert.That(rootEnd, Is.GreaterThan(rootStart), "the fixture's root element is unterminated");

        return confCons[..rootEnd] +
               $" {StorageFormat.AttributeName}=\"{level}\"" +
               confCons[rootEnd..];
    }

    private static Optional<SecureString> NeverCalled()
    {
        Assert.Fail("no password should have been requested");
        return new SecureString();
    }
}
