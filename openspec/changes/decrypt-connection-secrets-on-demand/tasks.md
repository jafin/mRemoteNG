# Tasks

Depends on `add-connection-file-key-slots`, which introduces the rekey. Task 3.1 exists because of
it, and getting 3.1 wrong writes a file whose contents are encrypted under two different keys.

Do not start before it ships.

## 1. Hold ciphertext until something asks

- [ ] 1.1 Give the record a pending-secret slot beside each `SecureString`: the ciphertext, and whatever resolves it. Populated by the deserializer instead of assigning a decrypted value.
- [ ] 1.2 Decrypt on first read, from **both** the plain-text property and the `SecureString` accessor, and cache into the existing field. Two doors to one secret and only one of them lazy would give different answers depending on which a caller used — the defect `narrow-connection-password-exposure` §2.4 tests against.
- [ ] 1.3 Resolve exactly once under concurrent reads. The tree is read from the UI thread and from the host-status monitor, and two threads decrypting the same record must not produce two `SecureString`s of which one is silently dropped and never disposed.
- [ ] 1.4 Assigning a secret discards the pending ciphertext. Otherwise a record edited before its first read would decrypt over the top of the user's new value on the next read.
- [ ] 1.5 Do not defer what the load already needs. The root sentinel is decrypted to validate the key, and that must keep happening at load — it is what turns "wrong key" into one honest failure instead of one per connection.

## 2. Failure has to stay legible

- [ ] 2.1 A secret that fails to decrypt raises the failure to the user against **that connection**, and never resolves to an empty string. Several callers read empty as "no password configured" and fall through to the configured default password — a lazy failure that returns empty would send the wrong credentials to a host rather than reporting anything.
- [ ] 2.2 Report a failed decrypt once per record, not once per read. The property grid re-reads constantly; a failure per repaint is a wall of dialogs over one broken field.
- [ ] 2.3 Keep the load-time key check as the first line. 1.5 is what makes 2.1 rare: a wrong key should still be caught at load, and 2.1 exists for the single corrupt attribute in an otherwise good file.
- [ ] 2.4 Tests: a corrupt attribute fails against its own connection and leaves every other connection openable; the failure is reported once; nothing resolves to empty.

## 3. Saving

- [ ] 3.1 **A save that changes the key must decrypt everything first.** Rekey, master-password change and hardening a classic store all re-encrypt the contents; a record still holding old-key ciphertext that is passed through unchanged produces a file encrypted under two keys, and the half under the old key is unrecoverable. Force a full `DecryptBatch` on those paths and treat any failure as a refusal to save, not as a field to skip. This is the one destructive outcome in this change.
- [ ] 3.2 An ordinary save may write back the ciphertext of a record that was never read. Same key, same bytes, nothing to gain by decrypting them.
- [ ] 3.3 Confirm 3.2 against the AEAD path specifically: passing ciphertext through unchanged means the nonce is not regenerated for that field. That is correct — the plaintext did not change either, so it is the same message under the same key, not nonce reuse across different plaintexts. Record the reasoning where the pass-through is written; a reader who spots "reused nonce" and not "identical message" will otherwise fix a bug that is not there.
- [ ] 3.4 Export writes a classic copy at a different level and re-encrypts everything, so it is a key change by 3.1's definition. Confirm it takes that path.
- [ ] 3.5 Tests: a rekey forces decryption and the file afterwards decrypts wholly under the new key; an ordinary save of an untouched store leaves the secret bytes identical; a save after editing one connection rewrites that one and passes the rest through.

## 4. Consumers

- [ ] 4.1 Audit every reader of `Password`, `RDGatewayPassword` and `VNCProxyPassword` for one that cannot tolerate a first read doing work — anything holding a lock, on a paint path, or inside a `catch`.
- [ ] 4.2 `CredentialImportHelper.HasPassword` asks whether a secret exists for every node in a file being imported. Under this change that question decrypts the whole file one record at a time, which is worse than the batch it replaced. Answer it from the pending ciphertext's presence instead.
- [ ] 4.3 Confirm the property grid does not decrypt a secret merely by displaying the row. It renders `PasswordPropertyText`, so it should not need the value — if it does, selecting a folder would decrypt every child.
- [ ] 4.4 Tests: reading a secret from two threads decrypts once; a record whose secret is never read never decrypts; importing a file does not decrypt it.

## 5. Verification

- [ ] 5.1 Full build; zero new analyzer warnings.
- [ ] 5.2 Full test suite; zero failures, no `[Ignore]`.
- [ ] 5.3 `openspec validate decrypt-connection-secrets-on-demand --strict`.
- [ ] 5.4 Measure the startup claim rather than asserting it. Open a file of ~200 connections before and after; report the number. The proposal says this *should* be faster because the batch is skipped, and a proposal that guessed wrong about this file's behaviour once already should not guess twice.
- [ ] 5.5 Manual, and required — the same matrix as `narrow-connection-password-exposure` §4.4, because every protocol reads its secret through this path: RDP with a saved password, an inherited password, a gateway with its own credentials, an external credential provider, Remote Credential Guard, restricted admin; SSH, SFTP and a file transfer.
- [ ] 5.6 Manual: rekey a store with connections that were never opened in that session, then reopen the file and connect to one of them. This is risk 1, and it is the check that a green suite cannot make convincing.
- [ ] 5.7 Manual: open a file, connect to one connection, save, and confirm the other connections' stored secrets are byte-identical to what they were.
