using System;
using mRemoteNG.Connection;
using mRemoteNG.Tree.Smart;
using NUnit.Framework;

namespace mRemoteNGTests.Tree.Smart;

[TestFixture]
public class SmartConnectionInfoTests
{
    [Test]
    public void Constructor_ShouldCopyPropertiesAndShareOpenConnections()
    {
        // Arrange
        var original = new ConnectionInfo
        {
            Name = "Test Connection",
            Hostname = "example.com"
        };

        // Act
        var smart = new SmartConnectionInfo(original);

        // Assert
        Assert.That(smart.Name, Is.EqualTo(original.Name));
        Assert.That(smart.Hostname, Is.EqualTo(original.Hostname));
        Assert.That(smart.OpenConnections, Is.SameAs(original.OpenConnections));
    }

    [Test]
    public void PropertyChanged_OnOriginal_ShouldUpdateSmartConnection()
    {
        // Arrange
        var original = new ConnectionInfo { Name = "Old Name" };
        var smart = new SmartConnectionInfo(original);
        bool fired = false;

        smart.PropertyChanged += (s, e) => {
            if (string.Equals(e.PropertyName, nameof(ConnectionInfo.Name), StringComparison.Ordinal)) fired = true;
        };

        // Act
        original.Name = "New Name";

        // Assert
        Assert.That(smart.Name, Is.EqualTo("New Name"));
        Assert.That(fired, Is.True);
    }
}