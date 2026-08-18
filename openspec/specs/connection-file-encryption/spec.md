# connection-file-encryption Specification

## Purpose

How a connection file's contents are keyed: which key encrypts it, what derives that key, who can
unwrap it, and how a file states all of this about itself.

Two rules run through every requirement here. **The file carries its own parameters**, so a copy is
readable wherever it lands and absence keeps its historical meaning rather than acquiring a new one.
And **daily use asks for nothing** — a protection that prompts on every open is one users route
around, so the recovery password exists for the cases the automatic protector cannot serve rather
than as the way in.

What this replaces is a fixed key published in the project's own source: a file with no master
password was encrypted under a constant anyone could read, which meant the encryption stopped
nobody holding a copy.
## Requirements
### Requirement: The connection file states the key derivation parameters it was written with

A connection file at the hardened level SHALL record the PBKDF2 pseudo-random function used to derive
its key, alongside the iteration count it already records, and the reader SHALL derive using the
parameters the file states rather than the parameters currently configured.

A classic file records no function, and absence is what means HMAC-SHA1 — recording it there would
put an attribute upstream mRemoteNG has never seen into a file it is required to be able to read.

A file outlives the build that wrote it. Parameters that are read from configuration at open time
cannot be changed without making every existing file unreadable, which is why the iteration count is
already stored in the file; the PRF is the other half of the same pair and was left implicit.

#### Scenario: A file written by this version

- **WHEN** a connection file at the hardened level is saved
- **THEN** the root element carries the pseudo-random function alongside the iteration count
- **AND** the recorded function is the one used to derive the key

#### Scenario: A file that records a pseudo-random function

- **WHEN** a connection file is opened and records a pseudo-random function
- **THEN** the key is derived with that function
- **AND** the currently configured value is not consulted

### Requirement: A file that predates the recorded PRF is read as SHA-1

The system SHALL derive with PBKDF2-HMAC-SHA1 when a connection file records no pseudo-random
function.

Every file written before this change used SHA-1 and says nothing about it. Absence has to keep
meaning SHA-1 or those files stop opening, which for a connection manager means the user loses their
connections with no way to tell why.

#### Scenario: A file with no recorded function

- **WHEN** a connection file records no pseudo-random function
- **THEN** the key is derived with HMAC-SHA1
- **AND** the file opens with no migration step and no prompt

#### Scenario: A file with no recorded function is saved

- **WHEN** such a file is opened and then saved at the classic level
- **THEN** it is rewritten with no recorded function
- **AND** it continues to derive with HMAC-SHA1

### Requirement: Hardened connection files are written with HMAC-SHA256

The system SHALL derive keys using PBKDF2-HMAC-SHA256 for connection files at the hardened format
level, and SHALL keep using HMAC-SHA1 for files at the classic level.

The 600,000 iterations already configured is OWASP's guidance for HMAC-SHA256. Against HMAC-SHA1 the
same count is roughly half the intended work factor, so pairing the count with the function it was
chosen for costs nothing at runtime and closes the gap without doubling the time to open a file.

It is gated because upstream mRemoteNG reads the same file from the same path and ignores the
attribute that records the function, so writing it would make the file unopenable there while
reporting a wrong password.

#### Scenario: Deriving a key for a hardened file

- **WHEN** a key is derived for a connection file at the hardened level
- **THEN** the pseudo-random function is HMAC-SHA256
- **AND** the iteration count is the configured value

#### Scenario: Deriving a key for a classic file

- **WHEN** a key is derived for a connection file at the classic level
- **THEN** the pseudo-random function is HMAC-SHA1
- **AND** no pseudo-random function is recorded in the file

### Requirement: The key derivation function is given its parameters explicitly

`Pkcs5S2KeyGenerator` SHALL require an iteration count from its caller and SHALL NOT supply a default
for it. The pseudo-random function SHALL default to HMAC-SHA1 when not supplied.

A defaulted iteration count is indistinguishable at the call site from a chosen one. The existing
default of 1000 is three orders of magnitude below the configured value and would silently produce a
weak key from a `new()` that merely forgot an argument.

