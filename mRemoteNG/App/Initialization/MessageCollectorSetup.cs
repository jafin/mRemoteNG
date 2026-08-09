using System.Collections.Generic;
using System.Linq;
using System.Runtime.Versioning;
using mRemoteNG.Messages;
using mRemoteNG.Messages.MessageFilteringOptions;
using mRemoteNG.Messages.MessageWriters;
using mRemoteNG.Messages.WriterDecorators;

namespace mRemoteNG.App.Initialization;

[SupportedOSPlatform("windows")]
public static class MessageCollectorSetup
{
    /// <summary>
    /// Wires the collector to the writers, including any messages it has already collected.
    /// </summary>
    /// <remarks>
    /// Call this <i>after</i> <see cref="BuildMessageWritersFromSettings"/>: the backlog is
    /// delivered during this call, so a writer list that is still empty would swallow it.
    /// </remarks>
    public static void SetupMessageCollector(MessageCollector messageCollector, IList<IMessageWriter> messageWriterList)
    {
        messageCollector.SubscribeAndReplay((o, args) =>
        {
            if (args.NewItems == null) return;

            IMessage[] messages = args.NewItems.Cast<IMessage>().ToArray();

            foreach (IMessageWriter printer in messageWriterList)
            {
                foreach (IMessage message in messages)
                {
                    printer.Write(message);
                }
            }
        });
    }

    public static void BuildMessageWritersFromSettings(IList<IMessageWriter> messageWriterList)
    {
#if DEBUG
        messageWriterList.Add(BuildDebugConsoleWriter());
#endif
        messageWriterList.Add(BuildTextLogMessageWriter());
        messageWriterList.Add(BuildNotificationPanelMessageWriter());
        messageWriterList.Add(BuildPopupMessageWriter());
    }

    private static IMessageWriter BuildDebugConsoleWriter()
    {
        return new DebugConsoleMessageWriter();
    }

    private static IMessageWriter BuildTextLogMessageWriter()
    {
        return new MessageTypeFilterDecorator(
            new LogMessageTypeFilteringOptions(),
            new TextLogMessageWriter(Logger.Instance)
        );
    }

    private static IMessageWriter BuildNotificationPanelMessageWriter()
    {
        return new OnlyLogMessageFilter(
            new MessageTypeFilterDecorator(
                new NotificationPanelMessageFilteringOptions(),
                new MessageFocusDecorator(AppWindows.ErrorsForm,
                    new NotificationPanelSwitchOnMessageFilteringOptions(),
                    new NotificationPanelMessageWriter(AppWindows.ErrorsForm))
            )
        );
    }

    private static IMessageWriter BuildPopupMessageWriter()
    {
        return new OnlyLogMessageFilter(
            new MessageTypeFilterDecorator(
                new PopupMessageFilteringOptions(),
                new PopupMessageWriter())
        );
    }
}