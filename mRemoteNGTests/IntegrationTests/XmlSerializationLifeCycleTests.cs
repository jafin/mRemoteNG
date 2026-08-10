using System;
using System.Linq;
using System.Text;
using mRemoteNG.Config.Serializers.ConnectionSerializers.Xml;
using mRemoteNG.Connection;
using mRemoteNG.Container;
using mRemoteNG.Security;
using mRemoteNG.Security.Factories;
using mRemoteNG.Tree;
using mRemoteNG.Tree.Root;
using mRemoteNGTests.TestHelpers;
using NUnit.Framework;

namespace mRemoteNGTests.IntegrationTests;

public class XmlSerializationLifeCycleTests
{
    private XmlConnectionsSerializer _serializer;
    private XmlConnectionsDeserializer _deserializer;
    private ConnectionTreeModel _originalModel;
    private readonly CryptoProviderFactory _cryptoFactory = new CryptoProviderFactory(BlockCipherEngines.AES , BlockCipherModes.GCM);

    [SetUp]
    public void Setup()
    {
        _originalModel = SetupConnectionTreeModel();
        var cryptoProvider = _cryptoFactory.Build();
        var nodeSerializer = new XmlConnectionNodeSerializer28(
            cryptoProvider,
            _originalModel.RootNodes.OfType<RootNodeInfo>().First().PasswordString.ConvertToSecureString(),
            new SaveFilter());
        _serializer = new XmlConnectionsSerializer(cryptoProvider, nodeSerializer);
        _deserializer = new XmlConnectionsDeserializer();
    }

    [TearDown]
    public void Teardown()
    {
        _serializer = null;
    }

    private RootNodeInfo OriginalRoot => _originalModel.RootNodes.OfType<RootNodeInfo>().First();

    private static RootNodeInfo RootOf(ConnectionTreeModel model) =>
        model.RootNodes.OfType<RootNodeInfo>().First();

    [Test]
    public void AStoreWithNoRecordedLevelStaysClassicAcrossARoundTrip()
    {
        // Every file written before the level existed, and every file upstream mRemoteNG has ever
        // written, looks like this. Opening and saving must not change what it is.
        string serialized = _serializer.Serialize(_originalModel);

        Assert.Multiple(() =>
        {
            Assert.That(serialized, Does.Not.Contain(StorageFormat.AttributeName),
                "a classic store must come out byte-compatible with what upstream writes");
            Assert.That(RootOf(_deserializer.Deserialize(serialized)).StorageFormat,
                Is.EqualTo(StorageFormatLevel.Classic));
        });
    }

    [Test]
    public void AHardenedStoreStaysHardenedAcrossARoundTrip()
    {
        OriginalRoot.StorageFormat = StorageFormatLevel.Hardened;

        string serialized = _serializer.Serialize(_originalModel);

        Assert.That(RootOf(_deserializer.Deserialize(serialized)).StorageFormat,
            Is.EqualTo(StorageFormatLevel.Hardened));
    }

    [Test]
    public void SavingDoesNotRaiseTheLevelByItself()
    {
        // The level is a property of the store, never of the application version or a setting. A
        // save is ordinary work and must not decide it — that is the whole guarantee.
        string firstSave = _serializer.Serialize(_originalModel);
        ConnectionTreeModel reloaded = _deserializer.Deserialize(firstSave);

        Assert.That(RootOf(reloaded).StorageFormat, Is.EqualTo(StorageFormatLevel.Classic));

        string secondSave = _serializer.Serialize(reloaded);

        Assert.That(new XmlConnectionsDeserializer().Deserialize(secondSave), Is.Not.Null);
        Assert.That(secondSave, Does.Not.Contain(StorageFormat.AttributeName));
    }

    [Test]
    public void AnOverrideProducesAClassicCopyFromAHardenedStore()
    {
        // What Export relies on. The escape route has to survive the store being hardened.
        OriginalRoot.StorageFormat = StorageFormatLevel.Hardened;
        _serializer.StorageFormatOverride = StorageFormatLevel.Classic;

        string serialized = _serializer.Serialize(_originalModel);

        Assert.Multiple(() =>
        {
            Assert.That(serialized, Does.Not.Contain(StorageFormat.AttributeName));
            Assert.That(RootOf(_deserializer.Deserialize(serialized)).StorageFormat,
                Is.EqualTo(StorageFormatLevel.Classic));
        });
    }

