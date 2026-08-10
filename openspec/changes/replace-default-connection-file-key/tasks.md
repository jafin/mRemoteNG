# Tasks

Depends on `harden-connection-file-kdf`, which establishes that the file records its own
cryptographic parameters. Do not start before that lands.

## 1. Export first

- [ ] 1.1 Provide an export of the current connection file to a password-protected file, reachable before any migration prompt appears.
- [ ] 1.2 Tests: an exported file opens on another machine with its password; it carries no machine-bound key.
- [ ] 1.3 This is task 1 deliberately. The migration must not be offered until the escape route exists — see design.md, "Export before migrate".

## 2. Protection declaration

- [ ] 2.1 Add a third sentinel value for the machine-bound case alongside `ThisIsProtected` and `ThisIsNotProtected` in `XmlRootNodeSerializer.cs:41`.
- [ ] 2.2 Make the reader refuse an unrecognised sentinel with a message naming the likely cause, rather than falling through to the legacy key.
- [ ] 2.3 Tests: each of the three values round-trips; an unknown value loads nothing and reports; the two existing values behave exactly as before.

## 3. Machine-bound key

- [ ] 3.1 Generate a random per-file key and wrap it with `ProtectedData.Protect` at `DataProtectionScope.CurrentUser`; store the wrapped blob in a root attribute.
- [ ] 3.2 Unwrap on load; a `CryptographicException` from the unwrap means a different account or profile and must be reported as that, not as a wrong password.
- [ ] 3.3 Confirm the wrapped key does not become the KDF password — the file key is used directly; PBKDF2 has nothing to stretch when the input is already random.
- [ ] 3.4 Tests: round-trip under the same account; unwrap failure reports account binding; the wrapped blob is not a constant across files.

## 4. Legacy key becomes read-only

- [ ] 4.1 Restrict `ConnectionFileDefaults.LegacyEncryptionKey` to the decrypt path; leave it reachable and documented as read-only.
- [ ] 4.2 Leave `SqlConnectionsLoader.cs:86,89` on the legacy default — a per-user key cannot serve a shared database. Record why in the code, not only here.
- [ ] 4.3 Tests: no write path reaches the legacy key on the installed edition; existing files still decrypt.

## 5. Migration

- [ ] 5.1 Prompt on the first save of a legacy file: state that the file becomes account-bound, offer the export from task 1, require confirmation.
- [ ] 5.2 Declining saves under the legacy key and asks again next time. Do not remember the refusal silently — a user who says no once should not be permanently opted out of a security fix without knowing it.
- [ ] 5.3 Detect a connection file living outside the user profile (redirected documents, a network share) and warn more strongly, since those users are the ones DPAPI will surprise.
- [ ] 5.4 Tests: accepting migrates and rewrites the sentinel; declining leaves the file byte-compatible with the old format; the prompt reappears on a later save.

## 6. Portable edition

- [ ] 6.1 Gate the machine-bound path on `Runtime.IsPortableEdition` so portable never takes it.
- [ ] 6.2 Offer a master password on portable instead; on decline, state plainly that the file is protected by a key published in the application's source.
- [ ] 6.3 Tests: portable never writes a machine-bound file; a portable file opens on a second machine.

## 7. Verification

- [ ] 7.1 Full build; zero new analyzer warnings.
- [ ] 7.2 Full test suite; zero failures, no `[Ignore]`.
- [ ] 7.3 `openspec validate replace-default-connection-file-key --strict`.
- [ ] 7.4 Manual: migrate a real file, confirm connections still open, confirm the file no longer decrypts with `mR3m` using an independent script.
- [ ] 7.5 Manual: copy a migrated file to a second Windows account and confirm the message names account binding rather than reporting a bad password.
- [ ] 7.6 Manual: portable edition on two machines from one USB stick, with and without a master password.
- [ ] 7.7 Manual: open a migrated file with the previous release and confirm it refuses with a version message rather than corrupting anything.
