# Design

## Context

`add-sftp-browser-panel` built the two panes and `sftp-recursive-transfers` made them move trees. What
neither did was make the listing pleasant to read or a dropped session recoverable.

Two of the five items here are not new features at all — they are things that were started and do not
work:

- `FilePaneControl.BuildList` sets `ImageGetter = o => ... ? "folder" : "file"` on the Name column, but
  no `SmallImageList` is ever assigned to `_list`. The keys resolve against nothing, so no icon has
  ever been drawn. The intent is in the code; the image list is missing.
- `SftpSession.ConnectAsync` assigns `_client` and `_authentication` without disposing whatever was
  there. Correct exactly once, which is all it is called today. Reconnect calls it again.

The pieces needed are already present: `FolderClosed_16x` and `Document_16x` are in
`mRemoteNG/Resources/` and exposed through `Properties.Resources`; `Language.Reconnect` and
`Language.Refresh` already exist; `ObjectListView` exposes `CustomSorter` and a public
`ColumnComparer`, which is what pinning a row above a user-chosen sort needs.

## Goals / Non-Goals

**Goals:**

- A dropped session recoverable in place, without losing the directory the user was in.
- A listing that says what each entry is, at a glance and exactly.
- Presentation rules that live where they can be tested, not buried in control setup.
- No new dependency for any of it.

**Non-Goals:**

- Automatic reconnection on drop, or retry with backoff. Reconnecting is a second authentication and
  may be a second prompt; it happens when the user asks.
- Per-extension icons, or resolving what a link points at in order to pick its icon.
- A configurable date format, or sortable-by-default ISO dates.
- Reconnecting the *local* pane, which cannot disconnect.

## Decisions

### D1 — The pane is given a connection to talk to, and does not know what a session is

`FilePaneControl` takes an optional `IPaneConnection`:

```
bool IsConnected { get; }
Task<bool> ReconnectAsync(CancellationToken cancellationToken = default);
event EventHandler? ConnectionChanged;
```

`FileManagerTab` implements it; the local pane is given nothing. That single null is what makes the
Reconnect button remote-only and reconnect-on-refresh remote-only, with no `if (isRemote)` anywhere —
the local pane has nothing to reconnect *to*, and the type says so.

The tab is the right implementer because it already owns the session, the title and the `Dropped`
subscription. The single-flight guard lives there too (D2), since it is the session that must not be
opened twice.

`ReconnectAsync` returns success rather than throwing, because the caller has to decide whether to go
on and list. Reporting the reason is the tab's job — it has `Runtime.MessageCollector` and the
connection error already flows through `ConnectRemoteAsync`.

Refresh becomes: reconnect first *if disconnected*, and list only if that worked.

```
if (_connection is { IsConnected: false } connection && !await connection.ReconnectAsync())
    return;
await _controller.RefreshAsync();
```

A connected pane is untouched by this — the guard is false and it lists exactly as before. Listing
after a failed reconnect was considered and rejected: it would fail with the same "not connected" error
the reconnect just reported, twice for one gesture.

### D2 — One reconnect at a time, guarded in the tab

The button and a refresh are two routes to the same operation and an impatient user will take both. Two
attempts in flight means two authentications and two `SftpClient`s, one of which is then abandoned —
exactly the leak D6 exists to stop.

A simple `_reconnecting` flag under the tab's lock; a second caller returns false immediately rather
than queueing. Queueing would mean the second attempt runs after the first has already succeeded,
reconnecting a healthy session for no reason.

### D3 — `SftpSession.ConnectAsync` becomes re-enterable

Before building the new client, dispose the old one and its authentication — and **unsubscribe
`ErrorOccurred` first**. That ordering is the part worth stating: disposing a connected `SftpClient`
can raise `ErrorOccurred`, and a late `Dropped` from the corpse of the previous connection would mark
the freshly reconnected session as dropped. The symptom would be a reconnect that appears to work and
then immediately reports itself disconnected, which is a miserable thing to debug.

`_credential` is *not* disposed here. It is owned for the life of the session and read lazily by the
authentication methods on each connect; disposing it would break the second attempt.

### D4 — The parent row is a flagged entry produced by the controller

`FileSystemEntry` gains `IsParentNavigation` (appended, defaulting false). `FilePaneController` exposes
`ParentEntry`, non-null after a successful listing except at the root, where `GetParentPath(path) ==
path` already answers "there is no parent" for both filesystems.

Deliberately *not* in `Entries`. That property feeds the item count, the size total and — through
`Rebind` — everything downstream, and a synthetic row in it would have to be subtracted back out at
every one of those sites. Keeping it beside the list means the count and total are right by
construction, and the one place that must merge them is the view.