    [Test]
    public void SerializeThenDeserialize()
    {
        var serializedContent = _serializer.Serialize(_originalModel);
        var deserializedModel = _deserializer.Deserialize(serializedContent);
        var nodeNamesFromDeserializedModel = deserializedModel.GetRecursiveChildList().Select(node => node.Name);
        var nodeNamesFromOriginalModel = _originalModel.GetRecursiveChildList().Select(node => node.Name);
        Assert.That(nodeNamesFromDeserializedModel, Is.EquivalentTo(nodeNamesFromOriginalModel));
    }

    [Test]
    public void SerializeThenDeserializeWithFullEncryption()
    {
        _serializer.UseFullEncryption = true;
        var serializedContent = _serializer.Serialize(_originalModel);
        var deserializedModel = _deserializer.Deserialize(serializedContent);
        var nodeNamesFromDeserializedModel = deserializedModel.GetRecursiveChildList().Select(node => node.Name);
        var nodeNamesFromOriginalModel = _originalModel.GetRecursiveChildList().Select(node => node.Name);
        Assert.That(nodeNamesFromDeserializedModel, Is.EquivalentTo(nodeNamesFromOriginalModel));
    }

    [Test]
    public void SerializeAndDeserializePropertiesWithInternationalCharacters()
    {
        var originalConnectionInfo = new ConnectionInfo {Name = "con1", Description = "£°úg¶┬ä" };
        var serializedContent = _serializer.Serialize(originalConnectionInfo);
        var deserializedModel = _deserializer.Deserialize(serializedContent);
        var deserializedConnectionInfo = deserializedModel.GetRecursiveChildList().First(node => string.Equals(node.Name, originalConnectionInfo.Name, StringComparison.Ordinal));
        Assert.That(deserializedConnectionInfo.Description, Is.EqualTo(originalConnectionInfo.Description));
    }

    [Test]
    public void SerializeAndDeserializeWithCustomKdfIterationsValue()
    {
        var cryptoProvider = _cryptoFactory.Build();
        cryptoProvider.KeyDerivationIterations = 5000;
        var nodeSerializer = new XmlConnectionNodeSerializer28(
            cryptoProvider,
            _originalModel.RootNodes.OfType<RootNodeInfo>().First().PasswordString.ConvertToSecureString(),
            new SaveFilter());
        _serializer = new XmlConnectionsSerializer(cryptoProvider, nodeSerializer);
        var serializedContent = _serializer.Serialize(_originalModel);
        var deserializedModel = _deserializer.Deserialize(serializedContent);
        var nodeNamesFromDeserializedModel = deserializedModel.GetRecursiveChildList().Select(node => node.Name);
        var nodeNamesFromOriginalModel = _originalModel.GetRecursiveChildList().Select(node => node.Name);
        Assert.That(nodeNamesFromDeserializedModel, Is.EquivalentTo(nodeNamesFromOriginalModel));
    }

    [Test]
    public void GuidCreatedIfNonExistedInXml()
    {
        var originalConnectionInfo = new ConnectionInfo { Name = "con1" };
        var serializedContent = _serializer.Serialize(originalConnectionInfo);

        // remove GUID from connection xml
        serializedContent = serializedContent.Replace(originalConnectionInfo.ConstantID, "");

        var deserializedModel = _deserializer.Deserialize(serializedContent);
        var deserializedConnectionInfo = deserializedModel.GetRecursiveChildList().First(node => string.Equals(node.Name, originalConnectionInfo.Name, StringComparison.Ordinal));
        Assert.That(Guid.TryParse(deserializedConnectionInfo.ConstantID, out var guid));
    }

    [Test]
    public void LinkedConnectionIdRoundTripsThroughSerialization()
    {
        var sourceConnectionInfo = new ConnectionInfo { Name = "source", Hostname = "source-host" };
        var linkedConnectionInfo = sourceConnectionInfo.Clone();
        linkedConnectionInfo.Name = "source-link";
        linkedConnectionInfo.LinkedConnectionId = sourceConnectionInfo.ConstantID;

        var rootNode = new RootNodeInfo(RootNodeType.Connection);
        rootNode.AddChild(sourceConnectionInfo);
        rootNode.AddChild(linkedConnectionInfo);
        var connectionTreeModel = new ConnectionTreeModel();
        connectionTreeModel.AddRootNode(rootNode);

        var serializedContent = _serializer.Serialize(connectionTreeModel);
        var deserializedModel = _deserializer.Deserialize(serializedContent);
        var deserializedLinkedConnectionInfo = deserializedModel.GetRecursiveChildList().First(node => string.Equals(node.Name, "source-link", StringComparison.Ordinal));

        Assert.That(deserializedLinkedConnectionInfo.LinkedConnectionId, Is.EqualTo(sourceConnectionInfo.ConstantID));
    }

