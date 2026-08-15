# Tasks

Schedule last of the eight. See proposal.md — this shortens an exposure window, where the other
changes decide whether the encryption is meaningful at all.

## 1. Lock in what is already correct

- [x] 1.1 Tests asserting `Password`, `RDGatewayPassword` and `VNCProxyPassword` are backed by `SecureString`, that assignment disposes the previous value, and that an unchanged assignment raises no notification. — `ConnectionRecordSecretStorageTests`, 22 cases across the three secrets. Beyond the three asked for: a secret round-trips through the string property unchanged including non-BMP characters, an unset secret reads as empty rather than null, clearing one disposes it, and — the one that catches a *fourth* secret added later without this treatment — no field on `AbstractConnectionRecord` whose name contains "assword" is a plain `string`. Named fields alone would not have caught that.
- [x] 1.2 Test asserting loading a connection file decrypts no secret. — **Written, and it asserts the opposite, because the opposite is what happens.** `ConnectionSecretDecryptionTimingTests` loads a three-connection store and finds every password already in memory before anything asked for one. The proposal's claim that decryption is deferred per field cites `XmlConnectionsDeserializer.cs:323`, which holds different code now; what the deserializer actually does is collect every encrypted attribute while walking the XML and decrypt **all of them in one batch** before the load returns (`ProcessPendingDecrypts`). The deferral is real but it batches the key derivation for speed — it does not narrow how long a secret is in memory. The batch also materialises them as a `string[]`, immutable and unzeroable, before each is copied into its record's `SecureString`: a wider exposure than the property-getter copies this change was written to narrow. The capability requirement that asserted it has been rewritten to state what is true and to record the gap; see the spec delta.
- [x] 1.3 Do this before anything else. These properties are true today and untested, which is why an automated scan reads the `string` property type and concludes the opposite. — Done first, and it earned its place: the two false premises in the proposal (this one and the SSH one in §2.2) were both found by writing the tests rather than by reading the code.

## 2. SecureString accessors

- [x] 2.1 Add `Browsable(false)` `SecureString` accessors returning a copy the caller owns and disposes. Returning the stored instance would let a caller dispose the record's own secret. — `SecurePassword`, `SecureRDGatewayPassword`, `SecureVNCProxyPassword`. Two of the four resolution routes are answered without a string existing: the record's own value, and a bound credential record. The other two — inheritance and connection links — go through the plain-text property **deliberately**, because they answer by reading another record's string property, including a walk that skips parents holding an empty credential, and a second implementation of those rules would be free to drift from the first. An accessor that returns a different password than the property beside it is worse than the copy it saves. A private sentinel distinguishes "nothing overrode this record" from "something resolved to empty", so the own-value path is reached without materialising anything.
- [x] 2.2 Adopt them in the SSH-backed callers that already deal in `SecureString` — `SshCredentialResolver` and what it feeds. — **The premise is wrong, so the adopter is different.** `SshCredentialResolver` deals in `string` throughout: the external credential providers return strings, the default password unprotects to one, and `ResolvedSshCredential` takes a string and converts it to a `char[]` it zeroes — with a remark already recording that the string entry point is out of scope. Handing it a `SecureString` would mean converting back for every provider branch, which is worse than today. The real round trip is at the **credential-record boundary**, and it is worse than the one proposed: `ConnectionInfo.TryGetCredentialRecordValue` converted a credential record's `SecureString` to a plain string on *every* read of `Password`, and the property grid re-reads a displayed connection constantly. Adopted there, and in `CredentialImportHelper`, which read the password as a string and converted it straight back — and which asked `string.IsNullOrEmpty(connection.Password)` for every node in a file being imported, materialising every password in it purely to learn whether there was one.
- [x] 2.3 Leave the `string` properties untouched. The property grid, the serializers and the inheritance machinery all bind to them. — Untouched. The accessors sit beside them and are `Browsable(false)`.
- [x] 2.4 Tests: the accessor returns an independent copy; disposing it does not affect the record; the `string` property still round-trips. — Nine, and the important ones are not those three. Every resolution route is asserted to **agree with the plain-text property beside it**, including the inheritance walk that skips an empty parent, because drift is the real risk in having two paths to one secret. Also: disposing a copy taken from a credential record does not reach into the credential store, which several connections may share; and a credential record does not answer for the gateway or proxy secret, which would silently authenticate a gateway with the connection's own credentials.

Adding three public properties to `ConnectionInfo` broke fifteen tests across four groups, and the
cause was worth fixing rather than working around. `ConnectionInfo.GetProperties` — which feeds
serialization, connection presets and default-connection copying — enumerated every public property
and filtered only by a hand-maintained name list, so a read-only view was enrolled in all three the
moment it existed. `ConnectionPropertyReflector`, the "single source of truth for which properties
are serializable", already drew the line correctly (`serializable = prop.CanWrite`). The two
disagreed, and `ConstantID` was named by hand in both lists to paper over it. `GetProperties` now
draws the same line, which is what the reflector's own comment says the rule is.

## 3. RDP conversion window

- [ ] 3.1 In `RdpProtocol`, move the password read from line 978 down to the assignment at line 1151.
- [ ] 3.2 Trace every branch between them first — external credential providers, Remote Credential Guard, restricted admin, gateway credentials — and confirm which of them replace the password and which fall through. The local is read by more than one path.
- [ ] 3.3 Leave `AdvancedSettings2.ClearTextPassword` taking a `string`. It is COM; the conversion cannot be removed.
- [ ] 3.4 Tests: each branch supplies the password it did before. Coverage here matters more than the change.

## 4. Verification

- [ ] 4.1 Full build; zero new analyzer warnings.
- [ ] 4.2 Full test suite; zero failures, no `[Ignore]`.
- [ ] 4.3 `openspec validate narrow-connection-password-exposure --strict`.
- [ ] 4.4 Manual against a real RDP host — **required, not optional.** A green suite does not cover the RDP credential paths, and breaking authentication there costs far more than the exposure this closes. Verify: a saved password; an inherited password; a gateway with its own credentials; an external credential provider; Remote Credential Guard; restricted admin.
- [ ] 4.5 Manual: an SSH connection, an SFTP session and a file transfer, confirming the new accessor changed nothing about what authenticates.
