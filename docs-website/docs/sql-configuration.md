---
title: SQL Configuration
---

:::warning

The SQL feature is in an early beta stage and not intended for use in a production environment! I recommend you to do a full backup of your connections and settings before switching to SQL Server.

:::

## Supported Databases

The list below includes databases that are officially supported. Others may already work and this list may expand with future updates.

- MSSQL
- MySQL

## Steps to configure your SQL Server

- Create a new Database called "mRemoteNG" on your SQL Server.
- Give the users that you want to grant access to the mRemoteNG Connections Database Read/Write permissions on the Database.

## Steps to configure mRemoteNG for SQL

- Start mRemoteNG if it's not already running.
- Go to File - Options - SQL Server
- Check the box that says "Use SQL Server to load & save connections".
- Fill in your SQL Server hostname or ip address.
- If you do not use your Windows logon info to authenticate against the SQL Server fill in the correct Username and Password.
- Click OK to apply the changes. The main window title should now change to "mRemoteNG \| SQL Server".
- Now click on File - Save to update the tables on your SQL Server with the data from the loaded connections xml file. (Do not click File - New, this doesn't work yet)
- You should now be able to do everything you were able to do with the XML storage plus see the changes live on another mRemoteNG instance that is connected to the same Database.

## When the database cannot be reached

:::info Version

Behaviour when the database is unavailable has changed. Earlier versions loaded a local copy and
described it as read-only while still allowing saves.

:::

mRemoteNG keeps a local copy of your connections each time it reads the database, so you can still
see them when the database is unavailable — on a VPN that is down, for instance, while the servers
themselves are reachable.

If the database cannot be read at startup, that copy is loaded and you are told so, including **how
old it is**. Work from it as normal: connect, open sessions, copy details out.

:::warning

**Changes are not saved while you are working from the local copy.** Editing a connection, adding
one or moving one will not reach the database, and mRemoteNG tells you so each time rather than
saving it somewhere it will be lost.

This is deliberate. The copy shows what the database held when it was last reachable, and colleagues
have very likely changed it since. Writing your copy back would delete their new connections and
restore ones they removed.

To make changes, get the database reachable again and restart mRemoteNG so the connections are
loaded fresh.

:::

The local copy is encrypted with a key held by your Windows account, so another account on the same
computer cannot read it. If you no longer want a copy kept, delete `SqlConnectionsCache.xml` and
`SqlConnectionsCache.xml.key` from your mRemoteNG settings folder — a new one is written the next
time the database is read.
## How passwords are stored in the database

Every password in the database — connection passwords, RD Gateway passwords and VNC proxy
passwords — is encrypted before it is written. Which encryption is used depends on how old the
database is.

Databases created from now on use **AES-256-GCM**. As well as being much harder to attack, this
detects any change to the stored text, so a password altered directly in the database is reported as
a failure instead of being handed to a connection.

Databases created by earlier versions use the older scheme, and keep working. You can still open
them, edit them and save to them exactly as before. The first save in each session adds one warning
to the notification panel telling you the database is still on the old format; nothing is refused,
and nothing changes on its own.

:::warning

The older scheme derives its key from your master password in a single step, which modern hardware
guesses through very quickly, and it cannot tell whether stored text has been tampered with. If your
database holds passwords that matter, upgrade it.

:::

### Upgrading an existing database

:::info Version

**Upgrade Encryption...** is new. Earlier versions had no way to change how an existing database
stores its passwords.

:::

Go to **File → Options → SQL Server** and click **Apply**. Once mRemoteNG has connected, a line
appears under the connection status saying which format the database uses. If it is still on the old
one, an **Upgrade Encryption...** button appears beside it.

Before you click it:

- **This decides for everybody who uses the database, not just for you.** Once upgraded, only
  installations that have this feature can open it. Upstream mRemoteNG and every earlier build will
  refuse it and say so. Tell your colleagues first — including anyone who has never installed this
  version.
- **Back the database up.** The upgrade cannot be undone from inside mRemoteNG. Restoring that
  backup is the only way back.
- Have the **database master password** to hand if the database has one. You are asked for it, and it
  is checked before anything is rewritten.

The upgrade re-encrypts every password in one go. Either all of it succeeds or none of it does, so
the database is never left half-converted. No connection needs editing afterwards and nothing else
about the database changes.

:::note

The upgrade is never offered automatically when you open a database, only from this page. It is not
the sort of decision that should be made by whoever happens to start mRemoteNG first.

:::

:::warning

Upgrading changes how passwords are stored from now on. It does not change the passwords themselves.
Anyone who already had a copy of the database, or a backup of it, can still read every password it
held at the time they took the copy — those should be changed on the systems themselves.

:::
