using System;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using mRemoteNG.Messages;
using mRemoteNG.Tools.Cmdline;
using NUnit.Framework;

namespace mRemoteNGTests.Tools.Cmdline;

[SupportedOSPlatform("windows")]
public class StartupArgumentsInterpreterTests
{
    private static readonly string[] ConnectArgs = ["mRemoteNG.exe", "--connect", "ConnA"];
    private static readonly string[] StartupArgs = ["mRemoteNG.exe", "--startup", "ConnA"];

    [SetUp]
    public void SetUp()
    {
        StartupArgumentsInterpreter.ResetConnectionArgs();
    }

    #region --connect

    [Test]
    public void ParseArguments_SetsConnectTo_WhenConnectArgumentIsProvided()
    {
        var sut = CreateSut();

        sut.ParseArguments(ConnectArgs);

        Assert.That(StartupArgumentsInterpreter.ConnectTo, Is.EqualTo("ConnA"));
        Assert.That(StartupArgumentsInterpreter.StartupConnectTo, Is.Null);
    }

    #endregion

    #region --startup

    [Test]
    public void ParseArguments_SetsStartupConnectTo_WhenStartupArgumentIsProvided()
    {
        var sut = CreateSut();

        sut.ParseArguments(StartupArgs);

        Assert.That(StartupArgumentsInterpreter.StartupConnectTo, Is.EqualTo("ConnA"));
        Assert.That(StartupArgumentsInterpreter.ConnectTo, Is.Null);
    }

    #endregion

    #region --quickconnect

    [Test]
    public void ParseArguments_SetsQuickConnectTo_WhenQuickConnectProvided()
    {
        var sut = CreateSut();

        sut.ParseArguments(["mRemoteNG.exe", "--quickconnect", "server1.example.com"]);

        Assert.That(StartupArgumentsInterpreter.QuickConnectTo, Is.EqualTo("server1.example.com"));
    }

    [Test]
    public void ParseArguments_SetsQuickConnectTo_WhenQcShorthandProvided()
    {
        var sut = CreateSut();

        sut.ParseArguments(["mRemoteNG.exe", "--qc", "myhost"]);

        Assert.That(StartupArgumentsInterpreter.QuickConnectTo, Is.EqualTo("myhost"));
    }

    [Test]
    public void ParseArguments_SetsProtocol_WhenProtocolProvided()
    {
        var sut = CreateSut();

        sut.ParseArguments(["mRemoteNG.exe", "--quickconnect", "server1", "--protocol", "SSH2"]);

        Assert.That(StartupArgumentsInterpreter.QuickConnectTo, Is.EqualTo("server1"));
        Assert.That(StartupArgumentsInterpreter.QuickConnectProtocol, Is.EqualTo("SSH2"));
    }

    [Test]
    public void ParseArguments_SetsProtocol_WhenPShorthandProvided()
    {
        var sut = CreateSut();

        sut.ParseArguments(["mRemoteNG.exe", "--qc", "server1", "--p", "VNC"]);

        Assert.That(StartupArgumentsInterpreter.QuickConnectProtocol, Is.EqualTo("VNC"));
    }

    [Test]
    public void ParseArguments_QuickConnectWithoutProtocol_LeavesProtocolNull()
    {
        var sut = CreateSut();

        sut.ParseArguments(["mRemoteNG.exe", "--quickconnect", "server1"]);

        Assert.That(StartupArgumentsInterpreter.QuickConnectTo, Is.EqualTo("server1"));
        Assert.That(StartupArgumentsInterpreter.QuickConnectProtocol, Is.Null);
    }

    #endregion

    #region --exitafter

    [Test]
    public void ParseArguments_SetsExitAfterLastConnection_WhenExitAfterProvided()
    {
        var sut = CreateSut();

        sut.ParseArguments(["mRemoteNG.exe", "--exitafter"]);

        Assert.That(StartupArgumentsInterpreter.ExitAfterLastConnection, Is.True);
    }

    [Test]
    public void ParseArguments_ExitAfterDefaultsFalse()
    {
        var sut = CreateSut();

        sut.ParseArguments(["mRemoteNG.exe"]);

        Assert.That(StartupArgumentsInterpreter.ExitAfterLastConnection, Is.False);
    }

    #endregion

    #region ResetConnectionArgs

