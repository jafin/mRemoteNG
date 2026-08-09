using mRemoteNG.Messages;
using mRemoteNG.Messages.MessageFilteringOptions;
using mRemoteNG.Messages.MessageWriters;
using mRemoteNG.Messages.WriterDecorators;
using NUnit.Framework;

namespace mRemoteNGTests.Messages
{
    /// <summary>
    /// Covers "informational and warning messages are shown in the panel by default" in
    /// <c>specs/notification-panel/spec.md</c>.
    /// </summary>
    [TestFixture]
    public class NotificationPanelFilterDefaultsTests
    {
        private static readonly string[] LoudOnly = ["loud"];
        private static readonly string[] WarningOnly = ["warning"];

        [TestCase("NotificationPanelWriterWriteInfoMsgs", "True")]
        [TestCase("NotificationPanelWriterWriteWarningMsgs", "True")]
        [TestCase("NotificationPanelWriterWriteErrorMsgs", "True")]
        [TestCase("NotificationPanelWriterWriteDebugMsgs", "False")]
        public void TheShippedPanelDefaults(string settingName, string expected)
        {
            // Reads the declared default rather than the current user value, which a previous test
            // or a developer's own profile may have changed.
            var property = mRemoteNG.Properties.OptionsNotificationsPage.Default.Properties[settingName];

            Assert.That(property!.DefaultValue, Is.EqualTo(expected));
        }

        [Test]
        public void ThePanelAndTheLogAgreeOnInfoAndWarnings()
        {
            // These disagreeing was the defect: the product kept info and warning messages in the
            // log but showed the user only errors.
            var panel = mRemoteNG.Properties.OptionsNotificationsPage.Default.Properties;

            Assert.Multiple(() =>
            {
                Assert.That(panel["NotificationPanelWriterWriteInfoMsgs"]!.DefaultValue,
                            Is.EqualTo(panel["TextLogMessageWriterWriteInfoMsgs"]!.DefaultValue));
                Assert.That(panel["NotificationPanelWriterWriteWarningMsgs"]!.DefaultValue,
                            Is.EqualTo(panel["TextLogMessageWriterWriteWarningMsgs"]!.DefaultValue));
            });
        }

        [Test]
        public void LogOnlyMessagesNeverReachTheDecoratedWriter()
        {
            // OnlyLog remains the lever for keeping noisy diagnostics out of the panel now that the
            // class filters no longer do it.
            RecordingWriter recorder = new();
            OnlyLogMessageFilter filter = new(recorder);

            filter.Write(new Message(MessageClass.InformationMsg, "quiet", onlyLog: true));
            filter.Write(new Message(MessageClass.InformationMsg, "loud"));

            Assert.That(recorder.Written, Is.EqualTo(LoudOnly));
        }

        [Test]
        public void DisablingAClassStopsOnlyThatClass()
        {
            RecordingWriter recorder = new();
            StubFilteringOptions options = new() { AllowInfoMessages = false, AllowWarningMessages = true };
            MessageTypeFilterDecorator filter = new(options, recorder);

            filter.Write(new Message(MessageClass.InformationMsg, "info"));
            filter.Write(new Message(MessageClass.WarningMsg, "warning"));

            Assert.That(recorder.Written, Is.EqualTo(WarningOnly));
        }

        private sealed class RecordingWriter : IMessageWriter
        {
            public System.Collections.Generic.List<string> Written { get; } = [];

            public void Write(IMessage message) => Written.Add(message.Text);
        }

        private sealed class StubFilteringOptions : IMessageTypeFilteringOptions
        {
            public bool AllowDebugMessages { get; set; }
            public bool AllowInfoMessages { get; set; }
            public bool AllowWarningMessages { get; set; }
            public bool AllowErrorMessages { get; set; }
        }
    }
}
