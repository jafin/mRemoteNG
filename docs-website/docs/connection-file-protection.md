---
title: Protecting Your Connection File
sidebar_label: Connection File Protection
---

:::info Version

The hardened storage format and the recovery password are new. Files written by earlier versions
keep working exactly as they did, and are never changed unless you ask for it.

:::

Your connection file (`confCons.xml`) holds every password you have saved. It has always been
encrypted — but unless you set a master password, it was encrypted with **a fixed key built into
mRemoteNG and published in its source code**. Anyone who obtains a copy of the file, from a backup,
a synchronised folder, a shared drive or a stolen disk, can read every password in it.

The **hardened** storage format replaces that key with one belonging to your file alone.

## The two formats

| | Classic | Hardened |
|---|---|---|
| Key | The published default, or one derived from your master password | Random, unique to this file |
| Opened by upstream mRemoteNG and older builds | Yes | **No** |
| Asks for a password in daily use | Only if you set a master password | No |
| Needed to open it elsewhere | The master password, if any | The recovery password |

Hardening is a per-file decision and the file records it, so a file carries its own format wherever
you copy it.

## Hardening a connection file

The first time you open a classic connection file, mRemoteNG offers to harden it. You can also
raise it yourself at any time from **File → Storage Format...**.

The **Harden Storage Format** dialog asks *Harden this connection file?* and offers three choices:

- **Harden the store** — the next save writes the hardened format.
- **Export a classic copy first** — writes a copy that upstream mRemoteNG can still open, and
  leaves your file alone. Asking for the way back is not the same as deciding, so this does *not*
  harden anything.
- **Cancel** — nothing changes.

:::warning Other applications lose access

After hardening, upstream mRemoteNG and earlier builds of this fork can no longer open the file, and
**they will not tell you why** — they ask for the password again and refuse the one you give them.
Backups taken before you harden stay readable by those applications; backups taken afterwards do
not.

:::

If you decline, the file is untouched and mRemoteNG will not ask about that file again. It records
your answer in a small file beside it, named after your connection file with `.hardening-declined`
on the end. Delete that file, or use **File → Storage Format...**, to be asked again.

## The recovery password

Hardening asks you to **Set a recovery password** once. It is not a password you type to use
mRemoteNG day to day — your Windows account unlocks the file automatically on the computer where
you set it.

You need the recovery password anywhere that automatic unlock cannot work:

- another computer
- another Windows account on the same computer
- the same account after the Windows profile has been rebuilt
- a backup restored into any of the above

:::warning Keep it somewhere other than this computer

The recovery password is the only way back into the file if this Windows account is lost. A note in
a password manager on your phone is fine; a text file next to the connection file is not.

There is currently no screen for changing it, so choose one you can keep.

:::

When mRemoteNG needs it, you get an ordinary password prompt naming the connection file. You have
three attempts, and once a password has worked, it is remembered for the rest of that mRemoteNG
session — opening a second file protected by the same password does not ask again.

## Sharing a file with your team

A connection file on a share works, and each person types the recovery password **once**.

The first time a colleague opens the file, their Windows account is not one it recognises yet, so
they are asked for the recovery password. The next time they **save**, mRemoteNG adds an unlock for
their account alongside everyone else's, and the file opens silently for them from then on. Nobody's
unlock replaces anybody else's.

Two things follow from that happening on save rather than on open:

- Someone who only ever **reads** the file never earns one, and is asked for the recovery password
  every time. Saving is what earns it.
- If two people save at the same moment, one save overwrites the other — as it already does for the
  rest of the file. If the lost one carried an unlock, that person is asked for the password once
  more, and has it back when they next save.

:::note Everyone still needs the recovery password once

Share it the way you would any team secret. It is also what opens the file on a machine that has
never seen it.

:::

## When every open asks for the password

**You are running the [portable edition](./portable-edition.md).** A key tied to one Windows account
defeats a copy whose whole purpose is to run from a USB stick on someone else's machine, so the
portable edition uses the recovery password alone and adds no automatic unlock, wherever the file is
kept.

Files it writes open in the installed edition, and vice versa.

## Moving a file to another computer

Copy the file across and open it. mRemoteNG asks for the recovery password once and then works
normally. Nothing needs exporting, converting or re-entering.

If you keep the file on a USB stick and use it on several machines, expect the prompt on each of
them — the automatic unlock is per account, per machine.

## Backups

Backups are exact copies of the encrypted file, so they carry the same protection it did. Two
consequences worth knowing:

- A backup restored on the same computer and account opens with no prompt, just like the original.
- A backup taken **before** you last changed the recovery password needs the password that was in
  use when it was written. If a restore reports that a backup could not be unwrapped, that is the
  first thing to check — the file is not damaged.

## Sharing a file with someone using upstream mRemoteNG

Use [Export to file](./user-interface/import-export.md#export-to-file). When the store is hardened,
mRemoteNG warns that the exported copy is
written in the classic format so other builds can open it, which means it is protected less
strongly than the file it came from. Keep it somewhere you would be willing to keep the original.

## What about the master password?

Setting a master password (**Password protect**) also replaces the published key, and it stays
available. The difference is when you are asked for it: a master password is typed every time you
open the file, on every machine, while a hardened file asks for nothing on the computer where you
set it up and falls back to the recovery password only where it has to.
