## Why

A change the user made can fail to reach disk, with nothing said about it.

Found while trying to verify something else: a master password was set on the connection file, then
removed through the property grid, then File → Save Connections, then the application was closed. On
restart the password was still there. The file had not been written at all — not a stale write, no
write. Two mechanisms combine to produce that.

### The debounced save is never flushed on exit

`ConnectionsService.SaveConnectionsAsync` waits two seconds before writing:

```csharp
private const int SaveDebounceMs = 2000;
…
_saveDebounceTimer?.Dispose();
_saveDebounceTimer = new System.Threading.Timer(_ => { … SaveConnections(…) }, null, SaveDebounceMs, Timeout.Infinite);
```

The debounce is right and should stay. Its comment explains why: rapid `PropertyChanged` events would
otherwise queue a save each, and every one re-encrypts every password with PBKDF2 at 600,000
iterations.

What is missing is the other half. `System.Threading.Timer` runs on the thread pool and keeps nothing
alive, so quitting inside those two seconds drops the write. `Shutdown.SaveConnections` does not
flush it, and does not unconditionally save either — it saves only when `SaveConnectionsFrequency` is
`OnExit`, or when a Daily/Weekly interval has elapsed:

```csharp
switch (Properties.OptionsBackupPage.Default.SaveConnectionsFrequency)
{
    …
    default:
        return;      // no save
}
```

So on the default settings, an edit made in the last two seconds before closing is lost silently.
That setting governs *periodic* saves; it was never meant to decide whether an edit the user already
made survives.

### A save that does not happen says nothing

`SaveConnections` has three early returns and one swallowed exception, and none of them reach the
user:

```csharp
if (ConnectionTreeModel is null || ConnectionFileName is null) return;
if (!forceSave && !IsConnectionsFileLoaded) return;
if (_batchingSaves) { _saveRequested = true; return; }
…
catch (Exception ex) { Runtime.MessageCollector?.AddExceptionStackTrace("SaveToXml failed", ex); }
```

Four ways for a change not to reach disk while the interface behaves as though it did. The log line
is the only trace, and nobody has the log open.

### Why it matters more than an ordinary bug

The case that exposed it was a security control. A user sets a master password, closes the
application, and gets a connection file still encrypted under the `mR3m` constant published in the
source — believing it protected. Losing a renamed folder is annoying; losing the only protection the
file has is not the same thing.

## What Changes

- A pending debounced save is **flushed on shutdown**, before the process exits, regardless of
  `SaveConnectionsFrequency`.
- The debounce itself is unchanged. It exists for a good reason and removing it would restore the
  behaviour it was added to fix.
- A save that is skipped or fails is **reported where the user is**, not only in the log.
- `SaveConnectionsFrequency` keeps its meaning: how often to save periodically, never whether an edit
  the user already made is written.

## Capabilities

Adds `connection-save-durability`. It states what the application guarantees about an edit reaching
disk — a property no change has owned, which is how two mechanisms came to disagree about it.

## Impact

`mRemoteNG/Connection/ConnectionsService.cs`, `mRemoteNG/App/Shutdown.cs`, and wherever save failures
surface.

Flushing on exit costs a shutdown that waits for one derivation and a write — a few hundred
milliseconds against a 600,000-iteration KDF. That is the right trade against silently discarding
what the user just did, but it does mean closing the application is no longer instant after an edit,
and the flush must not be able to hang shutdown indefinitely if the write blocks.

The reporting half needs judgement about volume. Saves are frequent and mostly automatic; a modal
dialog per failure would be intolerable. The requirement is that a failure is visible without the
log, not that it interrupts.

**Not part of the mRemoteNG#3416 audit work**, though it obstructed verifying it — see
`SECURITY-AUDIT-3416.md`. It is a defect in `dev` and stands on its own.
