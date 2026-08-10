# Refuse a storage format level this build does not understand

## Why

`StorageFormat.Parse` treats every value that is not `Hardened` as classic — absent, empty, and
unrecognised alike:

```csharp
public static StorageFormatLevel Parse(string? recordedValue) =>
    string.Equals(recordedValue, HardenedValue, StringComparison.OrdinalIgnoreCase)
        ? StorageFormatLevel.Hardened
        : StorageFormatLevel.Classic;
```

`StorageFormat.cs:69`

Collapsing absent and unrecognised was deliberate, and the reason is recorded on that method:

> Unrecognised is deliberately not an error: the value only says how to treat the file, and a file
> this build cannot make sense of is refused by the protection sentinel, which is read before
> anything is decrypted.

**That mitigation does not exist on this path.** `PlaintextValidator = ConnectionFileDefaults.IsKnownSentinel`
is set in exactly one place — `SqlConnectionsLoader.cs:111`. The connection file builds its
authenticator through `XmlConnectionsDecryptor` and never sets a validator, so
`PasswordAuthenticator` accepts any plaintext that decrypts without throwing. The sentinel guards the
SQL store. It does not guard `confCons.xml`.

So the reasoning that made collapsing the two safe holds for one store and not for the other.

### What goes wrong

A build that adds a third level writes `StorageFormat="Quantum"`. This build reads it as **classic**,
opens the file, and the user works normally. The next save writes a classic file: the level is gone,
and with it whatever protection it selected. Nothing is reported, because from this build's point of
view nothing unusual happened.

That is the silent downgrade the level exists to prevent, arriving from the other direction. The
whole of `add-storage-format-opt-in` is built on the rule that **a level is never changed without
being asked for** — and an unknown level is changed to classic by simply opening the file in an older
build.

The same shape is already refused for the SQL store, which throws rather than read a database newer
than it understands (`SqlConnectionsLoader.cs`, `IsNewerThanSupported`). The connection file has no
equivalent.

### Why now, while nothing writes one

Nothing writes an unknown level today, so nothing is broken today. That is exactly the window in
which this is cheap: the rule has to be in the build that ships *before* the one that adds a level,
or the first store at the new level meets a build that quietly downgrades it. Fixing it after the
fact does not help the files already flattened.

## What Changes

- `StorageFormat` distinguishes three outcomes rather than two: **absent** (classic, unchanged),
  **recognised**, and **unrecognised**.
- An unrecognised level is a read failure. The store does not open, and the user is told the file was
  written by a newer build rather than being shown a password prompt or a working-looking tree.
- An unrecognised level is never written back as classic. A file that cannot be read cannot be saved
  over.
- Absence keeps its meaning exactly: every file written before the level existed, and every file
  upstream mRemoteNG has ever written, still opens as classic. This is the property the change must
  not disturb.

## Impact

`mRemoteNG/Security/StorageFormat.cs`,
`mRemoteNG/Config/Serializers/ConnectionSerializers/Xml/XmlConnectionsDeserializer.cs`,
`mRemoteNG/Config/Connections/XmlConnectionsSaver.cs`.

Affects `storage-format-compatibility`. No user-visible change for any store that exists today —
every one of them resolves to absent or `Hardened`.
