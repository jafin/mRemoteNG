using System.Collections.Generic;
using mRemoteNG.Connection;
using mRemoteNG.Connection.Protocol;
using mRemoteNG.UI.Window;
using NUnit.Framework;

namespace mRemoteNGTests.UI.Window.ConfigWindowTests;

public abstract class ConfigWindowSpecialTestsBase
{
    protected abstract ProtocolType Protocol { get; }
    protected bool TestAgainstContainerInfo { get; set; }
    protected ConfigWindow ConfigWindow { get; set; }
    protected ConnectionInfo ConnectionInfo { get; set; }
    protected List<string> ExpectedPropertyList { get; set; }

    [SetUp]
    public virtual void Setup()
    {
        ConnectionInfo = ConfigWindowGeneralTests.ConstructConnectionInfo(Protocol, TestAgainstContainerInfo);
        ExpectedPropertyList = ConfigWindowGeneralTests.BuildExpectedConnectionInfoPropertyList(Protocol, TestAgainstContainerInfo);

        ConfigWindow = new ConfigWindow();
    }

    public void RunVerification()
    {
        ConfigWindow.SelectedTreeNode = ConnectionInfo;
        Assert.That(
            ConfigWindow.VisibleObjectProperties,
            Is.EquivalentTo(ExpectedPropertyList));
    }
}