## Why

The file manager works but reads poorly and strands the user when a connection drops. A dropped
session is terminal — the tab reports "disconnected" and the only way back is to close and reopen it,
losing the directory you were in. The listing itself is harder to scan than it should be: nothing
distinguishes a folder from a file at a glance, dates wander left and right because days and months
are not zero-padded, every folder claims to be "0 B", and going up a level means finding a toolbar
button rather than the `..` row every file manager has had for thirty years.

None of this is deep. All of it is friction on the operation people do most.

## What Changes

**Reconnecting**

- A Reconnect button on the remote pane, shown only there and enabled only while disconnected.
- Refresh on a disconnected pane reconnects first, then lists — so the habitual gesture recovers the
  session instead of reporting the same error again.
- A successful reconnect restores the tab title and re-lists the directory the user was in.
- Reconnecting twice at once does nothing the second time; one attempt runs at a time.
- **Fixes a latent leak**: `SftpSession.ConnectAsync` replaces `_client` and `_authentication` without
  disposing the previous ones. Harmless today because it is called once, but reconnect calls it again,
  and each attempt would abandon an SSH client holding a socket and its threads.

**Type column and icons**

- A Type column reading `Folder`, `File` or `Link`.
- A folder icon on directories and a document icon on files, in the Name column, using the
  `FolderClosed_16x` and `Document_16x` assets already in `mRemoteNG/Resources/`. **No FontAwesome
  import is needed** — the icons exist and are already exposed through `Properties.Resources`.
- **Fixes a latent bug**: the Name column already sets an `ImageGetter` returning `"folder"`/`"file"`
  keys, but no `SmallImageList` is ever assigned, so it resolves to nothing and no icon has ever been
  drawn. This change supplies the image list those keys were written for.
- A link takes the icon of what it is listed as; the Type column is what says it is a link. Deciding
  whether a link points at a folder or a file needs a round trip per entry, which is not worth it to
  choose an icon.

**Parent navigation row**

- A `..` row at the top of the listing that navigates to the parent, present in both panes and absent
  at the filesystem root.
- It is not a file: it cannot be transferred, renamed or deleted, it is excluded from the selection
  those commands act on, and it is not counted in the "N item(s)" summary or the size total.

**Layout**

- Modified dates zero-pad day, month and hour, keeping the user's locale field *order* while making
  the column align. A UK user keeps `dd/MM/yyyy`, a US user keeps `MM/dd/yyyy`; both stop ragged.
- Directories show nothing in the Size column rather than `0 B`, which is not their size.

## Capabilities

### New Capabilities

- `file-manager-presentation`: how the listing presents entries — type, icons, the parent row, date
  alignment and what the size column shows for a directory.
- `file-manager-reconnection`: recovering a dropped remote session from within the tab, and what the
  panes do while disconnected.

### Modified Capabilities

None. `sftp-browser-panel` requires that a dropped connection is reported and that stale contents are
not presented as current; both still hold, and reconnection is new behaviour beside them rather than a
change to them.

## Impact

- `mRemoteNG/Connection/Sftp/SftpSession.cs` — dispose prior client and authentication before
  reconnecting.
- `mRemoteNG/FileTransfer/FileSystemEntry.cs` — a flag marking the synthetic parent row.
- `mRemoteNG/FileTransfer/FilePaneController.cs` — expose the parent entry; keep it out of the entry
  list proper.
- `mRemoteNG/UI/Controls/FileTransfer/FilePaneControl.cs` — image list, Type column, parent row,
  date and size formatting, Reconnect button, reconnect-on-refresh.
- `mRemoteNG/UI/Window/FileManagerTab.cs` — supply the remote pane's reconnect handler; restore title
  and listing on success.
- `mRemoteNG/Language/` — strings for the Type column values. `Reconnect` and `Refresh` already exist.
- `mRemoteNGTests/FileTransfer/` — tests for the parent entry, formatting helpers and reconnect
  sequencing.

No new dependencies, no new packages, no configuration or file-format changes.