The pseudo-random function is defaulted rather than required, and defaulted to the *weaker* function,
because the two arguments fail in opposite directions. A forgotten iteration count produces a weak
key and nothing else notices; a forgotten function must produce a file upstream mRemoteNG can still
open, because the alternative is a caller that forgets and locks the user out of the application they
came from. Requiring both would be stronger still and is the better end state, but it is not what
shipped — see the deviation recorded against task 1.1 of `harden-connection-file-kdf`.

#### Scenario: Constructing without an iteration count

- **WHEN** the key derivation function is constructed without an iteration count
- **THEN** it does not compile

#### Scenario: Constructing without a pseudo-random function

- **WHEN** the key derivation function is constructed without a pseudo-random function
- **THEN** the key is derived with HMAC-SHA1
- **AND** the resulting file is readable by upstream mRemoteNG

### Requirement: Derived key caching accounts for the pseudo-random function

The system SHALL treat a change of pseudo-random function as a cache miss when reusing a derived key.

`AeadCryptographyProvider` caches a derived key against the password, salt and iteration count so
that saving a file derives once rather than once per field. Two files can share all three and differ
only in the function, so a cache that ignores it would encrypt one file with the other's key.

#### Scenario: Two derivations differing only by function

- **WHEN** a key is derived, and a second derivation matches on password, salt and iteration count
- **AND** the pseudo-random function differs
- **THEN** the cached key is not reused
- **AND** the key is derived again with the requested function

### Requirement: A hardened connection file is protected by a random per-file key

The system SHALL protect a connection file at the hardened format level with a randomly generated
per-file key, and SHALL NOT derive such a file's key from a value present in the application's source.

A key that is a public constant provides no confidentiality. A file copied to a backup, a sync
folder, a roaming profile or a shared drive is readable by anyone who obtains it, which is the whole
of CVE-2023-30367 and the default state for every user who never opens the security options.

It applies at the hardened level only because upstream mRemoteNG reads the same file from the same
path and does not recognise the sentinel a per-file key requires.

#### Scenario: Saving a hardened file

- **WHEN** a connection file at the hardened level is saved
- **THEN** its contents are encrypted with a random per-file key
- **AND** the legacy default key is not used

#### Scenario: Saving a classic file

- **WHEN** a connection file at the classic level is saved
- **THEN** it keeps the key source upstream mRemoteNG understands
- **AND** no per-file key is written

### Requirement: The per-file key has both a machine protector and a recovery protector

The system SHALL wrap the per-file key with the operating system's per-user data protection and with
a key derived from a user-supplied recovery password, SHALL store both wrapped copies in the file,
and SHALL open the file using whichever protector is available.

A single machine-bound protector makes the file unreadable after a profile rebuild, and because
backups are byte-identical copies of the encrypted file, it makes every backup unreadable with it. A
recovery protector travels with every copy, so confidentiality improves without recoverability
getting worse.

Two cases write the recovery protector alone: the portable edition, and a file outside the user
profile. Both are stated as their own requirements below.

#### Scenario: Opening on the machine that wrote the file

- **WHEN** a protected file with a machine protector is opened by the account that saved it
- **THEN** the key is unwrapped using per-user data protection
- **AND** the user is not prompted

#### Scenario: Opening where per-user data protection cannot help

- **WHEN** a protected file is opened by a different account, on a different machine, or after a
  profile rebuild
- **THEN** the recovery password is requested
- **AND** supplying it decrypts the file

#### Scenario: Neither protector available

- **WHEN** per-user data protection cannot unwrap the key and the recovery password is not supplied
- **THEN** no connection is decrypted
- **AND** the message distinguishes this from a corrupt file

#### Scenario: A protector that unwraps to a key this file was not written with

- **WHEN** a protector yields a key that does not decrypt the file's protection declaration
- **THEN** that key is not accepted
- **AND** the remaining protector is tried

