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

Nothing changes the recovery password on its own. **File → Rekey Connection File...** sets a new
one, but it gives the file a new key at the same time — see
[Removing someone's access](#removing-someones-access) for what that costs. Choose a password you
can keep.

:::

When mRemoteNG needs it, you get an ordinary password prompt naming the connection file. You have
three attempts, and once a password has worked, it is remembered for the rest of that mRemoteNG
session — opening a second file protected by the same password does not ask again.

## Sharing a file with your team

A connection file on a share works, and each person who saves it types the recovery password
**once**.

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

## Removing someone's access

:::info Version

**File → Rekey Connection File...** is new. Earlier versions had no way to take a connection file
away from someone it had been shared with.

:::

When someone leaves the team, they still have the recovery password — and they may have a copy of
the connection file. Rekeying is what removes their access. It does four things in one step:

1. Gives the file a **new key**.
2. Asks you for a **new recovery password**, which you then share with the people who stay.
3. Drops **every automatic unlock** on the file, including everyone else's.
4. Takes a **backup** of the file as it was, before any of it happens.

Everyone who stays is asked for the new recovery password the first time they open the file
afterwards, exactly as they were on their first open, and is unlocked automatically again when they
next save.

:::warning Rekeying does not reach a copy already taken

Anyone who kept a copy of the file can still open it with the old recovery password, and it still
holds every password that was in it when they took the copy. **Treat those passwords as known and
change them on the systems themselves.** Rekeying stops the next copy, not the one already gone.

The backup it takes is one of those copies: it opens with the *old* recovery password. Keep it as
carefully as you kept the original, and delete it when you are sure the rekey worked.

:::

There is deliberately no way to remove one person's unlock and leave the rest. It would not remove
their access — they know the recovery password and have had the file — and it would tell you it had.

### "This file has been rekeyed since you opened it"

If a colleague rekeys the file while you have it open, your next save is refused with that message.
Nothing has been written, and **your unsaved changes are still on screen**.

This is deliberate. Your copy of mRemoteNG is still holding the old key and the old recovery
password, so saving would write the whole file back the way it was and quietly undo the rekey —
letting back in the person it was meant to shut out.

To carry on:

1. Use **File → Save As...** to keep your unsaved changes somewhere else, if you have any that
   matter.
2. Close and re-open the connection file. It asks for the new recovery password once.
3. Redo the changes you saved aside.

Ordinary saves are not affected. Two people editing one connection file has always been
last-writer-wins, and it still is — only a rekey refuses anything.

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

## When one connection's password cannot be read

:::info Version

Changed in 1.82.0.

:::

Saved passwords are now unscrambled the moment you first need one, rather than all together when the
file is opened. Almost always you will never notice. The one place you can is a connection whose
stored password has been damaged — by a partial file copy, a sync conflict, or an editor that
rewrote the file.

Such a connection now reports the problem **when you open that connection**, naming it, instead of
at the moment you opened the file. Every other connection in the file keeps working normally.

To repair it, select the connection, type the password into the **Password** field again, and save.

:::warning

mRemoteNG will not connect using a blank password in place of one it could not read. If it did, a
connection set to fall back to a default password would silently send the wrong credentials to the
server. Being told the password is unreadable is the safe outcome.

:::

A file that does not open **at all** still says so once, while you are opening it — that has not
changed. One damaged password is a different thing from a wrong recovery password, and they are
still reported differently.

### Rekeying and hardening need every password

[Rekeying](#removing-someones-access) and
[hardening](#hardening-a-connection-file) both re-encrypt the whole file, so both need to read every
password in it first. If one cannot be read, the operation is refused and the file is left exactly
as it was — nothing is half-written. Repair the connection it names, then try again.

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
