using System.Threading;
using mRemoteNG.Connection;
using mRemoteNG.Connection.Protocol;
using mRemoteNG.UI.Tabs;
using NUnit.Framework;

namespace mRemoteNGTests.Connection.Protocol;

[TestFixture]
[Apartment(ApartmentState.STA)]
public class PuttyBaseConnectTests
{
    private PuttyBase _puttyProtocol;
    private ConnectionTab _connectionTab;
    private InterfaceControl _interfaceControl;
    private string _originalPuttyPath;

    [SetUp]
    public void Setup()
    {
        _originalPuttyPath = PuttyBase.PuttyPath;
        _puttyProtocol = new PuttyBase();
        _connectionTab = new ConnectionTab();
        ConnectionInfo connectionInfo = new ConnectionInfo
        {
            Protocol = ProtocolType.SSH2,
            Name = "Test Connection",
            Hostname = "localhost"
        };
        _interfaceControl = new InterfaceControl(_connectionTab, _puttyProtocol, connectionInfo);
        _puttyProtocol.InterfaceControl = _interfaceControl;

        // Set PuttyPath to cmd.exe to simulate a process starting
        PuttyBase.PuttyPath = "cmd.exe";
    }

    [TearDown]
    public void TearDown()
    {
        _puttyProtocol?.Close();

        // Detach before disposing. Control.Dispose walks its Controls collection to dispose any
        // ActiveX children, reading Count and then indexing — and the InterfaceControl removes
        // itself from that same collection as it is disposed. The walk could therefore index a
        // collection that had just emptied, which is the intermittent
        // "index ('0') must be less than '0'" in this teardown. An empty collection is never
        // walked at all.
        _connectionTab?.Controls.Clear();

        _interfaceControl?.Dispose();
        _connectionTab?.Dispose();
        PuttyBase.PuttyPath = _originalPuttyPath;
    }

    [Test]
    public void Connect_ReturnsTrueImmediately()
    {
        // This test verifies that Connect() returns true without waiting for the window
        bool result = _puttyProtocol.Connect();
        Assert.That(result, Is.True, "Connect should return true immediately");
    }
}