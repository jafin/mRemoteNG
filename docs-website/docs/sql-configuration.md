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
- Go to Tools - Options - SQL Server
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