The flag rather than matching on the name `".."`: a remote unix directory may legitimately contain a
file called `..` in a way a client should not have to reason about, and name-matching would make the
guard on transfer and delete depend on a string.

`SelectedEntries` filters the flag out, which is what stops transfer, rename and delete ever seeing it.
That is one filter in one property, rather than a check in each of the three commands.

### D5 — Pinned above the sort, via `CustomSorter`

`ObjectListView` sorts model objects, so a `..` in the list would sort with everything else and land in
the middle of a descending name sort. `CustomSorter` is the documented hook, and `ColumnComparer` is
public, so the fix is a comparer that puts the flagged row first and delegates everything else:

```
_list.CustomSorter = (column, order) =>
    _list.ListViewItemSorter = new ParentFirstComparer(new ColumnComparer(column, order));
```

Considered instead: a second single-row control docked above the list. Rejected — it needs its own
theming, its own column alignment and its own focus behaviour, to avoid one comparer.

### D6 — Dates are aligned by upgrading the locale's pattern, not replacing it

Take `ShortDatePattern` and `ShortTimePattern` from the current culture and widen single `d`, `M`, `h`
and `H` to their doubled forms. `dd/MM/yyyy` stays British, `MM/dd/yyyy` stays American, and both stop
being ragged.

Rejected: a fixed `yyyy-MM-dd HH:mm`. It aligns and sorts beautifully and shows a German user a date
format they do not use. Alignment is not worth overriding the user's locale.

The substitution must ignore quoted literals in the pattern — a culture whose pattern contains a
quoted `'d'` would otherwise have the literal rewritten. A small helper, tested against en-GB, en-US
and a pattern with a quoted literal.

### D7 — Blank cells come from the aspect, not the formatter

`AspectToStringConverter` receives only the value, so it cannot tell a directory's `0` from a file's
`0`. The aspect getter can, because it has the row:

```
AspectGetter = o => o is FileSystemEntry { IsDirectory: false, IsParentNavigation: false } file
    ? file.Length
    : null
```

`null` renders empty, and the converter is adjusted to pass `null` through rather than coercing it to
`0 B`. The same shape gives the parent row its empty kind and modified cells.

### D8 — Icons come from the assets already in the repository

`FolderClosed_16x` for directories, `Document_16x` for files, in a `SmallImageList` keyed `"folder"`
and `"file"` — the keys the existing `ImageGetter` already returns, so that line starts working
unchanged.

FontAwesome was offered and is not needed: it would add a dependency and a font-rendering path to draw
two glyphs the application already ships as bitmaps, in the visual language of the rest of mRemoteNG.

A link gets the icon of whatever it is listed as, and the Type column carries the fact that it is a
link. Choosing an icon by what the link *points at* would cost a following stat per link on every
listing, and the column already answers the question exactly.

## Risks / Trade-offs

**A reconnect is a second authentication** → For an agent or a stored credential it is silent; for a
server-issued second factor it is another prompt. Inherent to opening a connection, and the reason
reconnection is on demand rather than automatic.

**Reconnecting lands in a directory that no longer exists** → The listing fails and is reported, and
the pane keeps what it had — the existing behaviour of `NavigateCoreAsync` for any failed listing. The
user is connected and can navigate away, which is the state that matters.

**The date-pattern rewrite could mangle an exotic locale pattern** → Contained to a helper with tests
rather than spread through the column setup, and the worst outcome is a differently formatted date, not
a wrong one. If a pattern proves resistant, that culture keeps the unpadded format.

**`CustomSorter` is a less-travelled `ObjectListView` path** → It is the documented extension point and
`ColumnComparer` is public, so this is ordinary use rather than a workaround. If the parent row proves
unpinnable, the fallback is to leave it sorting with the rest, which is untidy but not broken.

**One `ImageList` per pane rather than a shared static** → Two small bitmaps duplicated per tab. Sharing
one across controls risks it being disposed while another pane still draws from it, which is a crash
rather than a few kilobytes.

### D9 — The action is on the tab as well as the pane

The tab header carries a context menu with the same Reconnect item, enabled on the same condition.
`DockContent.TabPageContextMenuStrip` is the existing mechanism and costs one menu.

Two entry points for one action, because the two are noticed at different moments. The tab title is
what says `(files — disconnected)`, so a user who has been working elsewhere reads the problem on the
tab and reaches for it there; a user already in the pane sees the failed listing and reaches for the
toolbar. Both routes call the same guarded `ReconnectAsync` (D2), so pressing both changes nothing.

Deliberately not a reconnect prompt on drop. A flaky link drops repeatedly, and a modal per drop would
interrupt whatever the user moved on to in order to ask a question a button already answers whenever
they choose to look.

## Open Questions

None outstanding.
