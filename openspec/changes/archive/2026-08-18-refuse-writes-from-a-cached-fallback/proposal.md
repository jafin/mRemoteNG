## Why

When a database load fails, `ConnectionsService.LoadConnections` silently substitutes a stale local
copy and tells the user it is read-only. Nothing makes it read-only.

```csharp
catch (Exception ex) when (useDatabase)
{
    string cachePath = Path.Combine(SettingsFileInfo.SettingsPath, SettingsFileInfo.SqlConnectionsCache);
    if (File.Exists(cachePath))
    {
        Runtime.MessageCollector.AddMessage(MessageClass.WarningMsg,
            $"Could not load connections from database ({ex.Message}). Loading from local cache in read-only mode.");
        connectionLoader = new XmlConnectionsLoader(cachePath);
        newConnectionTreeModel = connectionLoader.Load();
    }
    else
    {
        throw;
    }
}
```

`ConnectionsService.cs:395-409`

Execution then falls straight through to `UsingDatabase = true`. The only read-only gate that exists
is the user's own **SQL Read Only** setting, consulted by `SqlConnectionsSaver.SqlUserIsReadOnly()`;
nothing in this path sets it, and there is no other flag to set. `SaveConnections()` routes on
`UsingDatabase`, so **the next save writes the stale tree back over the live database.**

The result is worse than a failed load. Connections deleted by a colleague return, connections added
by a colleague disappear, and every edit made since the cache was written is undone — from a client
that told its user it was in read-only mode, at a moment nobody would connect with a database
problem. The `#1351` guard in `SqlConnectionsSaver` catches the *empty* tree case, so the codebase
already recognises this class of danger; a stale but populated tree walks straight past it.

**This defeats the protection `encrypt-sql-backend-with-aead` §1 shipped a release early for.** That
refusal is `throw new InvalidOperationException("Could not load SQL connections")` — an exception,
caught here. So for exactly the population §1 protects (people who have loaded from SQL before, and
therefore have a cache) refusing a too-new database turns into "work from a stale copy, then
overwrite the upgraded database with it". Found by that change's manual check 6.6.

**Separately, the cache is an unprotected copy of the whole store.** It is written on every
successful load by `TrySaveSqlConnectionsCache`, through `XmlConnectionsSaver` with a default
`SaveFilter`, so it contains every password. `DataTableDeserializer` sets the root node's
`PasswordString` to the database's decryption key, and `RootNodeInfo.PasswordString` marks a store
protected only when that key differs from the default — so a database with **no master password**,
which is the case this fork is otherwise busy fixing, produces a local file keyed with
`ConnectionFileDefaults.LegacyEncryptionKey`, a constant published in mRemoteNG's own source.
Upgrading such a database to AES-256-GCM does nothing for the copy sitting in `%APPDATA%`.

## What Changes

- A model loaded from the fallback is marked as such, and a save against it is **refused**, not
  attempted. The refusal reports through the existing not-performed path, so it is visible without
  the log.
- The warning stops claiming "read-only mode" and says what is true: which copy is in use, how old
  it is, and that changes will not be saved until the database is reachable again.
- Recovering is explicit — reconnect and reload — rather than something the user discovers by
  editing and finding out.
- The cache is written with the same protection as the store it copies, and is never written under
  the legacy published key. A store that cannot be cached safely is not cached.

## Impact

- Changed: `mRemoteNG/Connection/ConnectionsService.cs` (fallback path, cache writer), and whatever
  carries the read-only state to `SaveConnections`.
- `encrypt-sql-backend-with-aead` §1's refusal only reaches the user once this lands. Until then a
  too-new database is reported and then silently worked around.
- No user-visible change while the database is reachable, which is the overwhelming majority of the
  time. The change is entirely in what happens when it is not.
