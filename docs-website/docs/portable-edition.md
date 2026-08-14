---
title: Portable and Installed Editions
sidebar_label: Portable Edition
---

:::info Version

The `portable.flag` marker file is new. In earlier releases the edition was fixed when the
application was compiled, and every published download — including the MSI installer — behaved as
the portable edition.

:::

mRemoteNG runs in one of two editions, and the only difference between them is **where your files
live**:

| | Portable edition | Installed edition |
|---|---|---|
| Settings, connections, layouts, themes | `Settings` folder next to `mRemoteNG.exe` | `%APPDATA%\mRemoteNG` |
| Log file | `mRemoteNG.log` next to `mRemoteNG.exe` | `%LOCALAPPDATA%\mRemoteNG\mRemoteNG.log` |
| Custom configuration directory (**Tools → Options → Configuration**) | Not available | Available |

The program files are identical. One copy of mRemoteNG can be either edition.

If the folder holding `mRemoteNG.exe` is read-only — a CD, a locked `Program Files` folder, some
WebDAV drives — the portable edition falls back to `%APPDATA%` and `%LOCALAPPDATA%` so that it can
still start.

## Which edition am I running?

Open **Help → About**. The portable edition adds *Portable Edition* after the program name. The
same wording appears on the first line of `mRemoteNG.log` at every start.

## Which download is which

| Download | Edition |
|---|---|
| **Self-Contained** ZIP (includes the .NET runtime) | Portable |
| **Framework-Dependent** ZIP | Installed |
| **MSI** installer | Installed |

## Switching a copy between editions

The edition is decided by a single file named `portable.flag` sitting beside `mRemoteNG.exe`.
Present means portable, absent means installed. Nothing reads what is inside it, so an empty file
works.

To make a copy portable, create the file next to the program:

```powershell
New-Item -ItemType File "C:\Apps\mRemoteNG\portable.flag"
```

To make the same copy an installed-style one, delete that file.

Either way, **restart mRemoteNG**. The edition is decided once when the program starts, because it
selects where every file is read from and written to.

:::warning Your existing files stay where they are

Switching editions does not move anything. mRemoteNG simply looks in the other location, so a
switched copy starts with empty settings and no connections until you move the files yourself. Copy
the contents of the old folder into the new one before starting it again.

:::

## Upgrading a portable installation

Extract the whole ZIP over your existing folder. The ZIP contains `portable.flag`, so the upgraded
copy stays portable and keeps reading the `Settings` folder that is already there.

:::warning Do not upgrade by copying only `mRemoteNG.exe`

Replacing just the executable leaves the new program without a marker file, so the next start comes
up as the installed edition, looks in `%APPDATA%\mRemoteNG` instead, and appears to have lost every
connection and setting.

Nothing is deleted — your files are still in the `Settings` folder next to the program. Close
mRemoteNG, create an empty `portable.flag` beside `mRemoteNG.exe`, and start it again.

:::

## Running from a USB stick

Use a Self-Contained download, extract it to the stick, and keep `portable.flag` in the folder. All
settings and connections stay in the `Settings` subfolder and travel with the stick.
