# Design

## Refuse the save; do not "merge"

The tempting alternative is to reconcile the cached tree with the database when it comes back. It is
the wrong thing to build here, and expensive enough that proposing it would sink the fix.

There is no basis for a merge. The cache records what one client saw at one moment; the database has
since been changed by people whose edits this client never saw. Nothing records *which* of the two
is intended for any given connection, so a merge would be guessing — and guessing wrong silently
resurrects a connection somebody deliberately deleted, which is the failure this change exists to
stop.

Refusing is honest, cheap, and leaves the user's edits on screen where they can be copied out. If
multi-client reconciliation is wanted, it is its own change with its own conflict model.

## Read-only has to be a property of the model, not a setting

`SQLReadOnly` is a user preference. Reusing it would mean writing to the user's settings to record a
transient failure, and then restoring it later — from code that runs when things are already going
wrong. If the restore is missed, the user's database is silently read-only for ever, and the only
clue is a checkbox they did not tick.

So the state belongs to the loaded model: it was loaded from a fallback, therefore it may not be
written back to the source it stands in for. It disappears when the model is replaced by a real
load, which is the only thing that should clear it.

## Say what is true, and what to do

The current message is false in its most important clause. The replacement has to carry three things
the user cannot otherwise find out:

1. **These connections are a local copy** — not what the database currently holds.
2. **How old it is.** A copy from four minutes ago and one from four months ago call for different
   decisions, and only the application knows which this is.
3. **Changes will not be saved.** Said before they make any, not after.

The refusal itself goes through the existing not-performed path, which
`connection-save-durability` already requires to be visible without the log. That requirement exists
because a save that silently does nothing is the worst outcome available — which is precisely what
this defect produces today, only worse, because it does something wrong instead of nothing.

## The cache inherits the store's protection

The cache is a copy of the whole store, so it needs the store's protection, not the default.

The mechanism is already there and is being misused by accident: `DataTableDeserializer` sets
`rootNode.PasswordString` to the decryption key, and `RootNodeInfo.PasswordString`'s setter marks the
store protected only when that value differs from `DefaultPassword`. For a database with a master
password that works — the cache is written under the master password. For a database without one,
the key *is* the default, so the cache is written under `mR3m`.

That is not a bug in `RootNodeInfo`; it is correct for the connection file it was written for. It is
wrong here because a shared database has no per-user secret to fall back on, which is the same gap
`require-sql-master-password` addresses from the other end.

Two options, and the choice is a real one for review:

| | |
|---|---|
| **Harden the cache file** with a per-file key, as `add-connection-file-key-slots` does | Nothing to type, protects the copy, but a machine-bound file that is useless after a profile rebuild — acceptable, since a cache is disposable by definition |
| **Do not cache a store that has no master password** | Simplest and strictest; costs offline access to exactly the databases most likely to be casual |

**Recommendation: harden it.** The cache exists to be useful when the database is unreachable, and
deleting that capability to fix its protection trades one real feature for a property the user can
get another way. A per-file key is the right tool precisely because a cache is disposable: if the
protector is lost, the correct response is to delete the cache, which costs nothing.

## Ordering

This is independent of `encrypt-sql-backend-with-aead` and can land before or after it. It should
land **soon after §1**, because §1's refusal does not reach a user with a cache until it does.
