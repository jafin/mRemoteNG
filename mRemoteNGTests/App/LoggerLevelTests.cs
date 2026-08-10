using mRemoteNG.App;
using NUnit.Framework;

namespace mRemoteNGTests.App;

[TestFixture]
public class LoggerLevelTests
{
    private bool _originalWriteDebugMsgs;

    [SetUp]
    public void Setup() =>
        _originalWriteDebugMsgs = mRemoteNG.Properties.OptionsNotificationsPage.Default.TextLogMessageWriterWriteDebugMsgs;

    [TearDown]
    public void TearDown()
    {
        mRemoteNG.Properties.OptionsNotificationsPage.Default.TextLogMessageWriterWriteDebugMsgs = _originalWriteDebugMsgs;
        Logger.Instance.ApplyConfiguredLevel();
    }

    [Test]
    public void TheDefaultSettingSuppressesDebug()
    {
        // The setting's own default is false, which is where the log's default verbosity now comes
        // from. Previously the minimum was hardcoded to Verbose and nothing was ever filtered.
        Assert.That(new mRemoteNG.Properties.OptionsNotificationsPage().TextLogMessageWriterWriteDebugMsgs,
            Is.False, "the shipped default is what makes the log quiet");
    }

    [Test]
    public void EnablingDebugMessagesLowersTheLevel()
    {
        mRemoteNG.Properties.OptionsNotificationsPage.Default.TextLogMessageWriterWriteDebugMsgs = true;
        Logger.Instance.ApplyConfiguredLevel();

        Assert.That(Logger.Instance.WriteDebugMessages);
    }

    [Test]
    public void DisablingDebugMessagesRaisesTheLevel()
    {
        mRemoteNG.Properties.OptionsNotificationsPage.Default.TextLogMessageWriterWriteDebugMsgs = false;
        Logger.Instance.ApplyConfiguredLevel();

        Assert.That(Logger.Instance.WriteDebugMessages, Is.False);
    }

    [Test]
    public void TheLevelSurvivesALogPathChange()
    {
        Logger.Instance.WriteDebugMessages = true;

        // SetLogPath rebuilds the logger. A level read from configuration at build time would be
        // silently reverted here, which is why it lives on a switch the rebuild reuses.
        Logger.Instance.SetLogPath(Logger.DefaultLogPath);

        Assert.That(Logger.Instance.WriteDebugMessages);
    }
}
