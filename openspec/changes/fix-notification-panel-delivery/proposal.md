## Why

`NotificationPanelMessageWriter` buffers messages in a plain `List<ListViewItem>` that is mutated by
whichever thread reported the message, with no synchronization, and delivers some messages straight
to the panel while others are still in flight to it.

Raised in review of `fix-connection-save-durability`, which changed the writer's dispatch from
`Invoke` to `BeginInvoke` to remove a shutdown deadlock. The ordering weakness is older than that
change — `Invoke` interleaved identically — but looking at it turned up the data race, which is
worse.

### The buffer is not thread-safe

```csharp
private List<ListViewItem>? _pendingItems = [];
…
public void Write(IMessage message) => AddToList(new NotificationMessageListViewItem(message));

private void AddToList(ListViewItem lvItem)
{
    if (_pendingItems != null)
    {
        if (_flushPosted || !_messageWindow.lvErrorCollector.IsHandleCreated)
        {
            if (_pendingItems.Count == 0 && !_flushPosted)
                _messageWindow.lvErrorCollector.HandleCreated += OnHandleCreated;
            _pendingItems.Add(lvItem);
            return;
        }
        FlushPending();
    }
    …
}
```

`Write` runs on whichever thread reported the message — background workers as much as the UI thread.
Everything above runs before any marshalling: `_pendingItems.Add`, the `HandleCreated` subscription,
the `_flushPosted` assignment, and `FlushPending` niling the field. Two threads reporting during
startup can corrupt the list, drop messages, subscribe twice, or flush while another is appending.

`List<T>` corrupted concurrently does not fail loudly. It loses items, or throws somewhere unrelated
later.

### Entries can appear in the wrong order

The panel inserts each entry at index 0, so the newest is at the top. A message reported from a
background thread is queued to the UI thread; a message reported *by* the UI thread, while it is
inside an event handler, goes straight in. The direct one lands first, and the earlier background
message is inserted above it when the loop next runs — older message, higher position, against
timestamps the panel displays.

### Why the buffering exists

The panel starts in `DockBottomAutoHide`. Its handle is created only when the user first opens it,
which is well after startup — so there is a window where messages have arrived and there is nothing
to render them into. That is real and the fix keeps it; it just stops being a field that any thread
may poke.

## What Changes

- One `NotificationMessageQueue` between the reporters and the UI thread, guarded by a lock,
  replacing `_pendingItems`/`_flushPosted`.
- **Every** message goes through it, including the UI thread's own. Panel order becomes enqueue
  order by construction rather than by luck of timing.
- Queuing and asking for a drain are one atomic step, so exactly one drain is ever outstanding.
  Emptying the queue and giving up the drain are likewise one step, so a message queued at that
  moment cannot be left with nothing scheduled to collect it.
- `ListViewItem`s are built during the drain, on the UI thread, instead of on whatever thread
  reported the message.
- The handle-created path stays: no handle, no post, messages wait.

## Impact

`mRemoteNG/Messages/MessageWriters/NotificationPanelMessageWriter.cs` and a new
`NotificationMessageQueue` beside it. No caller changes — `Write` keeps its signature and its
contract of being callable from anywhere, which it now actually honours.

Messages reported *by* the UI thread are no longer rendered synchronously; they are posted like
every other. Nothing reads the panel back after writing to it, and the log writer is unaffected, so
this is not observable except in ordering — which is the point.

Messages still queued when the message loop ends are not rendered. They were already written to the
log by then, and the panel is going away.

Stacked on `fix-connection-save-durability`, which touches the same method.
