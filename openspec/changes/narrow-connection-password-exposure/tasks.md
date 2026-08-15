# Tasks

Schedule last of the eight. See proposal.md — this shortens an exposure window, where the other
changes decide whether the encryption is meaningful at all.

## 1. Lock in what is already correct

- [x] 1.1 Tests asserting `Password`, `RDGatewayPassword` and `VNCProxyPassword` are backed by `SecureString`, that assignment disposes the previous value, and that an unchanged assignment raises no notification. — `ConnectionRecordSecretStorageTests`, 22 cases across the three secrets. Beyond the three asked for: a secret round-trips through the string property unchanged including non-BMP characters, an unset secret reads as empty rather than null, clearing one disposes it, and — the one that catches a *fourth* secret added later without this treatment — no field on `AbstractConnectionRecord` whose name contains "assword" is a plain `string`. Named fields alone would not have caught that.
- [x] 1.2 Test asserting loading a connection file decrypts no secret. — **Written, and it asserts the opposite, because the opposite is what happens.** `ConnectionSecretDecryptionTimingTests` loads a three-connection store and finds every password already in memory before anything asked for one. The proposal's claim that decryption is deferred per field cites `XmlConnectionsDeserializer.cs:323`, which holds different code now; what the deserializer actually does is collect every encrypted attribute while walking the XML and decrypt **all of them in one batch** before the load returns (`ProcessPendingDecrypts`). The deferral is real but it batches the key derivation for speed — it does not narrow how long a secret is in memory. The batch also materialises them as a `string[]`, immutable and unzeroable, before each is copied into its record's `SecureString`: a wider exposure than the property-getter copies this change was written to narrow. See §5 for what this does to the capability.
- [x] 1.3 Do this before anything else. These properties are true today and untested, which is why an automated scan reads the `string` property type and concludes the opposite. — Done first, and it earned its place: the two false premises in the proposal (this one and the SSH one in §2.2) were both found by writing the tests rather than by reading the code.

## 2. SecureString accessors

- [ ] 2.1 Add `Browsable(false)` `SecureString` accessors returning a copy the caller owns and disposes. Returning the stored instance would let a caller dispose the record's own secret.
- [ ] 2.2 Adopt them in the SSH-backed callers that already deal in `SecureString` — `SshCredentialResolver` and what it feeds.
- [ ] 2.3 Leave the `string` properties untouched. The property grid, the serializers and the inheritance machinery all bind to them.
- [ ] 2.4 Tests: the accessor returns an independent copy; disposing it does not affect the record; the `string` property still round-trips.

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
