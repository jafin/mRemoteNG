## ADDED Requirements

### Requirement: Secrets are decrypted when required, not when the file is opened

The system SHALL decrypt a stored secret when that secret is first required, and SHALL NOT decrypt
every secret in a connection file as part of opening it.

Opening a file of two hundred connections otherwise places two hundred passwords in the process, for
the lifetime of the session, to serve the handful a user actually opens.

#### Scenario: Opening a connection file

- **WHEN** a connection file is loaded
- **THEN** no connection's secret is decrypted as part of loading

#### Scenario: Opening a connection

- **WHEN** a secret is required for the first time
- **THEN** it is decrypted and held as a `SecureString` on its record
- **AND** a later read of the same secret does not decrypt it again

#### Scenario: Two readers at once

- **WHEN** two threads require the same secret at the same time
- **THEN** it is decrypted once
- **AND** both obtain the same value

### Requirement: A secret that cannot be decrypted is reported, never returned empty

The system SHALL report a failed decryption against the connection it belongs to, and SHALL NOT
return an empty secret in its place.

An empty secret is not a neutral answer. Several callers read empty as "no password configured" and
fall through to the configured default password, so a failure that returned empty would send the
wrong credentials to a host instead of reporting anything. Deferring decryption is what makes this
reachable at all: today the key is proven at load, once.

#### Scenario: One unreadable secret

- **WHEN** a connection's stored secret cannot be decrypted
- **THEN** the failure is reported against that connection
- **AND** no empty secret is supplied in its place
- **AND** every other connection in the file remains usable

#### Scenario: The file's key is wrong

- **WHEN** a connection file is opened with a key that does not decrypt it
- **THEN** the failure is reported once while opening the file
- **AND** not once per connection

### Requirement: A save that changes the key decrypts everything first

The system SHALL decrypt every secret before writing a save that re-encrypts a store's contents under
a different key, and SHALL refuse that save if any secret cannot be decrypted. This covers a rekey, a
master-password change, and hardening a classic store.

A record that was never read still holds ciphertext under the *old* key. Writing that through
unchanged produces a file whose contents are encrypted under two different keys, and the half under
the old key can never be decrypted again. This is the one outcome of deferring decryption that
destroys data rather than inconveniencing somebody.

#### Scenario: Rekeying a store with unopened connections

- **WHEN** a store is rekeyed and saved
- **AND** some of its connections were never opened in that session
- **THEN** every secret is decrypted before the file is written
- **AND** the whole file afterwards decrypts under the new key

#### Scenario: A secret cannot be decrypted during a rekey

- **WHEN** a secret cannot be decrypted while re-encrypting the store
- **THEN** the save is refused
- **AND** the file is left as it was

#### Scenario: An ordinary save

- **WHEN** a store is saved without changing its key
- **THEN** a secret that was never read is written back as the ciphertext it was read as
- **AND** a secret that was edited is written re-encrypted