    [Test]
    public void AllPropertiesCorrectWhenSerializingThenDeserializing()
    {
        var originalConnectionInfo = new ConnectionInfo().RandomizeValues();
        originalConnectionInfo.Inheritance.TurnOffInheritanceCompletely();
        var serializedContent = _serializer.Serialize(originalConnectionInfo);
        var deserializedModel = _deserializer.Deserialize(serializedContent);
        var deserializedConnectionInfo = deserializedModel
            .GetRecursiveChildList()
            .First(info => info.GetTreeNodeType() == TreeNodeType.Connection);

        var sb = new StringBuilder();
        foreach (var property in originalConnectionInfo.GetSerializableProperties())
        {
            if (string.Equals(property.Name, nameof(ConnectionInfo.Password), StringComparison.Ordinal))
                continue;

            var originalValue = property.GetValue(originalConnectionInfo);
            var deserializedValue = property.GetValue(deserializedConnectionInfo);
            if (originalValue.Equals(deserializedValue))
                continue;

            sb.AppendLine($"Property: {property.Name}");
        }

        Assert.That(sb.Length, Is.EqualTo(0), "Some properties are not being serialized properly:\n" + sb);
    }

    [Test]
    public void AllInheritanceCorrectWhenSerializingThenDeserializing()
    {
        var originalConnectionInfo = new ConnectionInfo();
        originalConnectionInfo.Inheritance.ToggleAllBooleanProperties(excludeProperties: nameof(ConnectionInfoInheritance.EverythingInherited));
        var container = new ContainerInfo();
        container.AddChild(originalConnectionInfo);

        var serializedContent = _serializer.Serialize(container);
        var deserializedModel = _deserializer.Deserialize(serializedContent);
        var deserializedConnectionInfo = deserializedModel
            .GetRecursiveChildList()
            .First(info => info.GetTreeNodeType() == TreeNodeType.Connection);

        var sb = new StringBuilder();
        foreach (var property in ConnectionInfoInheritance.GetProperties())
        {
            if (string.Equals(property.Name, nameof(originalConnectionInfo.Password), StringComparison.Ordinal))
                continue;

            var originalValue = property.GetValue(originalConnectionInfo.Inheritance);
            var deserializedValue = property.GetValue(deserializedConnectionInfo.Inheritance);

            if (originalValue.Equals(deserializedValue))
                continue;

            sb.AppendLine($"Property: Inheritance.{property.Name}");
        }

        Assert.That(sb.Length, Is.EqualTo(0), "Some properties are not being serialized properly:\n" + sb);
    }


    private static ConnectionTreeModel SetupConnectionTreeModel()
    {
        /*
         * Root
         * |--- con0
         * |--- folder1
         * |    L--- con1
         * L--- folder2
         *      |--- con2
         *      L--- folder3
         *           |--- con3
         *           L--- con4
         */
        var connectionTreeModel = new ConnectionTreeModel();
        var rootNode = new RootNodeInfo(RootNodeType.Connection);
        var folder1 = new ContainerInfo { Name = "folder1" };
        var folder2 = new ContainerInfo { Name = "folder2" };
        var folder3 = new ContainerInfo { Name = "folder3" };
        var con0 = new ConnectionInfo { Name = "con0" };
        var con1 = new ConnectionInfo { Name = "con1" };
        var con2 = new ConnectionInfo { Name = "con2" };
        var con3 = new ConnectionInfo { Name = "con3" };
        var con4 = new ConnectionInfo { Name = "con4" };
        rootNode.AddChild(folder1);
        rootNode.AddChild(folder2);
        rootNode.AddChild(con0);
        folder1.AddChild(con1);
        folder2.AddChild(con2);
        folder2.AddChild(folder3);
        folder3.AddChild(con3);
        folder3.AddChild(con4);
        connectionTreeModel.AddRootNode(rootNode);
        return connectionTreeModel;
    }
}