    [Test]
    public void ResetConnectionArgs_ClearsAllProperties()
    {
        var sut = CreateSut();
        sut.ParseArguments(["mRemoteNG.exe", "--connect", "A", "--quickconnect", "B", "--protocol", "RDP", "--exitafter"]);

        StartupArgumentsInterpreter.ResetConnectionArgs();

        Assert.That(StartupArgumentsInterpreter.ConnectTo, Is.Null);
        Assert.That(StartupArgumentsInterpreter.StartupConnectTo, Is.Null);
        Assert.That(StartupArgumentsInterpreter.QuickConnectTo, Is.Null);
        Assert.That(StartupArgumentsInterpreter.QuickConnectProtocol, Is.Null);
        Assert.That(StartupArgumentsInterpreter.CustomConnectionFile, Is.Null);
        Assert.That(StartupArgumentsInterpreter.ExitAfterLastConnection, Is.False);
    }

    #endregion

    #region Combined arguments

    [Test]
    public void ParseArguments_MultipleArgs_SetsAll()
    {
        var sut = CreateSut();

        sut.ParseArguments(["mRemoteNG.exe", "--connect", "ConnA", "--exitafter"]);

        Assert.That(StartupArgumentsInterpreter.ConnectTo, Is.EqualTo("ConnA"));
        Assert.That(StartupArgumentsInterpreter.ExitAfterLastConnection, Is.True);
    }

    #endregion

    #region --cons

    [Test]
    public void ParseArguments_SetsCustomConnectionFile_WhenConsIsAnAbsolutePathAsASeparateArgument()
    {
        // The case the parsing fix exists for: before it, the drive letter split the argument and
        // cons was given the string "true", so the switch was silently dropped and whatever store
        // happened to be discovered opened instead.
        string connectionsFile = Path.Combine(Path.GetTempPath(),
            "mRemoteNG_StartupArgs_" + Guid.NewGuid().ToString("N") + ".xml");
        File.WriteAllText(connectionsFile, "<connections />");

        try
        {
            CreateSut().ParseArguments(["mRemoteNG.exe", "--cons", connectionsFile]);

            Assert.That(StartupArgumentsInterpreter.CustomConnectionFile, Is.EqualTo(connectionsFile));
        }
        finally
        {
            File.Delete(connectionsFile);
        }
    }

    [Test]
    public void ParseArguments_KeepsTheRequestedPath_WhenTheFileDoesNotExist()
    {
        // Not a fallback to the usual store. A path that is not there is carried through so that
        // loading fails against the file the user named, which is what raises the "connection file
        // not found" dialog for it. Substituting a different store silently is the half of this
        // defect that let people edit and save into a file they never asked to open.
        string missing = Path.Combine(Path.GetTempPath(),
            "mRemoteNG_StartupArgs_missing_" + Guid.NewGuid().ToString("N") + ".xml");
        MessageCollector collector = new();

        new StartupArgumentsInterpreter(collector).ParseArguments(["mRemoteNG.exe", "--cons", missing]);

        Assert.Multiple(() =>
        {
            Assert.That(StartupArgumentsInterpreter.CustomConnectionFile, Is.EqualTo(missing));
            Assert.That(collector.Messages.Any(m => m.Text.Contains(missing, StringComparison.Ordinal)),
                Is.True, "the message must name the path the user typed, not a placeholder");
        });
    }

    [Test]
    public void ParseArguments_IgnoresConsWithNoPath()
    {
        // `--cons` on its own carries the flag placeholder, not a path. Honouring that would open a
        // "connection file not found" dialog about a file called "true"; nothing was asked for, so
        // the usual store opens and the message says the switch needs a path.
        MessageCollector collector = new();

        new StartupArgumentsInterpreter(collector).ParseArguments(["mRemoteNG.exe", "--cons"]);

        Assert.Multiple(() =>
        {
            Assert.That(StartupArgumentsInterpreter.CustomConnectionFile, Is.Null);
            Assert.That(collector.Messages.Any(m => m.Text.Contains("needs a path", StringComparison.Ordinal)), Is.True);
        });
    }

    #endregion

    #region Constructor

    [Test]
    public void Constructor_NullMessageCollector_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new StartupArgumentsInterpreter(null!));
    }

    #endregion

    #region No arguments

    [Test]
    public void ParseArguments_NoArgs_LeavesAllNull()
    {
        var sut = CreateSut();

        sut.ParseArguments(["mRemoteNG.exe"]);

        Assert.That(StartupArgumentsInterpreter.ConnectTo, Is.Null);
        Assert.That(StartupArgumentsInterpreter.StartupConnectTo, Is.Null);
        Assert.That(StartupArgumentsInterpreter.QuickConnectTo, Is.Null);
    }

    #endregion

    private static StartupArgumentsInterpreter CreateSut()
    {
        return new StartupArgumentsInterpreter(new MessageCollector());
    }
}