A protector unwrapping proves only that the reader may use it, not that it belongs to this file. A
machine protector left over from an earlier key, or copied from another file the same account owns,
unwraps perfectly and yields the wrong key — and the store then opens onto contents that cannot be
decrypted, with no prompt and nothing reported. The protection declaration is the one ciphertext
whose plaintext is known in advance, so it is what separates a usable protector from a right one.

#### Scenario: A protector that cannot be used is told apart from a wrong secret

- **WHEN** a protector is absent, truncated, or records parameters outside the range this build writes
- **THEN** the recovery password is not requested repeatedly against it
- **AND** the failure is reported as a property of the file rather than of the password

### Requirement: A recovery password is required before a per-file key is written

The system SHALL obtain a recovery password before writing a per-file key for the first time, and
SHALL NOT write a file whose only protector is machine-bound.

A file with one machine-bound protector is lost when the profile is rebuilt, and its entire backup
history is lost with it. The user has no signal that this is so until they need a backup, at which
point every copy they hold is equally unopenable.

#### Scenario: Raising a store to the hardened level

- **WHEN** the user raises a store to the hardened level
- **THEN** a recovery password is requested before the file is written
- **AND** the file is written only once one is supplied

#### Scenario: A store already at the hardened level with no per-file key

- **WHEN** a store declares the hardened format but carries no key protectors
- **THEN** it is offered the per-file key, by the automatic offer and on request alike
- **AND** it is not reported as a store with nothing left to do

A store hardened before per-file keys existed has a stretched KDF over the published default key,
which stretches a constant everybody has. Deciding whether to offer from the declared *level* rather
than from whether a key protector is present would exclude exactly the users who took the earlier
security upgrade, and tell them so.

#### Scenario: Accepting on a store that is already at the hardened level

- **WHEN** such a store is given a per-file key
- **THEN** the store is written, even though its declared level did not change

The level not changing is not the same as nothing changing. A key and its protectors exist only in
memory until the store is saved, so treating "the level was already correct" as "nothing to do"
would walk the user through the whole migration and write none of it.

### Requirement: One storage format confirmation at a time

The system SHALL NOT present more than one storage format confirmation at once.

The offer is raised whenever the store is loaded, and a store reloads for reasons that have nothing
to do with the user — an external edit to the file, a recovery from backup, a switch between files.
Each of those otherwise adds another copy of the question on top of the last, and a question asked
several times cannot be answered once.

#### Scenario: The store reloads while the confirmation is open

- **WHEN** the connection file is reloaded while a storage format confirmation is on screen
- **THEN** no second confirmation is presented

#### Scenario: An automatic offer while the user has opened the confirmation

- **WHEN** the store reloads while a confirmation the user opened from the menu is on screen
- **THEN** no second confirmation is presented

#### Scenario: The user declines to set a recovery password

- **WHEN** the user declines to supply a recovery password
- **THEN** the store stays at the classic level with the protector it already had
- **AND** a file under a user's master password keeps that password
- **AND** a file under the legacy default key keeps that key
- **AND** the level is not raised
- **AND** a file left under the legacy default key is stated to be protected by a key published in the application's source

#### Scenario: A declined store that is not under the legacy default key

- **WHEN** the user declines and the store carries a master password
- **THEN** nothing is said about a published key

The statement is worth making only where it is true. A classic store with a master password is
encrypted under that password, so telling its owner their key is published would be false — and a
warning that turns out to be false is worth less to the person reading the next one than no warning
at all.

A classic file is not always under the legacy default key: it carries a master password whenever the
user set one. Describing every declined migration as remaining under the default key would either
misdescribe those files or, if implemented literally, take their master password off them — the
opposite of what declining a security upgrade should do.

### Requirement: Backups of a protected file remain restorable

The system SHALL ensure that a copy of a protected connection file can be decrypted with the recovery
password on any machine.

`FileBackupCreator` copies the encrypted file without re-encrypting it, so a backup carries exactly
the protectors the original had. Backups exist to survive the loss of the machine, and users direct
them at network shares and synced folders for that reason.

#### Scenario: Restoring a backup on another machine

