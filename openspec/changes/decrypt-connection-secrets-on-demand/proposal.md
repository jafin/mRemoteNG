## Why

Opening a connection file decrypts every password in it, before the load returns.

`XmlConnectionsDeserializer` collects each encrypted attribute while it walks the XML and hands the
whole list to `ProcessPendingDecrypts`, which decrypts all of them in one batch and assigns each to
its record:

```csharp
string[] plainTexts = _decryptor.DecryptBatch(cipherTexts);

for (int i = 0; i < _pendingDecrypts.Count; i++)
    _pendingDecrypts[i].Setter(plainTexts[i]);
```

`XmlConnectionsDeserializer.cs:180-192`

So a file of two hundred connections puts two hundred passwords into the process to serve the two
the user will actually open, and they stay there for the session. Worse, `DecryptBatch` returns
`string[]`: every secret exists as an immutable, unzeroable string before it is copied into its
record's `SecureString`, and that array is alive until the garbage collector happens to take it.

**This was found while implementing `narrow-connection-password-exposure`, whose proposal asserted
the opposite** — that decryption was deferred per field — citing a line that no longer held that
code. The deferral is real, but it batches the *key derivation* for speed; it does not narrow how
long a secret is in memory. `ConnectionSecretDecryptionTimingTests` pins the current behaviour, and
the capability's requirement was rewritten to state what is true rather than what was assumed.

This exposure is wider than everything that change closed. It was deliberately left open rather than
folded in, because closing it is not a narrowing — it changes when decryption happens, and therefore
when it can fail.

## What Changes

- A record holds its secret as ciphertext until something asks for it, and decrypts on first read.
- `SecurePassword` and the plain-text property both trigger that read, so no caller changes.
- The decrypted value is cached in the existing `SecureString` field, so a connection opened twice
  derives once.
- A save writes back the ciphertext a record never decrypted, rather than decrypting it in order to
  re-encrypt it — **except where the key changed**, which is the dangerous case and is called out
  below.
- `DecryptBatch` stays. It is what makes a *forced* full decryption tolerable, and rekey needs one.

## Impact

`mRemoteNG/Config/Serializers/ConnectionSerializers/Xml/XmlConnectionsDeserializer.cs`,
`mRemoteNG/Connection/AbstractConnectionRecord.cs`, `mRemoteNG/Config/Serializers/XmlConnectionsDecryptor.cs`,
and every consumer that reads a secret without expecting the read to fail.

### The three risks, in the order they matter

**1. A rekey must force full decryption.** `ConnectionFileRekey` gives the store a new key and the
following save re-encrypts the contents under it. If a record still holds ciphertext from the *old*
key and the saver passes it through untouched, the file ends up with contents encrypted under two
different keys — half of which nothing can ever decrypt again. This is the one outcome here that
destroys data rather than annoying somebody, and the pass-through optimisation is what creates it.
The same applies to a master-password change and to hardening a classic store.

**2. Failure moves from load to connect.** Today a wrong key fails once, at load, where the user is
already being asked about the file. Lazily, it fails the first time each connection is opened —
which reads as "this server rejected my password" rather than "this file did not open". A silent
empty password would be worse still: several callers treat an empty secret as "no password
configured" and fall through to a configured default.

**3. It cannot be verified by a green suite alone.** Every protocol reads its secret through this
path. The manual matrix from `narrow-connection-password-exposure` §4.4 applies again in full.

### What it is worth

Not much on its own, and the proposal it came from was right that an attacker who can read this
process's memory has already won by most measures. What makes it worth doing is the ratio: the
window is currently *every password in the file, for the whole session*, and the connections a user
opens are a handful. It also removes the `string[]` entirely, which is the only place in the load
path where a secret exists in a form that cannot be zeroed at all.

There is a plausible secondary benefit and it should not be claimed without measuring: startup does
this work today, and the batch exists because PBKDF2 at 600,000 iterations dominated it. Skipping it
should make opening a large file faster. Whether it does is a task, not a premise.

**Schedule after `add-connection-file-key-slots` lands.** That change introduces the rekey, and risk
1 is a direct interaction with it.
