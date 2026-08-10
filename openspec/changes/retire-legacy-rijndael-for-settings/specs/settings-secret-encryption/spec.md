## ADDED Requirements

### Requirement: Settings secrets are encrypted with authenticated encryption

The system SHALL encrypt secrets stored in application settings using AES-256-GCM with a key derived
by PBKDF2, and SHALL NOT write them with the legacy MD5-keyed provider.

An unsalted MD5 of the application key is one hash away from the plaintext, and AES-CBC without a tag
lets a stored secret be altered without detection. These values include the default credential
password and the SQL Server password, which reach live connections.

#### Scenario: Storing a secret

- **WHEN** a secret is written to application settings
- **THEN** it is encrypted with AES-256-GCM
- **AND** it carries its own salt and nonce

#### Scenario: A stored secret is altered

- **WHEN** an encrypted settings value is modified outside the application
- **THEN** decryption fails and is reported
- **AND** no value is returned

### Requirement: Stored secrets declare which provider produced them

Encrypted settings values SHALL carry a marker identifying the provider that produced them, and a
value with no marker SHALL be read with the legacy provider.

Every value written before this change is unmarked legacy ciphertext, and the two formats are not
distinguishable by inspection. Without a marker the reader must guess, and guessing wrong against an
unauthenticated cipher returns plausible bytes rather than an error.

#### Scenario: Reading a value written before this change

- **WHEN** an encrypted settings value carries no marker
- **THEN** it is decrypted with the legacy provider
- **AND** the user is not prompted

#### Scenario: Reading a value written after this change

- **WHEN** an encrypted settings value carries the authenticated-encryption marker
- **THEN** it is decrypted with authenticated encryption

#### Scenario: A marker the build does not recognise

- **WHEN** a value carries a marker the build does not recognise
- **THEN** decryption fails and is reported
- **AND** the legacy provider is not attempted as a fallback

### Requirement: Stored secrets migrate when rewritten

The system SHALL rewrite a settings secret with authenticated encryption whenever that secret is
saved, and SHALL NOT require a migration pass over settings that are not being changed.

A value nobody edits is a value nobody is exposing further. Rewriting the whole settings file on
upgrade risks every stored secret on one operation, to fix values that are no more at risk tomorrow
than today.

#### Scenario: Saving an existing secret

- **WHEN** a secret previously stored in the legacy format is saved
- **THEN** it is written with authenticated encryption and the current marker

#### Scenario: A secret that is never edited

- **WHEN** settings are saved and a legacy secret is unchanged
- **THEN** it is left in its existing form
- **AND** it continues to decrypt

### Requirement: One component decides which provider protects a settings secret

The system SHALL resolve the cryptography provider for settings secrets through a single component,
and call sites SHALL NOT construct a provider directly.

Six secrets currently pick their own provider by constructing it inline at eleven call sites. That is
how they all ended up on the weak one, and a seventh added the same way would do the same.

#### Scenario: Adding a secret

- **WHEN** a new settings secret is encrypted or decrypted
- **THEN** it obtains its provider from the shared component
- **AND** no call site constructs a cryptography provider directly