- **WHEN** a backup of a protected file is opened on a machine that did not write it
- **THEN** the recovery password decrypts it
- **AND** the connections are recovered

#### Scenario: Restoring a backup on the original machine

- **WHEN** a backup of a protected file is opened by the account that wrote it
- **THEN** it opens without a prompt

### Requirement: Automatic recovery distinguishes an unreadable key from a corrupt file

The system SHALL treat a failure to unwrap the per-file key as distinct from a failure to parse the
file, and SHALL NOT iterate over backup files when the key could not be unwrapped.

Automatic recovery walks every backup on a load failure and reports each one it cannot read. A key
that cannot be unwrapped fails identically for every backup, so the user is shown a series of
warnings implying their backups are corrupt when the files are intact and only the protector is out
of reach. Recovery also overwrites the live file with the first backup that parses, so reaching that
path for the wrong reason is destructive.

#### Scenario: The key cannot be unwrapped

- **WHEN** loading fails because the per-file key could not be unwrapped
- **THEN** the backup set is not searched
- **AND** the recovery password is requested
- **AND** the live file is not overwritten

#### Scenario: The file is genuinely corrupt

- **WHEN** loading fails because the file cannot be parsed
- **THEN** the existing backup recovery proceeds as it does today

### Requirement: The file declares which protection it uses

The connection file SHALL declare whether it is protected by a master password, by a per-file key, or
by the legacy default key, and the reader SHALL refuse a declaration it does not recognise.

The reader decides how to obtain a key before it can decrypt anything, so the declaration has to be
readable without one. Treating an unrecognised declaration as the legacy default would mean a build
that predates a future protection attempts `mR3m` against a file that never used it and reports a
wrong password.

#### Scenario: Each protection is declared

- **WHEN** a connection file is written
- **THEN** it declares which of the three protections applies

#### Scenario: An unrecognised declaration

- **WHEN** a connection file declares a protection the build does not recognise
- **THEN** no connection is loaded
- **AND** the message says the file was written by a newer version

### Requirement: Files written with the legacy default key remain readable

The system SHALL decrypt connection files declaring the legacy default key, and SHALL continue to
write under that key at the classic format level.

Fifteen years of files were written this way. Making them unreadable to fix how they are written
would destroy exactly what the change exists to protect, and the classic level is also what keeps a
store readable by upstream mRemoteNG.

#### Scenario: Opening a legacy file

- **WHEN** a connection file declares the legacy default key
- **THEN** it decrypts with that key
- **AND** it opens with no prompt and no migration step

#### Scenario: Saving a legacy file that was not upgraded

- **WHEN** a legacy file is saved and its level was never raised
- **THEN** it is written under the legacy default key
- **AND** upstream mRemoteNG can still open it

### Requirement: The portable edition writes no machine-bound protector

The portable edition SHALL protect a connection file with the recovery password alone, SHALL NOT
write a per-user data protection protector, and SHALL state plainly when a file is left under the
legacy default key.

A protector bound to one Windows account cannot be unwrapped on the next machine, which is the only
thing a portable build is for. Writing one would produce a file the edition cannot open on the
machine it is carried to.

#### Scenario: Saving from the portable edition

- **WHEN** the portable edition saves a protected file
- **THEN** only the recovery-password protector is written

#### Scenario: A portable file moves between machines

- **WHEN** a file written by the portable edition is opened on another machine
- **THEN** the recovery password decrypts it

#### Scenario: The portable edition declines protection

- **WHEN** the portable edition saves and no recovery password is set
- **THEN** the legacy default key is used
- **AND** the user is told the file is protected by a key published in the application's source

The statement itself is not a portable behaviour and is specified above, for either edition. It
belongs here as well because portable is where declining is least recoverable: the installed edition
can offer a machine protector as the easy answer and portable has no account to bind to, so a
portable user who declines has nothing protecting the file but a password they chose not to set.

### Requirement: Files are interchangeable between editions

A connection file written by either edition SHALL be readable by the other, given its recovery
password.

Users move a file between an installed machine and a USB stick, and a format split between the two
editions would turn an ordinary copy into data loss. Both editions write the same format; only the
presence of the machine-bound protector differs.

#### Scenario: An installed file opened by the portable edition

- **WHEN** a file written by the installed edition is opened by the portable edition
- **THEN** the recovery password decrypts it

#### Scenario: A portable file opened by the installed edition

- **WHEN** a file written by the portable edition is opened by the installed edition
- **THEN** the recovery password decrypts it
- **AND** a machine-bound protector may be added when it is next saved

### Requirement: A connection file outside the user profile writes no machine-bound protector

The system SHALL detect a connection file whose location is outside the user's profile and SHALL
protect it with the recovery password alone.

The file carries one machine-bound protector, so on a file several people share it can serve exactly
one of them. Everyone else fails it on every open, is told the file was protected by a different
account, and has no way to make that stop — the protector they would need a slot for does not have
one. A protector only one member of a team can use costs the others a prompt and buys nothing.

The detection cannot be exact: a redirected Documents folder is a share its owner does not know they
have, and a personal file on a NAS is not a team file. It does not need to be. Being wrong in this
direction costs one prompt on a file that would not otherwise have prompted; being wrong the other
way costs every other member of a team a prompt they can never remove.

#### Scenario: Saving a file that lives outside the user profile

- **WHEN** a protected connection file outside the user profile is saved
- **THEN** only the recovery-password protector is written

#### Scenario: A shared file opened by someone who did not write it

- **WHEN** a protected connection file outside the user profile is opened by another account
- **THEN** the recovery password decrypts it
- **AND** no failure of a machine-bound protector is reported, because none was written

#### Scenario: A file inside the user profile is unaffected

- **WHEN** a protected connection file in the user's own profile is saved
- **THEN** both protectors are written
- **AND** the account that saved it is not prompted when it opens it

### Requirement: A recovery password supplied in a session is not requested again

The system SHALL retain a recovery password for the lifetime of the session once it has opened a
file, SHALL request it again after a restart, and SHALL discard it whenever the store is locked.

Where the machine protector is absent or cannot be used — a shared file, the portable edition, a
backup restored on another machine or under another account — the recovery password is what opens the
store, and the store is re-read more than once per session:
after an external change, and on the automatic recovery path. Prompting each time would turn a
password meant to be typed rarely into one typed constantly, which is how a user ends up choosing a
short one.

Discarding it on lock is what keeps this from defeating `AutoLockOnMinimize`, whose entire purpose is
that walking away requires re-authentication.

#### Scenario: The store is re-read during a session

- **WHEN** the recovery password has opened the store and the store is read again in the same session
- **THEN** the password is not requested again

#### Scenario: The application is restarted

- **WHEN** the application is restarted and the store is opened
- **THEN** the recovery password is requested

#### Scenario: The store is locked

- **WHEN** the store is locked
- **THEN** the retained recovery password is discarded
- **AND** opening the store again requests it

### Requirement: A local copy of a store carries the store's protection, never the published key

The system SHALL protect a local cached copy of a connection store at least as well as the store it
copies, and SHALL NOT write such a copy under `ConnectionFileDefaults.LegacyEncryptionKey`.

The SQL connections cache is written on every successful load, through the ordinary connection-file
saver with a default save filter, so it holds every password in the store. Its key comes from the
root node's `PasswordString`, which is marked protected only when it differs from the default — so a
database with no master password produces a local file encrypted under a constant published in
mRemoteNG's own source code. Upgrading that database to authenticated encryption does nothing for
the copy in `%APPDATA%`, and the copy is on a workstation rather than a server.

#### Scenario: Caching a store with no master password

- **WHEN** a store with no master password is cached locally
- **THEN** the copy is not written under the legacy published key

#### Scenario: Caching a store with a master password

- **WHEN** a store protected by a master password is cached locally
- **THEN** the copy is protected at least as well

#### Scenario: A store that cannot be cached safely

- **WHEN** a local copy cannot be given protection at least equal to the store's
- **THEN** no copy is written
- **AND** the user is told that offline access is unavailable for this store

